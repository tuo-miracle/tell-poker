using System;
using System.Collections.Generic;
using System.Globalization;
using Poker.AI;
using UnityEngine;

namespace Poker.Characters
{
    public enum UtteranceState { Queued, Playing, Done, Interrupted, Dropped }

    /// <summary>一句話（字幕原文＋它的聲音）。</summary>
    public sealed class Utterance
    {
        public string Text;
        /// <summary>預錄 id（拼接時用 + 連起來）、"tts"（語音對話整段回話）、"stream"（串流回話）或 "none"（沒有聲音，程式錯誤）。</summary>
        public string ClipId;
        public string LineId;
        public AudioClip Clip;
        public SpeechPriority Priority;
        public bool IsStream, OwnsClip;
        /// <summary>音檔長度（秒）；串流在結束前是 -1。</summary>
        public float Duration;
        /// <summary>開始播之後過了幾秒。</summary>
        public float Elapsed;
        /// <summary>排隊等了幾秒。</summary>
        public float Waited;
        /// <summary>這一句排隊最多等幾秒（&lt; 0＝依 Priority 的預設 SpeechChannel.MaxWait）。即時台詞用 1.5 s：過時了就不講。</summary>
        public float MaxWait = -1f;
        /// <summary>呼叫端自訂的標記（例如 "live"：玩家出手時一起丟掉）。</summary>
        public string Tag;
        /// <summary>沒講就被丟掉時呼叫（原因：stale／floor busy／busy／呼叫端給的原因）。</summary>
        public Action<string> OnDropped;
        public UtteranceState State;
        /// <summary>送出的先後（整桌的發言權用來決定同等級誰先講）。</summary>
        internal long Seq;
        /// <summary>曾經因為別人佔著發言權而等（丟掉時記 floor busy）。</summary>
        internal bool FloorWait;
    }

    /// <summary>
    /// 一個角色的說話管道（每個角色一個）：頭上的字幕泡泡和聲音同進同出。
    /// - 字在那句聲音開始播的同一刻出現、就是那句的原文；聲音播完再留 ReadTail 秒閱讀時間就收起來。
    /// - 同時只講一句：比正在講的更重要（SpeechPriority）就打斷，字和聲音一起換；否則排隊（太久的閒聊丟掉、Filler 忙就不講）。
    ///   別句絕不會蓋掉正在講的字（2026-10-13 實機：字被其他台詞蓋掉而和聲音對不上）。
    /// - 串流回話（語音對話）用 SayStream：第一塊音訊開始播時呼叫，播完（LivePlaybackEnd）呼叫 EndStream。
    /// - 每一句開始都記 log：[Speech] who clip=id dur=秒 text="…"（串流在結束時記，dur 是實際播放秒數）。
    /// - 加入整桌的發言權（SpeechFloor）時，同時只有一個角色在講，換人前停 SpeechFloor.Gap 秒（規則見 SpeechFloor）。
    /// 時間由角色的 Tick 推進（牌局暫停時 AudioListener 也暫停，字和聲音一起停）。
    /// </summary>
    public sealed class SpeechChannel : MonoBehaviour
    {
        /// <summary>聲音停了之後字幕再留的閱讀時間（秒）。</summary>
        public const float ReadTail = 0.5f;
        /// <summary>排隊的下一句和上一句之間的停頓（秒）。</summary>
        public const float Gap = 0.15f;
        /// <summary>串流沒人呼叫 EndStream 時的安全上限（秒）。</summary>
        public const float StreamSafetySeconds = 45f;

        /// <summary>排隊最多等多久（秒），過了就丟掉：閒聊很快就過時，重要的提示可以等久一點。</summary>
        public static float MaxWait(SpeechPriority p) =>
            p == SpeechPriority.Filler ? 0f : p == SpeechPriority.Chatter ? 3f : p == SpeechPriority.Info ? 12f : 20f;

        public static string VoiceFor(CharacterKind k) => SpeechVoices.ForSeat(k);

