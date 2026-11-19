using System;
using System.Collections.Generic;
using Poker.AI;
using Poker.Characters;
using Poker.Core;
using Poker.Game;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Poker.Voice
{
    /// <summary>
    /// 語音對話接到牌桌：掛在 GameController 的物件上（Poker.UX 在牌局初始化後掛上）。
    /// 注視來自 GameController（Quest 3 是頭部朝向）；收音用 UnityEngine.Microphone。
    /// 回話（整段或串流）一律交給回話角色的 SpeechChannel：聲音在角色頭上的 3D AudioSource 播放，字幕在聲音開始時出現、播完才收（聽障玩家）。
    /// 沒拿到聲音的回話（離線備案、配音失敗）改說有預錄聲音的備案句，字幕就是那句。
    /// 只有玩家同意（首次使用的同意畫面）且在 Settings 打開 Voice chat 時才開麥克風。
    /// </summary>
    public sealed class VoiceChatController : MonoBehaviour, IVoiceTable
    {
        /// <summary>false 時不在 Update 推進，由測試呼叫 Tick。</summary>
        public bool AutoUpdate = true;

        public GameController Game { get; private set; }
        public VoiceSession Session { get; private set; }
        public VoiceConfig Config { get; private set; }
        public VoiceActionLog ActionLog { get; } = new VoiceActionLog();
        /// <summary>麥克風真的開著（同意、打開、有權限而且 Microphone.Start 成功）。</summary>
        public bool MicOn => _micOn;

        IMicSource _mic;
        bool _micOn;
        float[] _buf = new float[4096];
        double _time;
        int _cannedN;
        string[] _names;
        /// <summary>角色講完後再等這麼久才收音（避免收到尾音）。</summary>
        public const float QuietAfterSpeech = 0.25f;

        /// <summary>streams：/live 串流；沒給時只有正式環境（transport 也沒給）才用真的 WebSocket，測試傳了假 HTTP 就不串流。</summary>
        public static VoiceChatController Attach(GameController game, IMicSource mic = null, IVoiceTransport transport = null, VoiceConfig config = null, IVoiceStreamTransport streams = null)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (game.TryGetComponent<VoiceChatController>(out var existing)) return existing;
            var v = game.gameObject.AddComponent<VoiceChatController>();
            if (streams == null && transport == null) streams = new WebSocketStreamTransport();
            v.Setup(game, mic ?? new UnityMicSource(), transport ?? new UnityWebRequestTransport(), config ?? VoiceConfig.Load(), streams);
            return v;
        }

        void Setup(GameController game, IMicSource mic, IVoiceTransport transport, VoiceConfig config, IVoiceStreamTransport streams)
        {
            Game = game;
            Config = config;
            _mic = mic;
            Session = new VoiceSession(this, new VoiceProxyClient(transport, config), config, mic.SampleRate, streams);
            game.CoreEvents += OnCoreEvents;
        }

        void OnDestroy()
        {
            if (Game != null) Game.CoreEvents -= OnCoreEvents;
            StopMic();
            StopLive();
        }

        /// <summary>同意與開關（UX 設定改變時呼叫）。兩者都 true 才會開麥克風。</summary>
        public void SetEnabled(bool consented, bool userEnabled)
        {
            Session.Consented = consented;
            Session.UserEnabled = userEnabled;
            if (consented && userEnabled) RequestMicPermission();
            UpdateMic();
        }

        void RequestMicPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone)) { Session.MicAllowed = true; return; }
            Session.MicAllowed = false;
            var cb = new PermissionCallbacks();
            cb.PermissionGranted += _ => { Session.MicAllowed = true; UpdateMic(); Debug.Log("[Voice] microphone permission granted"); };
            cb.PermissionDenied += _ => { Session.MicAllowed = false; UpdateMic(); Debug.LogWarning("[Voice] microphone permission denied; voice chat stays off"); };
            Permission.RequestUserPermission(Permission.Microphone, cb);