        /// <summary>false 時由角色的 Tick 推進（預設；角色本身有 AutoUpdate）。</summary>
        public bool AutoUpdate;
        /// <summary>聲音 id（pip／rocco／vex／dot），也是 log 裡的名字。</summary>
        public string Who { get; private set; } = "?";
        /// <summary>找預錄音檔用的聲音鍵：預設＝Who；寫實角色坐上來時 UseVoice 換成角色自己的聲音（SpeechVoices）。</summary>
        public string Voice { get; private set; } = "?";
        public AudioSource Source { get; private set; }
        public SubtitleBubble Bubble { get; private set; }
        /// <summary>正在播的那句（沒有就是 null）。</summary>
        public Utterance Current { get; private set; }
        /// <summary>頭上字幕顯示的那句（正在播的、或剛播完還在閱讀時間的）。</summary>
        public Utterance Shown { get; private set; }
        public bool IsSpeaking => Current != null;
        public bool IsShowingText => Bubble != null && Bubble.IsVisible;
        public string ShownText => IsShowingText && Shown != null ? Shown.Text : null;
        /// <summary>上一句聲音停了多久（秒；從沒講過是很大的數）。</summary>
        public float SinceEnd { get; private set; } = 999f;
        public int QueuedCount => _queue.Count;
        /// <summary>上一句播完（或被打斷）的那句。</summary>
        public Utterance LastEnded { get; private set; }
        /// <summary>比這個不重要的話先排隊、不開口（玩家正在講話時，VoiceChatController 設成 Info）。</summary>
        public SpeechPriority HoldBelow = SpeechPriority.Filler;
        /// <summary>整桌的發言權（GameController 設定；沒有就只管自己這個角色）。</summary>
        public SpeechFloor Floor { get; internal set; }
        /// <summary>最近一次送出的話（排隊中的也算）。</summary>
        public Utterance LastSubmitted { get; private set; }
        public event Action<Utterance> Started;
        public event Action<Utterance> Ended;
        /// <summary>動嘴（角色設定成 CharacterPerformer.Talk）。</summary>
        public Action<float> TalkFor;

        readonly List<Utterance> _queue = new List<Utterance>();
        float _gapLeft, _tailLeft;

        public void Setup(string who, SubtitleBubble bubble, Transform head)
        {
            Who = who;
            Voice = who;
            Bubble = bubble;
            var go = new GameObject("Voice_" + who);
            go.transform.SetParent(head != null ? head : transform, false);
            Source = go.AddComponent<AudioSource>();
            Source.playOnAwake = false;
            Source.spatialBlend = 1f;
            Source.minDistance = 0.4f;
            Source.maxDistance = 6f;
            Source.rolloffMode = AudioRolloffMode.Linear;
            Source.dopplerLevel = 0f;
        }

        /// <summary>換聲音（寫實角色坐進這個座位）；台詞內容不變，只換找音檔的聲音鍵。空字串＝回到座位聲音。</summary>
        public void UseVoice(string voice)
        {
            if (string.IsNullOrEmpty(voice)) voice = Who;
            if (voice == Voice) return;
            Voice = voice;
            Debug.Log($"[Speech] {Who} voice={Voice}");
        }

        void Update()
        {
            if (AutoUpdate) Tick(Time.deltaTime);
        }

        void OnDestroy()
        {
            StopAll();
            Floor?.Leave(this);
        }

        // ───────────── 說話 ─────────────

        /// <summary>
        /// 說一句預錄台詞（用聲音＋原文找預錄音檔；有數字就用片段拼）。
        /// 角色的聲音沒錄這句（陣容換成沒預錄的組合）時退回座位聲音並警告：不能沒聲音，但也不能默默變成別人的聲音。
        /// </summary>
        public Utterance Say(string text, SpeechPriority priority = SpeechPriority.Chatter, string lineId = null)
        {
            var a = SpeechBank.Resolve(Voice, text);
            if (a == null && Voice != Who && !string.IsNullOrEmpty(text))
            {
                a = SpeechBank.Resolve(Who, text);
                if (a != null) Debug.LogWarning($"[Speech] {Who}: voice {Voice} has no clip for \"{text}\"; using the seat voice {Who} (bake this lineup: tools/voice-proxy node bake/bake.ts)");
            }
            if (a == null) return Submit(new Utterance { Text = text, ClipId = "none", Priority = priority, LineId = lineId });
            return Submit(new Utterance { Text = text, ClipId = a.Id, Clip = a.Clip, OwnsClip = a.OwnsClip, Priority = priority, LineId = lineId });
        }

        /// <summary>說一句，聲音是給定的音檔（語音對話的整段回話：clipId = "tts"）。</summary>
        public Utterance Say(string text, AudioClip clip, string clipId, SpeechPriority priority, bool ownsClip = false) =>
            Submit(new Utterance { Text = text, Clip = clip, ClipId = clip != null ? clipId : "none", OwnsClip = ownsClip && clip != null, Priority = priority });

        /// <summary>串流回話：在第一塊音訊開始播的那一刻呼叫（立刻開始，打斷比它不重要的）；播完呼叫 EndStream。</summary>
        public Utterance SayStream(string text, AudioClip clip, SpeechPriority priority = SpeechPriority.Reply)
        {
            var u = new Utterance { Text = text, Clip = clip, ClipId = "stream", IsStream = true, Duration = -1f, Priority = priority, OwnsClip = true };
            u.Seq = Floor != null ? Floor.NextSeq() : 0;
            LastSubmitted = u;
            if (Current != null) Interrupt(Current, u);
            Floor?.MayStart(this, u, out _);   // 串流不能等：切掉別人的閒聊（或記重疊），馬上播
            _queue.Remove(u);
            Start(u);
            return u;
        }

        public void EndStream(Utterance u)
        {
            if (u == null || u != Current) return;
            Finish(u, UtteranceState.Done);
        }

        /// <summary>丟掉排隊中符合條件的話（正在講的不動）；回傳丟了幾句。觸發的情境不在了（例如玩家已經出手）時用。</summary>
        public int DropQueued(Func<Utterance, bool> match, string why)
        {
            int n = 0;
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                var q = _queue[i];
                if (!match(q)) continue;
                _queue.RemoveAt(i);
                Drop(q, why);
                n++;
            }
            return n;
        }

        /// <summary>停掉一切（角色消失、新牌局）。</summary>
        public void StopAll()
        {
            foreach (var q in _queue) { q.State = UtteranceState.Dropped; Release(q); }
            _queue.Clear();
            if (Current != null) Finish(Current, UtteranceState.Interrupted);
            HideText();
        }

        Utterance Submit(Utterance u)
        {
            LastSubmitted = u;
            if (Floor != null) u.Seq = Floor.NextSeq();
            if (string.IsNullOrEmpty(u.Text)) { u.State = UtteranceState.Dropped; Release(u); return u; }
            if (u.Clip != null) u.Duration = u.Clip.length;
            else u.Duration = Mathf.Max(1.2f, 0.5f + u.Text.Length * 0.06f);   // 沒有聲音：至少讓字停留到讀得完
            bool held = u.Priority < HoldBelow;
            if (Current != null && !held && Interrupts(u, Current)) { Interrupt(Current, u); Start(u); return u; }
            SpeechChannel blocker = null;
            if (Current == null && !held && _gapLeft <= 0f && !HasQueuedAtLeast(u.Priority))
            {
                if (Floor == null || Floor.MayStart(this, u, out blocker)) { Start(u); return u; }
            }
            if (u.Priority == SpeechPriority.Filler) { Drop(u, blocker != null ? "floor busy" : "busy", blocker != null); return u; }
            if (blocker != null) WaitFor(u, blocker);
            u.State = UtteranceState.Queued;
            int at = _queue.Count;
            while (at > 0 && _queue[at - 1].Priority < u.Priority) at--;   // 重要的排前面，同等級先來先講
            _queue.Insert(at, u);
            return u;
        }

        static bool Interrupts(Utterance next, Utterance cur) =>
            next.Priority > cur.Priority || (next.Priority == SpeechPriority.Reply && cur.Priority == SpeechPriority.Reply);

        /// <summary>被別人切掉（整桌的發言權：回話切掉閒聊）。</summary>
        internal void CutForFloor(Utterance by)
        {
            if (Current != null) Interrupt(Current, by);
        }

        /// <summary>排隊中下一句會講的話（被 HoldBelow 擋住的不算）。</summary>
        internal Utterance NextEligible()
        {
            foreach (var q in _queue) if (q.Priority >= HoldBelow) return q;
            return null;
        }