#endif
        }

        void UpdateMic()
        {
            bool want = Session.Enabled;
            if (want && !_micOn) _micOn = _mic.Start();
            else if (!want && _micOn) StopMic();
        }

        void StopMic()
        {
            if (!_micOn) return;
            _mic.Stop();
            _micOn = false;
        }

        void Update()
        {
            // 暫停（選單、摘下頭盔）時 GameController.AutoStep 為 false：語音也停
            if (!AutoUpdate || Game == null || !Game.AutoStep) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (Game == null || !Game.IsInitialized) return;
            _time += dt;
            int n = 0;
            if (_micOn)
            {
                // 一幀可能有好幾塊；全部讀完
                int got;
                while ((got = _mic.Read(_buf)) > 0)
                {
                    Session.Tick(n == 0 ? dt : 0f, _buf, got);
                    n += got;
                    if (got < _buf.Length) break;
                }
            }
            if (n == 0) Session.Tick(dt, null, 0);
            // 玩家正在講話：閒聊等級的台詞先排隊別開口（不然會打斷收音）
            var hold = Session.State == VoiceState.Capturing ? SpeechPriority.Info : SpeechPriority.Filler;
            foreach (var c in Game.AllCharacters()) c.Speech.HoldBelow = hold;
            // 即時牌桌對話：玩家開口時不接話（也是一個訊號：PlayerSpoke）
            Game.PlayerSpeaking = Session.State == VoiceState.Capturing;
            TickVoice();
        }

        // ================= IVoiceTable =================

        public VoiceCharacter Gazed
        {
            get
            {
                if (Game == null || !Game.IsInitialized) return VoiceCharacter.None;
                int id = Game.GazedId >= 0 ? Game.GazedId : Game.GazeCandidate;
                return FromGazeId(id);
            }
        }

        /// <summary>已經「盯著」（GameController 滿 1 秒）就一定超過 ListenDwell；否則是候選目標累積的秒數。</summary>
        public float GazeDwell
        {
            get
            {
                if (Game == null || !Game.IsInitialized) return 0f;
                return Game.GazedId >= 0 ? Mathf.Max(Game.GazeDwell, Config.ListenDwell) : Game.GazeDwell;
            }
        }

        public bool AnyCharacterSpeaking
        {
            get
            {
                if (_live != null) return true;
                if (Game == null || !Game.IsInitialized) return false;
                foreach (var c in Game.AllCharacters())
                {
                    var sp = c.Speech;
                    if (sp.IsSpeaking ? IsConversation(sp.Current) : sp.SinceEnd < QuietAfterSpeech && IsConversation(sp.LastEnded)) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 半雙工只看「對話」的聲音（回話與等回話的「Hm?」）：和以前一樣；牌桌閒聊在玩家開口時本來就先排隊（HoldBelow），
        /// 如果連閒聊、報牌都算，荷官常講話會讓玩家幾乎插不上話。
        /// </summary>
        static bool IsConversation(Utterance u) => u != null && (u.Priority == SpeechPriority.Reply || u.Priority == SpeechPriority.Filler);

        public PublicTableState BuildState()
        {
            var names = Names();
            return PublicTableState.From(Game.Session.State, names, Game.Session.HumanSeat, ActionLog.Actions);
        }

        public string DirectivesJson()
        {
            var list = new List<(VoiceCharacter, PerformanceCue)>();
            foreach (var seat in Game.Session.AiSeats)
            {
                var c = Game.Character(seat);
                list.Add((FromSeat(seat), c != null ? c.CurrentCue : null));
            }
            return VoiceDirectives.ToJson(list);
        }

        public string VoicesJson()
        {
            if (Game == null || !Game.IsInitialized) return "{}";
            var list = new List<(VoiceCharacter, string)>();
            foreach (var seat in Game.Session.AiSeats)
            {
                var c = Game.Character(seat);
                if (c != null && c.Speech != null) list.Add((FromSeat(seat), c.Speech.Voice));
            }
            if (Game.Dealer != null && Game.Dealer.Speech != null) list.Add((VoiceCharacter.Dot, Game.Dealer.Speech.Voice));
            return VoiceCast.VoicesJson(list);
        }

        public void OnListening(VoiceCharacter who)
        {
            var c = CharacterOf(who);
            if (c == null) return;
            // 「在聽」：保留目前的情緒與破綻（AI 的表演不中斷），只把視線轉向玩家
            var cue = Clone(c.CurrentCue);
            cue.Look = LookTarget.Player;
            cue.LineId = null;
            c.Play(cue);
            Debug.Log($"[Voice] listening to {VoiceCast.Id(who)}");
        }

        public void OnFiller(VoiceCharacter who)
        {
            var c = CharacterOf(who);
            if (c == null) return;
            // 等回話：轉頭看玩家、出聲「Hm?」蓋過等待（忙著講別的就不出聲；有預錄聲音，字幕同步）
            var cue = Clone(c.CurrentCue);
            cue.Look = LookTarget.Player;
            cue.LineId = "voice.filler";
            c.Play(cue);
            c.Speech.Say(VoiceLines.Filler(who), SpeechPriority.Filler, "voice.filler");
        }

        public void Speak(VoiceCharacter who, string text, float[] samples, int sampleRate)
        {
            var c = CharacterOf(who) ?? Game.Dealer;
            if (c == null) return;
            if (CharacterOf(who) == null) who = VoiceCharacter.Dot;
            StopLive();
            LookAtPlayer(c, "voice.reply");
            if (samples != null && samples.Length > 0 && sampleRate > 0)
            {
                var clip = AudioClip.Create("voice_" + VoiceCast.Id(who), samples.Length, 1, sampleRate, false);
                clip.SetData(samples, 0);
                c.Speech.Say(text, clip, "tts", SpeechPriority.Reply, ownsClip: true);
                return;
            }
            // 沒有聲音（離線備案、配音失敗）：備案句都有預錄聲音（有數字的用片段拼）；
            // 模型的回話原文沒有預錄 → 改說罐頭句，字幕跟著換（絕不顯示沒有聲音的字）
            if (string.IsNullOrEmpty(text) || !SpeechBank.CanSay(VoiceCast.Id(who), text, out _))
            {
                var canned = VoiceFallback.CannedLine(who, _cannedN++);
                Debug.Log($"[Voice] no audio for \"{text}\"; saying the baked line \"{canned}\" instead");
                text = canned;
            }
            c.Speech.Say(text, SpeechPriority.Reply, "voice.fallback");
        }

        public void BeginVoice(VoiceCharacter who, string text, int sampleRate)
        {
            StopLive();
            var c = CharacterOf(who) ?? Game.Dealer;
            if (c == null) return;
            var pcm = new PcmStream(sampleRate);
            // 串流 AudioClip：音訊執行緒用 PCMReaderCallback 從環形緩衝拿資料（不夠就補靜音）
            var clip = AudioClip.Create("voice_live_" + VoiceCast.Id(who), sampleRate * 60, 1, sampleRate, true, data => pcm.Read(data, 0, data.Length));
            _live = new LiveVoice { Char = c, Pcm = pcm, Clip = clip, Text = text };
            LookAtPlayer(c, "voice.reply");
        }

        public void FeedVoice(byte[] pcm16, int count)
        {
            if (_live == null) return;
            _live.Pcm.Write(pcm16, 0, count);
            if (_live.Utt == null && _live.Pcm.Buffered >= Config.PlaybackPrebufferSeconds * _live.Pcm.SampleRate) StartLive();
        }

        public void EndVoice()
        {
            if (_live == null) return;
            _live.Pcm.Complete();
            if (_live.Utt == null && _live.Pcm.Written > 0) StartLive();
        }

        /// <summary>正在串流播放的回話（null＝沒有）。</summary>
        public PcmStream LiveStream => _live?.Pcm;

        sealed class LiveVoice
        {
            public CharacterPerformer Char;
            public PcmStream Pcm;
            public AudioClip Clip;
            public string Text;
            /// <summary>開始播之後才有（字幕在這一刻出現）。</summary>
            public Utterance Utt;
            public double StartedAt;
            public readonly LivePlaybackEnd End = new LivePlaybackEnd();
        }

        LiveVoice _live;

        /// <summary>第一塊音訊（預先緩衝夠了）開始播：字幕和聲音一起出現。</summary>
        void StartLive()
        {
            _live.StartedAt = _time;
            _live.Utt = _live.Char.Speech.SayStream(_live.Text, _live.Clip);
        }

        /// <summary>串流播放的收尾見 LivePlaybackEnd；停了之後 QuietAfterSpeech 秒才收音（AnyCharacterSpeaking）。</summary>
        void TickVoice()
        {
            if (_live == null) return;
            var l = _live;
            if (l.Utt != null)
            {
                if (l.Utt.State != UtteranceState.Playing) { _live = null; return; }   // 被打斷（例如新的回話）
                float dur = l.Pcm.DurationSeconds;
                double played = _time - l.StartedAt;
                var end = l.End.Tick(l.Pcm.IsComplete, l.Pcm.Drained, l.Pcm.Played, _time, l.StartedAt, dur);
                if (end != LivePlaybackEnd.Result.Continue)
                {
                    Debug.Log($"[Voice] playback end {end} dur={dur:0.00}s wall={played:0.00}s written={l.Pcm.Written} played={l.Pcm.Played} dropped={l.Pcm.Dropped}");
                    StopLive();
                }
            }
            else if (l.Pcm.IsComplete) StopLive();
        }

        void StopLive()
        {
            if (_live == null) return;
            var l = _live;
            _live = null;
            if (l.Utt != null) l.Char.Speech.EndStream(l.Utt);
            else Destroy(l.Clip);   // 還沒開始播就結束：沒有字幕也沒有聲音
        }

        void LookAtPlayer(CharacterPerformer c, string lineId)
        {
            var cue = Clone(c.CurrentCue);
            cue.Look = LookTarget.Player;
            cue.LineId = lineId;
            c.Play(cue);
        }

        // ================= 對照 =================

        public CharacterPerformer CharacterOf(VoiceCharacter who)
        {
            if (Game == null || !Game.IsInitialized || who == VoiceCharacter.None) return null;
            if (who == VoiceCharacter.Dot) return Game.Dealer;
            foreach (var seat in Game.Session.AiSeats)
                if (FromSeat(seat) == who) return Game.Character(seat);
            return null;
        }

        /// <summary>角色頭上的 3D 語音 AudioSource（SpeechChannel 的；找不到角色就用荷官的）。</summary>
        public AudioSource VoiceOf(VoiceCharacter who)
        {
            var c = CharacterOf(who) ?? (Game != null && Game.IsInitialized ? Game.Dealer : null);
            return c != null ? c.Speech.Source : null;
        }

        VoiceCharacter FromGazeId(int id)
        {
            if (id < 0) return VoiceCharacter.None;
            if (id == GameController.DealerGazeId) return VoiceCharacter.Dot;
            return Game.Character(id) != null ? FromSeat(id) : VoiceCharacter.None;
        }

        VoiceCharacter FromSeat(int seat) => VoiceCast.Parse(Game.Session.AiAt(seat)?.Personality.Name);

        string[] Names()
        {
            int n = Game.Session.SeatCount;
            if (_names != null && _names.Length == n) return _names;
            _names = new string[n];
            for (int i = 0; i < n; i++) _names[i] = i == Game.Session.HumanSeat ? "you" : (Game.Session.NameOf(i) ?? "seat" + i).ToLowerInvariant();
            return _names;
        }

        void OnCoreEvents(IReadOnlyList<GameEvent> events) => ActionLog.Observe(events, Names());

        static PerformanceCue Clone(PerformanceCue c)
        {
            c = c ?? new PerformanceCue();
            return new PerformanceCue
            {
                Seat = c.Seat, Mood = c.Mood == Mood.Startled ? Mood.Neutral : c.Mood, Intensity = c.Intensity, Tell = c.Tell, Look = c.Look, LookSeat = c.LookSeat,
                LineId = c.LineId, Subtitle = null, Duration = c.Duration,
            };
        }
    }
}