        void WaitFor(Utterance u, SpeechChannel blocker)
        {
            if (u.FloorWait) return;
            u.FloorWait = true;
            Debug.Log($"[Speech] floor: {Who} waits for {blocker.Who}");
        }

        bool HasQueuedAtLeast(SpeechPriority p)
        {
            foreach (var q in _queue) if (q.Priority >= p) return true;
            return false;
        }

        void Start(Utterance u)
        {
            u.State = UtteranceState.Playing;
            u.Elapsed = 0f;
            Current = u;
            Shown = u;
            _tailLeft = 0f;
            if (Bubble != null) Bubble.Show(u.Text, float.PositiveInfinity);
            if (Source != null)
            {
                Source.Stop();
                Source.clip = u.Clip;
                if (u.Clip != null) Source.Play();
            }
            TalkFor?.Invoke(u.IsStream ? 0.25f : Mathf.Max(0.2f, u.Duration * 0.9f));
            if (!u.IsStream)
            {
                var line = Line(u, u.Duration);
                if (u.Clip == null) Debug.LogWarning(line); else Debug.Log(line);
            }
            Started?.Invoke(u);
        }

        void Interrupt(Utterance cur, Utterance by)
        {
            Debug.Log($"[Speech] {Who} interrupted clip={cur.ClipId} by clip={by.ClipId} after {cur.Elapsed.ToString("0.00", CultureInfo.InvariantCulture)}s");
            Finish(cur, UtteranceState.Interrupted);
            _gapLeft = 0f;
        }

        void Finish(Utterance u, UtteranceState state)
        {
            if (u.IsStream) Debug.Log(Line(u, u.Elapsed));
            u.State = state;
            if (Source != null && Source.clip == u.Clip) { Source.Stop(); Source.clip = null; }
            Release(u);
            if (Current == u) Current = null;
            LastEnded = u;
            SinceEnd = 0f;
            _gapLeft = Gap;
            _tailLeft = ReadTail;
            Ended?.Invoke(u);
        }

        void Drop(Utterance u, string why, bool floor = false)
        {
            u.State = UtteranceState.Dropped;
            Debug.Log($"[Speech] {(floor ? "floor: " : "")}{Who} dropped ({why}) clip={u.ClipId} text=\"{u.Text}\"");
            Release(u);
            u.OnDropped?.Invoke(why);
        }

        static void Release(Utterance u)
        {
            if (u.OwnsClip && u.Clip != null) { Destroy(u.Clip); u.OwnsClip = false; }
        }

        void HideText()
        {
            Shown = null;
            if (Bubble != null) Bubble.Hide();
        }

        string Line(Utterance u, float dur) =>
            $"[Speech] {Who} clip={u.ClipId} dur={dur.ToString("0.00", CultureInfo.InvariantCulture)} text=\"{u.Text}\"";

        // ───────────── 推進 ─────────────

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            var u = Current;
            if (u != null)
            {
                u.Elapsed += dt;
                if (u.IsStream)
                {
                    TalkFor?.Invoke(0.25f);
                    if (u.Elapsed >= StreamSafetySeconds) Finish(u, UtteranceState.Done);
                }
                else if (u.Elapsed >= u.Duration) Finish(u, UtteranceState.Done);
            }
            else
            {
                SinceEnd += dt;
                if (Shown != null)
                {
                    _tailLeft -= dt;
                    if (_tailLeft <= 0f) HideText();
                }
            }

            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                var q = _queue[i];
                q.Waited += dt;
                if (q.Waited > (q.MaxWait >= 0f ? q.MaxWait : MaxWait(q.Priority))) { _queue.RemoveAt(i); Drop(q, q.FloorWait ? "floor busy" : "stale", q.FloorWait); }
            }

            if (_gapLeft > 0f) _gapLeft -= dt;
            if (Current == null && _gapLeft <= 0f)
                for (int i = 0; i < _queue.Count; i++)
                {
                    if (_queue[i].Priority < HoldBelow) continue;
                    var next = _queue[i];
                    if (Floor != null && !Floor.MayStart(this, next, out var blocker))
                    {
                        if (blocker != null) WaitFor(next, blocker);
                        break;
                    }
                    _queue.RemoveAt(i);
                    Start(next);
                    break;
                }
        }
    }
}
