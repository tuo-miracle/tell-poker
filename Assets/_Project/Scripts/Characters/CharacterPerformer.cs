using System;
using System.Collections.Generic;
using Poker.AI;
using UnityEngine;

namespace Poker.Characters
{
    /// <summary>
    /// 把 PerformanceCue 演出來。只吃抽象指令（情緒、破綻、看哪裡、台詞），不知道 AI 的牌；換寫實角色時換掉這層即可。
    /// 動畫全部是程式插值，不用 Animator；每幀只更新約 30 個 Transform。
    /// </summary>
    public sealed class CharacterPerformer : MonoBehaviour
    {
        /// <summary>false 時不在 Update 推進，改由外部呼叫 Tick（測試與截圖工具用）。</summary>
        public bool AutoUpdate = true;

        public CharacterSpec Spec { get; private set; }
        public CharacterKind Kind => Spec.Kind;
        public CharacterRig Rig { get; private set; }
        public SubtitleBubble Subtitle { get; private set; }
        /// <summary>這個角色的說話管道：字幕泡泡和聲音同進同出（見 SpeechChannel）。</summary>
        public SpeechChannel Speech { get; private set; }
        public PerformanceCue CurrentCue => _cue;
        public bool IsGazedAt => _gazed;
        /// <summary>這一幀的臉（寫實角色預覽用）。</summary>
        public FaceState Face { get; private set; }
        /// <summary>這一幀眼睛看的點（世界座標，含掃視）；寫實角色的眼骨頭照這個轉。</summary>
        public Vector3 GazePoint { get; private set; }
        /// <summary>這一幀頭的角度（度）：x 低頭、y 往右轉、z 歪頭（含抖動）。</summary>
        public Vector3 HeadAngles { get; private set; }
        /// <summary>最後一次 Tick 的 Time.frameCount（寫實角色預覽用：遊戲自己 Tick 角色時，它在 LateUpdate 跟上）。</summary>
        public int LastTickFrame { get; private set; } = -1;

        /// <summary>目前破綻的實際演出幅度（強度 × 個性 × 注視放大）。</summary>
        public float TellAmplitude { get; private set; }
        /// <summary>離散破綻動作（偷瞄、撥籌碼、吞口水…）累計次數。</summary>
        public int TellActionCount { get; private set; }
        public int BlinkCount { get; private set; }
        /// <summary>身體前後傾角（度），正值往前、負值往後靠。</summary>
        public float LeanDegrees { get; private set; }
        /// <summary>正在眨眼（截圖工具用來避開閉眼的那一幀）。</summary>
        public bool IsBlinking => _blinkT >= 0;

        /// <summary>牌桌中央（世界座標）；沒設就用角色前方 0.55 m。</summary>
        public Vector3? TableCenter;
        /// <summary>外部提供看向目標的世界座標（例如其他座位）；回傳 null 用預設。</summary>
        public Func<LookTarget, int, Vector3?> LookPointResolver;
        /// <summary>台詞開始：參數是 LineId（沒有就是空字串）。音效層在這裡播咕噥聲。</summary>
        public event Action<CharacterPerformer, string> LineStarted;

        const float GazeAmpMax = 1.75f;

        PerformanceCue _cue = new PerformanceCue();
        float _cueAge, _time;
        Transform _viewer;
        bool _gazed;
        float _gazeAmp = 1f, _spotted, _gazedFor;
        System.Random _rng = new System.Random(1);
        readonly float[] _moodW = new float[Enum.GetValues(typeof(Mood)).Length];
        float _seedOffset;

        // 破綻排程
        float _tellNext, _tellPhase = -1, _tellDur, _tellW;
        Tell _activeTell;
        int _avoidSide = 1;

        // 待機
        float _idleNext, _idlePhase = -1, _idleDur;
        Vector3 _idleTarget;
        float _blinkNext, _blinkT = -1;
        bool _doubleBlink;
        float _saccadeNext;
        Vector2 _saccade;

        // 被嚇
        float _stZ, _stV, _stY, _stYV, _shake, _armsUp;

        // 平滑後的狀態
        float _lean, _yaw, _pitch, _roll, _shrug, _cardLift, _handsTogether, _hbh, _leanInHands, _smile;
        HeadSymbol _symbol = HeadSymbol.None;
        float _symbolAge, _symbolOut;
        readonly float[] _dropAge = { -1, -1, -1 };
        readonly int[] _dropSide = { 1, -1, 1 };
        float _dropNext;
        float _talkLeft;
        Vector3 _handL, _handR;
        bool _handsInit;

        internal void Init(CharacterSpec spec, CharacterRig rig, int seed)
        {
            Spec = spec;
            Rig = rig;
            _rng = new System.Random(seed);
            _seedOffset = (float)_rng.NextDouble() * 100f;
            Subtitle = new SubtitleBubble(transform, BubbleLocal);
            Rig.Bubble = Subtitle.Transform;
            Speech = gameObject.AddComponent<SpeechChannel>();
            Speech.Setup(SpeechChannel.VoiceFor(spec.Kind), Subtitle, Rig.Head);
            Speech.TalkFor = Talk;
            _blinkNext = Rand(0.5f, Spec.BlinkInterval);
            _idleNext = Rand(3f, 6f);
            _moodW[(int)Mood.Neutral] = 1;
            Apply(0);
        }

        /// <summary>泡泡原本的位置（角色本地座標：頭的左上方）；實際位置再夾進玩家的舒適視野（BubblePlacement）。</summary>
        public static readonly Vector3 BubbleLocal = new Vector3(-0.1f, 0.64f, -0.12f);
        /// <summary>這個角色的泡泡位置（角色本地座標）；預設 BubbleLocal，寫實角色預覽會移到頭的旁邊。</summary>
        public Vector3 BubbleAnchor = BubbleLocal;

        /// <summary>頭的半徑（公尺，泡泡避開頭用）：卡通頭約 0.16；寫實角色由 RealisticBody 設成真人的大小。</summary>
        public float HeadRadius = 0.17f;
        /// <summary>上身（胸口為中心）從玩家看過去的半徑（公尺）：泡泡不躲在別人身體後面。</summary>
        const float BodyRadius = 0.22f;

        /// <summary>場上所有角色（泡泡避開別人的頭與泡泡；只看同一張牌桌底下的）。</summary>
        static readonly List<CharacterPerformer> Everyone = new List<CharacterPerformer>();
        void OnEnable() { if (!Everyone.Contains(this)) Everyone.Add(this); }
        void OnDisable() => Everyone.Remove(this);

        void OnDestroy() => Subtitle?.Dispose();

        readonly List<Vector3> _bubbleCands = new List<Vector3>();
        // 候選：左右（BubbleAnchor 那一側優先）、高度（原本、頭的高度、往下到胸口）、往牌桌中間挪（公尺）
        static readonly float[] SideSteps = { 1f, -1f, 0f, 1.5f, -1.5f }, DropSteps = { 0f, 0f, -0.2f, -0.35f }, ForwardSteps = { 0f, 0.3f, 0.5f };
        readonly List<(Vector3, float)> _heads = new List<(Vector3, float)>();
        readonly List<(Vector3, float, float)> _bubbles = new List<(Vector3, float, float)>();
        readonly List<(Vector3, float)> _bodies = new List<(Vector3, float)>();

        /// <summary>
        /// 泡泡位置：先試 BubbleAnchor，蓋到誰的頭（含自己）或別人的泡泡就換：頭的另一邊、更外側、頭的高度、胸口高度、往牌桌中間挪
        /// （BubblePlacement.Place；都會先夾進玩家的舒適視野）。
        /// </summary>
        Vector3 PlaceBubble(float halfWidth, float halfHeight)
        {
            var head = Rig.Head != null ? transform.InverseTransformPoint(Rig.Head.position) : Vector3.up * 0.5f;
            float dx = BubbleAnchor.x - head.x, dy = BubbleAnchor.y - head.y;
            float side = Mathf.Max(Mathf.Abs(dx), HeadRadius + halfWidth + 0.04f);
            _bubbleCands.Clear();
            _bubbleCands.Add(transform.TransformPoint(BubbleAnchor));
            float s = Mathf.Sign(dx) * side;
            _bubbleCands.Add(transform.TransformPoint(head + new Vector3(0f, HeadRadius + halfHeight + 0.03f, 0f)));   // 頭上方
            for (int f = 0; f < ForwardSteps.Length; f++)
                for (int iy = 0; iy < DropSteps.Length; iy++)
                    for (int ix = 0; ix < SideSteps.Length; ix++)
                        _bubbleCands.Add(transform.TransformPoint(head + new Vector3(s * SideSteps[ix], iy == 0 ? dy : DropSteps[iy], ForwardSteps[f])));
            _heads.Clear();
            _bubbles.Clear();
            _bodies.Clear();
            _heads.Add((Rig.Head != null ? Rig.Head.position : transform.position, HeadRadius));   // 第一個是自己（BubblePlacement.Place 的歸屬規則）
            foreach (var o in Everyone)
            {
                if (o == null || o.transform.parent != transform.parent || o.Rig == null || o.Rig.Head == null) continue;
                if (o == this) continue;
                _heads.Add((o.Rig.Head.position, o.HeadRadius));
                if (o.Rig.Chest != null) _bodies.Add((o.Rig.Chest.position, BodyRadius));
                if (o.Subtitle != null && o.Subtitle.IsVisible)
                {
                    float k = o.Subtitle.ContentScale * 0.5f;
                    _bubbles.Add((o.Subtitle.Transform.position, o.Subtitle.Size.x * k, o.Subtitle.Size.y * k));
                }
            }
            return BubblePlacement.Place(_bubbleCands, ViewerPoint(), TableCenterPoint(), halfWidth, halfHeight, _heads, _bubbles, _bodies, transform.position.y);
        }

        void Update()
        {
            if (AutoUpdate && Rig != null) Tick(Time.deltaTime);
        }

        // ───────────── 公開 API ─────────────

        public void Play(PerformanceCue cue)
        {
            if (cue == null) cue = new PerformanceCue();
            var prev = _cue;
            _cue = cue;
            _cueAge = 0;
            if (cue.Mood == Mood.Startled) Startle(Mathf.Max(0.5f, cue.Intensity));
            if (cue.Tell != _activeTell || prev == null)
            {
                _activeTell = cue.Tell;
                _tellPhase = -1;
                _tellW = 0;
                _tellNext = Rand(0.15f, 0.5f);
                if (IsContinuous(cue.Tell)) TellActionCount++;
                _avoidSide = _rng.NextDouble() < 0.5 ? -1 : 1;
            }
            _idleNext = Mathf.Max(_idleNext, Rand(3f, 5f) / Spec.IdleBusy);
            // 台詞交給這個角色的 SpeechChannel：預錄聲音開始播的同時顯示同一句字幕，和其他句子排隊或依重要程度打斷，
            // 不會有「字沒聲音」或「字被別句蓋掉」（2026-10-13 實機回饋；先前的做法是乾脆不顯示字）
            if (!string.IsNullOrEmpty(cue.Subtitle)) Speech.Say(cue.Subtitle, SpeechPriority.Chatter, cue.LineId);
            if (cue.LineId != null || !string.IsNullOrEmpty(cue.Subtitle)) LineStarted?.Invoke(this, cue.LineId ?? "");
        }

        /// <summary>嘴巴正在動（講話）。</summary>
        public bool IsTalking => _talkLeft > 0;

        /// <summary>動嘴 seconds 秒（SpeechChannel 播放時呼叫；本身不顯示字）。</summary>
        public void Talk(float seconds) => _talkLeft = Mathf.Max(_talkLeft, seconds);

        public void SetViewer(Transform head) => _viewer = head;

        public void SetGazedAt(bool gazed)
        {
            if (gazed && !_gazed) { _spotted = 0.5f; _gazedFor = 0; }
            _gazed = gazed;
        }

        /// <summary>被嚇一跳（例如玩家摔牌）。strength 0–1+。</summary>
        public void Startle(float strength = 1f)
        {
            _stV = -1.45f * strength;
            _stYV = 0.75f * strength;
            _armsUp = Mathf.Clamp01(strength);
            _shake = 1f;
        }

        /// <summary>顯示或隱藏角色自帶的蓋牌與籌碼（牌桌層有自己的物件時可以關掉）。</summary>
        public void SetPropsVisible(bool visible) => Rig.Props.gameObject.SetActive(visible);

        public void Tick(float dt)
        {
            if (Rig == null || dt <= 0) return;
            LastTickFrame = Time.frameCount;
            Speech.Tick(dt);   // 說話用真實的經過時間（和 AudioSource 一起走），不受下面的夾值影響
            dt = Mathf.Min(dt, 0.1f);
            _time += dt;
            _cueAge += dt;
            if (_cue.Duration > 0 && _cueAge >= _cue.Duration)
            {
                // 指令時間到：回到待機（看著玩家）
                _cue = new PerformanceCue { Seat = _cue.Seat, Look = LookTarget.Player };
                _cueAge = 0;
                if (_activeTell != Tell.None) { _activeTell = Tell.None; _tellPhase = -1; }
            }
            Apply(dt);
        }

        // ───────────── 主迴圈 ─────────────

        void Apply(float dt)
        {
            var s = Spec;
            float intensity = Mathf.Clamp01(_cue.Intensity);

            // 注視放大
            _gazeAmp = Util.Damp(_gazeAmp, _gazed ? GazeAmpMax : 1f, 4f, dt);
            if (_gazed) _gazedFor += dt;
            _spotted = Mathf.Max(0, _spotted - dt);
            float freq = 1f + (_gazeAmp - 1f) * 0.8f;

            // 情緒權重
            float moodRate = _cue.Mood == Mood.Startled ? 18f : 5f;
            for (int i = 0; i < _moodW.Length; i++)
                _moodW[i] = Util.Damp(_moodW[i], i == (int)_cue.Mood ? 1f : 0f, moodRate, dt);
            float strength = Mathf.Lerp(0.55f, 1.15f, intensity) * Mathf.Lerp(1f, s.Expressiveness, 0.7f) * (1f + (_gazeAmp - 1f) * 0.35f);
            if (_cue.Mood == Mood.Startled) strength = Mathf.Max(strength, 1f);
            var e = new Expression();
            int dom = 0;
            for (int i = 0; i < _moodW.Length; i++)
            {
                if (_moodW[i] < 0.001f) continue;
                e.AddScaled(Expression.For((Mood)i), _moodW[i] * strength);
                if (_moodW[i] > _moodW[dom]) dom = i;
            }
            var domExpr = Expression.For((Mood)dom);

            // 破綻幅度
            _tellW = Util.Damp(_tellW, _activeTell == Tell.None ? 0 : 1, 5f, dt);
            float A = _activeTell == Tell.None ? 0 : Mathf.Clamp(intensity * s.Expressiveness * _gazeAmp, 0, 2f);
            TellAmplitude = A;
            float At = A * _tellW;

            // 排程離散動作
            TickTellAction(dt, freq);
            float actT = _tellPhase >= 0 ? _tellPhase / _tellDur : -1;
            float act = actT >= 0 ? Util.Bell(actT, 0.2f, 0.25f) : 0;

            // 被嚇的彈簧
            const float k = 110f;
            float c = 2f * 0.38f * Mathf.Sqrt(k);
            _stV += (-k * _stZ - c * _stV) * dt; _stZ += _stV * dt;
            _stYV += (-k * _stY - c * _stYV) * dt; _stY += _stYV * dt;
            _shake = Mathf.Max(0, _shake - dt * 1.6f);
            _armsUp = Mathf.Max(0, _armsUp - dt * 0.9f);

            // ── 身體 ──
            bool freeze = _activeTell == Tell.Freeze;
            float leanT = s.BaseLean + e.Lean;
            if (_activeTell == Tell.LeanBack) leanT -= 12f * At;
            if (_activeTell == Tell.LeanIn) leanT += 11f * At;
            if (_activeTell == Tell.StareDown) leanT += 3f * At;
            if (_spotted > 0 && s.OnGazed == GazeResponse.LookAway) leanT -= 3f;
            _lean = Util.Damp(_lean, leanT, 4f * s.Snappiness, dt);
            float breathHz = Mathf.Max(0.12f, 0.27f + e.Breath);
            float breathAmp = (freeze ? 0.15f : 1f) * (1f + Mathf.Max(0, e.Breath));
            float breath = Mathf.Sin((_time * breathHz + _seedOffset) * Mathf.PI * 2f);
            float bounce = e.Bounce > 0.05f ? Mathf.Abs(Mathf.Sin(_time * 7f)) * 0.012f * Mathf.Min(1, e.Bounce) : 0;
            float sway = freeze ? 0 : Mathf.Sin(_time * 0.37f + _seedOffset) * 1.5f * s.IdleBusy;
            float startleLean = _stZ / 0.08f * 12f;
            float shiver = _shake * Mathf.Sin(_time * 55f) * 2.5f;
            float pitch = _lean + startleLean + breath * 0.4f * breathAmp;
            Rig.Hips.localPosition = Rig.HipsRest + new Vector3(shiver * 0.002f, _stY * 0.6f + bounce, _stZ);
            Rig.Hips.localRotation = Quaternion.Euler(pitch, sway, shiver);
            LeanDegrees = pitch;
            Rig.Chest.localScale = new Vector3(1 + 0.012f * breath * breathAmp, 1 + 0.022f * breath * breathAmp, 1 + 0.012f * breath * breathAmp);

            float shrugT = Mathf.Clamp(e.Shrug + (_activeTell == Tell.Swallow ? act * 0.5f * At : 0), -0.6f, 1.3f);
            _shrug = Util.Damp(_shrug, shrugT, 8f, dt);
            Rig.Neck.localPosition = Rig.NeckRest + new Vector3(0, -0.022f * _shrug + 0.003f * breath * breathAmp, 0);

            // ── 視線 ──
            var look = ResolveGaze(e, act, At, dt, out float headFollow);
            var hipsLocal = Rig.Hips.InverseTransformPoint(look) - Rig.NeckRest;
            float yawT = Mathf.Atan2(hipsLocal.x, hipsLocal.z) * Mathf.Rad2Deg;
            float pitchT = -Mathf.Atan2(hipsLocal.y - Rig.HeadRest.y, new Vector2(hipsLocal.x, hipsLocal.z).magnitude) * Mathf.Rad2Deg;
            yawT = Mathf.Clamp(yawT * headFollow, -55f, 55f);
            pitchT = Mathf.Clamp(pitchT * headFollow * 0.85f, -25f, 30f) + e.HeadPitch;
            if (_activeTell == Tell.GlanceAtCards) pitchT += 6f * act * Mathf.Min(1.3f, At);
            if (_activeTell == Tell.Swallow) pitchT += Mathf.Sin(Mathf.Clamp01(actT) * Mathf.PI * 2f) * 6f * At * (actT >= 0 ? 1 : 0);
            if (_activeTell == Tell.StareDown) pitchT += 5f * At;
            float rollT = e.HeadRoll * (s.Kind == CharacterKind.Rookie ? -1 : 1);
            float headRate = (freeze ? 14f : 7f) * s.Snappiness;
            _yaw = Util.Damp(_yaw, yawT, headRate, dt);
            _pitch = Util.Damp(_pitch, pitchT, headRate, dt);
            _roll = Util.Damp(_roll, rollT, 4f, dt);
            float headShake = _shake * Mathf.Sin(_time * 48f) * 7f;
            float tremor = Mathf.Max(0, e.Tremor) * (_activeTell == Tell.None ? 1f : 1f + At * 0.3f);
            float headTremor = tremor * (Mathf.PerlinNoise(_time * 9f, _seedOffset) - 0.5f) * 1.6f;
            Rig.Neck.localRotation = Quaternion.Euler(_pitch, _yaw + headTremor, _roll + headShake);
            HeadAngles = new Vector3(_pitch, _yaw + headTremor, _roll + headShake);
            GazePoint = look;

            // 吞口水：頭先壓扁再拉長
            float gulp = _activeTell == Tell.Swallow && actT >= 0 ? Mathf.Sin(actT * Mathf.PI * 2f) * 0.05f * Mathf.Min(1.4f, At) : 0;
            Rig.Head.localScale = new Vector3(1 + gulp * 0.5f, 1 - gulp, 1 + gulp * 0.5f);

            AimEyes(look, e);

            // ── 臉 ──
            ApplyFace(dt, e, domExpr, dom, act, actT, At, freeze);

            // ── 手 ──
            ApplyHands(dt, e, act, actT, At, tremor);

            // ── 符號、汗、字幕 ──
            ApplySymbol(dt, domExpr.Symbol, dom, strength);
            float sweat = Mathf.Max(0, e.Sweat) + (_activeTell == Tell.Sweat ? 0.75f * At : 0);
            ApplySweat(dt, sweat);
            if (Subtitle.IsVisible)
            {
                float half = Subtitle.ContentScale * 0.5f;
                Subtitle.Transform.position = PlaceBubble(Subtitle.Size.x * half, Subtitle.Size.y * half);
            }
            Subtitle.Tick(dt, ViewerPoint());
            _talkLeft = Mathf.Max(0, _talkLeft - dt);
        }

        // ───────────── 破綻排程 ─────────────

        static bool IsContinuous(Tell t) =>
            t == Tell.LeanBack || t == Tell.LeanIn || t == Tell.Freeze || t == Tell.Sweat || t == Tell.StareDown;

        static float TellInterval(Tell t)
        {
            switch (t)
            {
                case Tell.FidgetChips: return 2.4f;
                case Tell.GlanceAtCards: return 4.2f;
                case Tell.GlanceAtChips: return 3.4f;
                case Tell.TapFingers: return 2.2f;
                case Tell.Swallow: return 3.0f;
                case Tell.AvoidEyeContact: return 2.8f;
                default: return 0;
            }
        }

        static float TellDuration(Tell t)
        {
            switch (t)
            {
                case Tell.FidgetChips: return 1.6f;
                case Tell.GlanceAtCards: return 1.6f;
                case Tell.GlanceAtChips: return 0.8f;
                case Tell.TapFingers: return 1.1f;
                case Tell.Swallow: return 0.8f;
                case Tell.AvoidEyeContact: return 0.35f; // 偷看玩家一眼
                default: return 0;
            }
        }

        void TickTellAction(float dt, float freq)
        {
            float interval = TellInterval(_activeTell);
            if (interval <= 0) { _tellPhase = -1; return; }
            if (_tellPhase >= 0)
            {
                _tellPhase += dt;
                if (_tellPhase >= _tellDur) { _tellPhase = -1; _tellNext = interval * Rand(0.75f, 1.25f) / freq; }
                return;
            }
            _tellNext -= dt;
            if (_tellNext > 0) return;
            // 被盯著時，避開視線的人更不敢偷看
            if (_activeTell == Tell.AvoidEyeContact && _gazed && _gazedFor < 2.5f) { _tellNext = 0.3f; return; }
            _tellPhase = 0;
            _tellDur = TellDuration(_activeTell) * Rand(0.9f, 1.15f);
            TellActionCount++;
        }

        // ───────────── 視線 ─────────────

        Vector3 ViewerPoint() => _viewer != null ? _viewer.position : transform.TransformPoint(new Vector3(0, 0.45f, 1.2f));

        Vector3 TableCenterPoint() => TableCenter ?? transform.TransformPoint(new Vector3(0, 0, 0.55f));

        /// <summary>某個注視目標的世界座標（寫實角色預覽的固定眼神用）。</summary>
        public Vector3 LookPointFor(LookTarget t, int seat = 0) => LookPoint(t, seat);

        Vector3 LookPoint(LookTarget t, int seat)
        {
            var custom = LookPointResolver?.Invoke(t, seat);
            if (custom.HasValue) return custom.Value;
            switch (t)
            {
                case LookTarget.OwnCards: return Rig.Props.gameObject.activeSelf ? Rig.CardPivotL.position + transform.forward * -0.04f : transform.TransformPoint(0, 0, 0.16f);
                case LookTarget.OwnChips: return Rig.ChipStack.position;
                case LookTarget.Pot:
                case LookTarget.Board: return TableCenterPoint();
                case LookTarget.OtherSeat: return transform.TransformPoint(new Vector3(seat % 2 == 0 ? -0.7f : 0.7f, 0.35f, 0.5f));
                case LookTarget.Away: return transform.TransformPoint(new Vector3(_avoidSide * 0.6f, 0.05f, 0.35f));
                default: return ViewerPoint();
            }
        }

        Vector3 ResolveGaze(in Expression e, float act, float At, float dt, out float headFollow)
        {
            var s = Spec;
            headFollow = 0.62f;
            var tell = _activeTell;
            bool acting = _tellPhase >= 0;
            Vector3 target = LookPoint(_cue.Look, _cue.LookSeat);
            bool lockOn = false;

            if (tell == Tell.StareDown) { target = ViewerPoint(); lockOn = true; }
            else if (tell == Tell.GlanceAtCards && acting && act > 0.3f) target = LookPoint(LookTarget.OwnCards, 0);
            else if (tell == Tell.GlanceAtChips && acting && act > 0.3f) { target = LookPoint(LookTarget.OwnChips, 0); headFollow = 0.3f; }
            else if (tell == Tell.FidgetChips && acting && act > 0.5f && !_gazed) { target = Rig.ChipStack.position; headFollow = 0.35f; }
            else if (tell == Tell.AvoidEyeContact)
            {
                bool peek = acting && act > 0.3f;
                if (_spotted > 0.2f) target = ViewerPoint();
                else if (!peek) target = _gazed ? LookPoint(LookTarget.Away, 0) : TableCenterPoint();
                else target = ViewerPoint();
                headFollow = _gazed ? 0.75f : 0.5f;
            }
            else if (_spotted > 0.15f) target = ViewerPoint(); // 被看到：先瞄玩家一眼
            else if (_gazed && s.OnGazed == GazeResponse.LookAway && tell == Tell.None && _cue.Mood != Mood.Startled)
            {
                // 新手被盯會心虛：看別處、偶爾偷看
                bool peekBack = Mathf.Repeat(_gazedFor, 2.6f) > 1.9f;
                target = peekBack ? ViewerPoint() : LookPoint(LookTarget.Away, 0);
            }
            else if (_gazed && s.OnGazed == GazeResponse.StareBack) { target = ViewerPoint(); lockOn = true; }

            // 待機小動作：偶爾瞄別的地方（只在沒有破綻動作時）
            if (!lockOn && tell != Tell.Freeze && !acting && !_gazed)
            {
                _idleNext -= dt;
                if (_idlePhase < 0 && _idleNext <= 0)
                {
                    _idlePhase = 0;
                    _idleDur = Rand(0.6f, 1.4f);
                    int pick = _rng.Next(3);
                    _idleTarget = pick == 0 ? TableCenterPoint() : pick == 1 ? Rig.ChipStack.position : transform.TransformPoint(new Vector3(_rng.NextDouble() < 0.5 ? -0.7f : 0.7f, 0.35f, 0.45f));
                }
            }
            if (_idlePhase >= 0)
            {
                _idlePhase += dt;
                if (_idlePhase >= _idleDur) { _idlePhase = -1; _idleNext = Rand(3f, 6f) / Spec.IdleBusy; }
                else if (!lockOn && tell != Tell.Freeze && !acting) { target = _idleTarget; headFollow = 0.3f; }
            }

            // 思考往上看、沮喪往下看
            if (!lockOn && Mathf.Abs(e.GazeUp) > 0.05f && target == ViewerPoint())
                target += transform.TransformDirection(new Vector3(0.25f * e.GazeUp, 0.3f * e.GazeUp, 0));

            // 虛張：斜眼瞄旁邊，左右慢慢換邊
            if (!lockOn && e.GazeSide > 0.05f && target == ViewerPoint())
                target += transform.TransformDirection(new Vector3(0.4f * e.GazeSide * Mathf.Sign(Mathf.Sin(_time * 0.8f + _seedOffset)), -0.05f, 0));

            // 眼睛亂飄（掃視）
            float dart = lockOn || tell == Tell.Freeze ? 0 : Mathf.Clamp01(e.EyeDart * (1f + (_gazeAmp - 1f)));
            _saccadeNext -= dt;
            if (_saccadeNext <= 0)
            {
                _saccadeNext = dart > 0.1f ? Rand(0.18f, 0.5f) : Rand(0.8f, 2.2f);
                float amp = dart > 0.1f ? 0.16f * dart : 0.006f;
                _saccade = new Vector2(Rand(-1f, 1f), Rand(-0.6f, 0.6f)) * amp;
            }
            return target + transform.TransformDirection(new Vector3(_saccade.x, _saccade.y, 0));
        }

        void AimEyes(Vector3 target, in Expression e)
        {
            AimEye(Rig.EyeL, target);
            AimEye(Rig.EyeR, target);
            float ps = Mathf.Max(0.45f, 1f + e.PupilScale);
            Rig.PupilL.localScale = Rig.PupilR.localScale = new Vector3(ps, ps, 1);
            float es = 1f + Mathf.Max(-0.2f, e.EyeScale) + (_spotted > 0 ? 0.08f * (_spotted / 0.5f) : 0);
            Rig.EyeRootL.localScale = Rig.EyeRootR.localScale = Vector3.one * es;
        }

        static void AimEye(Transform eye, Vector3 target)
        {
            var parent = eye.parent;
            var local = parent.InverseTransformDirection(target - eye.position);
            if (local.sqrMagnitude < 1e-8f) return;
            local.Normalize();
            const float maxAngle = 34f;
            float ang = Vector3.Angle(Vector3.forward, local);
            if (ang > maxAngle) local = Vector3.Slerp(Vector3.forward, local, maxAngle / ang);
            eye.localRotation = Quaternion.LookRotation(local, Vector3.up);
        }

        // ───────────── 臉 ─────────────

        void ApplyFace(float dt, in Expression e, in Expression domExpr, int dom, float act, float actT, float At, bool freeze)
        {
            var s = Spec;
            // 眨眼
            bool suppressBlink = freeze || (_activeTell == Tell.StareDown && At > 0.3f);
            float blink = 0;
            if (_blinkT >= 0)
            {
                _blinkT += dt;
                blink = Util.Bell(_blinkT / 0.15f, 0.4f, 0.55f);
                if (_blinkT >= 0.15f)
                {
                    _blinkT = -1;
                    if (_doubleBlink) { _doubleBlink = false; _blinkNext = 0.08f; }
                }
            }
            else if (!suppressBlink)
            {
                _blinkNext -= dt * (1f + Mathf.Max(0, e.EyeDart));
                if (_blinkNext <= 0)
                {
                    _blinkT = 0;
                    BlinkCount++;
                    _blinkNext = s.BlinkInterval * Rand(0.5f, 1.5f);
                    _doubleBlink = _rng.NextDouble() < (s.Kind == CharacterKind.Rookie ? 0.35 : 0.12);
                }
            }

            // 眼皮：情緒與個性決定基準；瞪人是「瞇到一定程度」而不是疊加，非眨眼時最多蓋 55%，瞳孔要看得到
            float stare = _activeTell == Tell.StareDown ? 0.3f + 0.12f * Mathf.Min(1f, At) : 0;
            float lookDown = Mathf.Clamp01((_pitch - 12f) / 30f) * 0.2f;
            float lidMood = s.BaseLid * (1f - Mathf.Clamp01(e.EyeScale * 3f)) + e.Lid + lookDown;
            float lidBase = Mathf.Min(0.55f, Mathf.Max(lidMood, stare));
            if (_cue.Mood == Mood.Startled) lidBase *= 0.2f;
            float lid = Mathf.Max(lidBase, blink);
            SetLid(Rig.LidL, Rig.LidRendererL, Rig.LashL, lid);
            SetLid(Rig.LidR, Rig.LidRendererR, Rig.LashR, lid);

            // 眉毛
            float spot = _spotted > 0 ? 0.5f * (_spotted / 0.5f) : 0;
            float inner = e.BrowInner - (_activeTell == Tell.StareDown ? 0.3f + 0.8f * Mathf.Min(1.3f, At) : 0);
            float raise = e.BrowRaise + spot;
            float asym = e.BrowAsym + (s.Kind == CharacterKind.Cold ? 0.15f : 0);
            // 被盯著還回瞪：挑一邊眉毛，像在說「看什麼看」
            if (_activeTell == Tell.StareDown && _gazed) asym += 0.7f * Mathf.Min(1f, At);
            SetBrow(Rig.BrowL, Rig.BrowRestL, Rig.BrowRotL, -1, inner, raise - asym * 0.5f);
            SetBrow(Rig.BrowR, Rig.BrowRestR, Rig.BrowRotR, +1, inner, raise + asym * 0.5f);

            // 嘴
            var shape = _moodW[dom] > 0.4f && dom != (int)Mood.Neutral ? domExpr.Mouth : s.RestMouth;
            if (_gazed && dom == (int)Mood.Neutral && s.OnGazed == GazeResponse.Smile) shape = MouthShape.Smile;
            float open = shape == MouthShape.Grin || shape == MouthShape.O ? Mathf.Clamp01(e.MouthOpen) : 0;
            bool swallowing = _activeTell == Tell.Swallow && actT >= 0;
            if (swallowing) { shape = MouthShape.Flat; open = 0; }
            if (_activeTell == Tell.Freeze) { shape = MouthShape.Flat; open = 0; }
            bool dMouth = shape != MouthShape.O;
            if (_talkLeft > 0)
            {
                float flap = Mathf.Abs(Mathf.Sin(_time * 13f + Mathf.Sin(_time * 5.3f) * 1.5f));
                open = Mathf.Max(open * 0.6f, 0.15f + 0.45f * flap);
            }
            if (open > 0.08f)
            {
                Rig.MouthLineRenderer.enabled = false;
                Rig.MouthOpenRenderer.enabled = true;
                var mesh = CharacterBuilder.OpenMouthMesh(dMouth, out var mats);
                if (Rig.MouthOpenFilter.sharedMesh != mesh) { Rig.MouthOpenFilter.sharedMesh = mesh; Rig.MouthOpenRenderer.sharedMaterials = mats; }
                float w = dMouth ? (shape == MouthShape.Grin ? 0.062f : 0.045f) : 0.03f + 0.012f * open;
                float h = dMouth ? 0.012f + 0.03f * open : 0.018f + 0.03f * open;
                Rig.MouthOpen.localScale = new Vector3(w, h, 1);
            }
            else
            {
                Rig.MouthLineRenderer.enabled = true;
                Rig.MouthOpenRenderer.enabled = false;
                var mesh = CharacterBuilder.MouthMesh(shape);
                if (Rig.MouthLine.sharedMesh != mesh) Rig.MouthLine.sharedMesh = mesh;
            }
            Rig.Mouth.localScale = new Vector3(s.MouthWidth * (swallowing ? 0.7f : 1f), 1, 1);

            // 臉紅與鼓頰
            float blush = Mathf.Clamp01(s.BaseBlush + e.Blush + (_gazed && s.OnGazed != GazeResponse.StareBack ? 0.2f : 0));
            float puff = swallowing ? Util.Bell(actT / 0.6f, 0.3f, 0.4f) * 0.35f : 0;
            var cs = Rig.CheekScale * (0.45f + 0.75f * blush + puff);
            bool showCheek = blush > 0.06f || puff > 0.02f;
            Rig.CheekL.gameObject.SetActive(showCheek);
            Rig.CheekR.gameObject.SetActive(showCheek);
            Rig.CheekL.localScale = Rig.CheekR.localScale = cs;
            float moodLid = Mathf.Min(0.55f, Mathf.Max(e.Lid, stare)) * (_cue.Mood == Mood.Startled ? 0.2f : 1f);
            bool exprOpen = (shape == MouthShape.Grin || shape == MouthShape.O) && !swallowing && _activeTell != Tell.Freeze;
            Face = new FaceState
            {
                BrowInner = inner, BrowRaise = raise, BrowAsym = asym, Lid = lidBase, Blink = blink, MouthOpen = open, Puff = puff, Mouth = shape,
                Mood = (Mood)dom, MoodW = _moodW[dom], MoodLid = Mathf.Max(0, moodLid), EyeWide = e.EyeScale + spot * 0.3f,
                ExprOpen = exprOpen ? Mathf.Clamp01(e.MouthOpen) : 0,
            };
        }

        static void SetLid(Transform pivot, Renderer r, Transform lash, float cover)
        {
            bool on = cover > 0.03f;
            r.enabled = on;
            pivot.localScale = new Vector3(1, Mathf.Max(0.001f, cover), 1);
            // 睫毛線跟著眼皮下緣：眼皮是以 pivot 為頂、高 1.16h 的橢球
            if (lash.gameObject.activeSelf != on) lash.gameObject.SetActive(on);
            if (!on) return;
            var eyeRoot = lash.parent;
            float top = pivot.localPosition.y;
            float lidH = Mathf.Abs(pivot.GetChild(0).localScale.y) * cover;
            var lp = lash.localPosition;
            lash.localPosition = new Vector3(lp.x, top - lidH * 0.5f, lp.z);
            var ls = lash.localScale;
            lash.localScale = new Vector3(ls.x, lidH, ls.z);
        }

        static void SetBrow(Transform brow, Vector3 rest, Quaternion restRot, int side, float inner, float raise)
        {
            brow.localPosition = rest + new Vector3(0, 0.011f * raise, 0);
            // 右眉（+X）內側在 −X 端：內側上揚是負的 Z 旋轉
            brow.localRotation = restRot * Quaternion.Euler(0, 0, -side * 17f * inner);
        }

        // ───────────── 手 ─────────────

        void ApplyHands(float dt, in Expression e, float act, float actT, float At, float tremor)
        {
            var s = Spec;
            float W = s.BodyWidth;
            var restL = new Vector3(-0.115f * W, 0.03f, 0.05f);
            var restR = new Vector3(0.115f * W, 0.03f, 0.05f);
            var tgtL = restL;
            var tgtR = restR;
            var rotL = Quaternion.Euler(0, 12, 0);
            var rotR = Quaternion.Euler(0, -12, 0);

            // 緊張時雙手靠攏搓手
            _handsTogether = Util.Damp(_handsTogether, Mathf.Clamp01(e.HandsTogether), 4f, dt);
            if (_handsTogether > 0.01f)
            {
                float rub = Mathf.Sin(_time * 9f) * 0.012f * _handsTogether;
                tgtL = Vector3.Lerp(tgtL, new Vector3(-0.035f + rub, 0.035f, 0.03f), _handsTogether);
                tgtR = Vector3.Lerp(tgtR, new Vector3(0.035f + rub, 0.035f, 0.03f), _handsTogether);
                rotL = Quaternion.Slerp(rotL, Quaternion.Euler(0, 40, 0), _handsTogether);
                rotR = Quaternion.Slerp(rotR, Quaternion.Euler(0, -40, 0), _handsTogether);
            }

            // 往後靠：老手雙手抱頭，其他人手收回桌緣
            float lb = _activeTell == Tell.LeanBack ? Mathf.Clamp01(At * 1.4f) : 0;
            _hbh = Util.Damp(_hbh, lb, 3f, dt);
            if (_hbh > 0.01f)
            {
                if (s.HandsBehindHead)
                {
                    var head = Rig.Head;
                    var hl = transform.InverseTransformPoint(head.TransformPoint(new Vector3(-Rig.HeadRadii.x * 0.95f, 0.02f, -0.07f)));
                    var hr = transform.InverseTransformPoint(head.TransformPoint(new Vector3(Rig.HeadRadii.x * 0.95f, 0.02f, -0.07f)));
                    tgtL = Vector3.Lerp(tgtL, hl, _hbh);
                    tgtR = Vector3.Lerp(tgtR, hr, _hbh);
                    rotL = Quaternion.Slerp(rotL, Quaternion.Euler(-80, 90, 0), _hbh);
                    rotR = Quaternion.Slerp(rotR, Quaternion.Euler(-80, -90, 0), _hbh);
                }
                else
                {
                    tgtL = Vector3.Lerp(tgtL, new Vector3(-0.14f * W, 0.03f, -0.01f), _hbh);
                    tgtR = Vector3.Lerp(tgtR, new Vector3(0.14f * W, 0.03f, -0.01f), _hbh);
                }
            }

            // 往前傾：手肘往前、手往中間
            _leanInHands = Util.Damp(_leanInHands, _activeTell == Tell.LeanIn ? Mathf.Clamp01(At) : 0, 3f, dt);
            if (_leanInHands > 0.01f)
            {
                tgtL = Vector3.Lerp(tgtL, new Vector3(-0.07f, 0.03f, 0.12f), _leanInHands);
                tgtR = Vector3.Lerp(tgtR, new Vector3(0.07f, 0.03f, 0.12f), _leanInHands);
            }

            // 撥籌碼
            bool fidget = _activeTell == Tell.FidgetChips && actT >= 0;
            var chipTop = Rig.ChipTop;
            if (fidget)
            {
                float w = Util.Bell(actT, 0.2f, 0.2f);
                float spin = actT * Mathf.PI * 2f * (2f + At);
                var over = chipTop + new Vector3(Mathf.Cos(spin) * 0.012f, 0.028f + Mathf.Abs(Mathf.Sin(spin * 2f)) * 0.012f * At, Mathf.Sin(spin) * 0.01f - 0.01f);
                tgtR = Vector3.Lerp(tgtR, over, w);
                rotR = Quaternion.Slerp(rotR, Quaternion.Euler(25, -20, 0), w);
            }

            // 敲桌
            float tap = 0;
            if (_activeTell == Tell.TapFingers && actT >= 0)
            {
                float w = Util.Bell(actT, 0.12f, 0.12f);
                tgtL = Vector3.Lerp(tgtL, new Vector3(-0.09f * W, 0.032f, 0.09f), w);
                tap = Mathf.Max(0, Mathf.Sin(actT * _tellDur * Mathf.PI * 2f * 4.5f)) * w * Mathf.Min(1.4f, 0.6f + At);
                rotL = rotL * Quaternion.Euler(-28f * tap, 0, 0);
                tgtL.y += 0.012f * tap;
            }

            // 偷看底牌：右手去掀牌角
            float lift = 0;
            if (_activeTell == Tell.GlanceAtCards && actT >= 0)
            {
                float reach = Util.Bell(actT, 0.22f, 0.22f);
                float l = Util.Bell((actT - 0.15f) / 0.7f, 0.3f, 0.3f);
                var cardNear = transform.InverseTransformPoint(Rig.CardPivotR.TransformPoint(new Vector3(0.02f, 0.012f, -0.075f)));
                tgtR = Vector3.Lerp(tgtR, cardNear + new Vector3(0, 0.02f * l, 0), reach);
                rotR = Quaternion.Slerp(rotR, Quaternion.Euler(10, -30, 0), reach);
                lift = l * Mathf.Min(1.3f, 0.5f + At);
            }
            _cardLift = Util.Damp(_cardLift, lift, 12f, dt);
            Rig.CardPivotR.localRotation = Quaternion.Euler(20f * _cardLift, 7f, -8f * _cardLift);

            // 被嚇：雙手舉起
            if (_armsUp > 0.01f)
            {
                float w = Util.Smooth01(_armsUp * 1.4f);
                tgtL = Vector3.Lerp(tgtL, new Vector3(-0.2f * W, 0.22f, -0.04f), w);
                tgtR = Vector3.Lerp(tgtR, new Vector3(0.2f * W, 0.22f, -0.04f), w);
                rotL = Quaternion.Slerp(rotL, Quaternion.Euler(-80, -20, 0), w);
                rotR = Quaternion.Slerp(rotR, Quaternion.Euler(-80, 20, 0), w);
            }

            float rate = 9f * s.Snappiness;
            if (!_handsInit) { _handL = tgtL; _handR = tgtR; _handsInit = true; }
            _handL = dt > 0 ? Util.Damp(_handL, tgtL, rate, dt) : tgtL;
            _handR = dt > 0 ? Util.Damp(_handR, tgtR, rate, dt) : tgtR;

            // 手抖：加在平滑之後，否則會被濾掉
            Vector3 jitL = Vector3.zero, jitR = Vector3.zero;
            float tr = tremor * 0.004f * _gazeAmp;
            if (tr > 0)
            {
                jitL = new Vector3(Mathf.PerlinNoise(_time * 16f, 1.3f) - 0.5f, Mathf.PerlinNoise(_time * 16f, 7.1f) - 0.5f, 0) * tr * 2f;
                jitR = new Vector3(Mathf.PerlinNoise(_time * 17f, 3.7f) - 0.5f, Mathf.PerlinNoise(_time * 17f, 9.9f) - 0.5f, 0) * tr * 2f;
            }
            PlaceArm(Rig.ShoulderL, _handL + jitL, rotL, Rig.UpperArmL, Rig.ForeArmL, Rig.HandL, -1);
            PlaceArm(Rig.ShoulderR, _handR + jitR, rotR, Rig.UpperArmR, Rig.ForeArmR, Rig.HandR, +1);

            // 手上的籌碼
            if (fidget && Util.Bell(actT, 0.25f, 0.25f) > 0.5f)
            {
                Rig.FidgetChip.position = Rig.HandR.TransformPoint(new Vector3(-0.012f, -0.012f, 0.035f));
                Rig.FidgetChip.rotation = Rig.HandR.rotation * Quaternion.Euler(_time * 700f, 0, 70f);
            }
            else
            {
                Rig.FidgetChip.localPosition = Util.Damp(Rig.FidgetChip.localPosition, chipTop + new Vector3(0, 0.0017f, 0), 20f, dt);
                Rig.FidgetChip.localRotation = Quaternion.identity;
            }
        }

        /// <summary>兩段式手臂 IK：手肘往外下方。肩膀跟著 Hips 走，手的目標在根節點空間。</summary>
        void PlaceArm(Vector3 shoulderHips, Vector3 handRoot, Quaternion handRot, Transform upper, Transform fore, Transform hand, int side)
        {
            var S = transform.InverseTransformPoint(Rig.Hips.TransformPoint(shoulderHips + new Vector3(0, 0.012f * _shrug, 0)));
            var wrist = handRoot - handRot * new Vector3(0, 0, 0.03f);
            float a = Rig.UpperArmLength, b = Rig.ForeArmLength;
            var d = wrist - S;
            float dist = d.magnitude;
            float stretch = Mathf.Max(1f, dist / (a + b - 0.002f));
            a *= stretch; b *= stretch;
            dist = Mathf.Clamp(dist, Mathf.Abs(a - b) + 0.01f, a + b - 0.001f);
            var dir = d.normalized;
            float cosA = Mathf.Clamp((a * a + dist * dist - b * b) / (2 * a * dist), -1, 1);
            var pole = new Vector3(side * 0.6f, -0.5f, -0.25f);
            var perp = Vector3.ProjectOnPlane(pole, dir).normalized;
            var elbow = S + dir * (a * cosA) + perp * (a * Mathf.Sqrt(1 - cosA * cosA));
            Segment(upper, S, elbow);
            Segment(fore, elbow, wrist);
            hand.localPosition = handRoot;
            hand.localRotation = handRot;
        }

        void Segment(Transform t, Vector3 from, Vector3 to)
        {
            var d = to - from;
            float len = d.magnitude;
            float th = Rig.ArmThickness;
            t.localPosition = (from + to) * 0.5f;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, d / Mathf.Max(1e-5f, len));
            t.localScale = new Vector3(th, (len + th) * 0.5f, th);
        }

        // ───────────── 符號與汗 ─────────────

        void ApplySymbol(float dt, HeadSymbol moodSymbol, int dom, float strength)
        {
            var want = HeadSymbol.None;
            if (_cue.Mood == Mood.Startled && _cueAge < 1.6f) want = HeadSymbol.Exclaim;
            else if (_moodW[dom] > 0.6f && strength >= 0.85f && moodSymbol != HeadSymbol.None && _cue.Intensity >= 0.6f) want = moodSymbol;
            else if (_spotted > 0.05f && Spec.OnGazed == GazeResponse.LookAway) want = HeadSymbol.Exclaim;

            var sym = Rig.Symbol;
            if (want != HeadSymbol.None && want != _symbol)
            {
                _symbol = want;
                _symbolAge = 0;
                Rig.SymbolFilter.sharedMesh = CharacterBuilder.SymbolMesh(want);
                Rig.SymbolRenderer.sharedMaterial = CharacterBuilder.SymbolMaterial(want);
                sym.gameObject.SetActive(true);
            }
            if (want == HeadSymbol.None && _symbol != HeadSymbol.None)
            {
                _symbolOut += dt;
                if (_symbolOut > 0.15f) { _symbol = HeadSymbol.None; sym.gameObject.SetActive(false); _symbolOut = 0; }
            }
            else _symbolOut = 0;
            if (_symbol == HeadSymbol.None) return;

            _symbolAge += dt;
            float pop = _symbolAge < 0.12f ? _symbolAge / 0.12f * 1.35f : 1f + 0.35f * Mathf.Exp(-(_symbolAge - 0.12f) * 9f) * Mathf.Cos((_symbolAge - 0.12f) * 30f);
            float outS = 1f - _symbolOut / 0.15f;
            float scale = (_symbol == HeadSymbol.Exclaim ? 1.15f : 1f) * Mathf.Max(0.01f, pop * outS);
            sym.localScale = Vector3.one * scale;
            sym.localPosition = new Vector3(0, Mathf.Sin(_symbolAge * 5f) * 0.006f, 0);
            var away = sym.position - ViewerPoint();
            var flat = Vector3.ProjectOnPlane(away, Vector3.up);
            if (flat.sqrMagnitude > 1e-6f) sym.rotation = Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(0, 0, _symbol == HeadSymbol.Exclaim ? -12f : 0);
        }

        void ApplySweat(float dt, float level)
        {
            var R = Rig.HeadRadii;
            _dropNext -= dt;
            if (level > 0.15f && _dropNext <= 0)
            {
                for (int i = 0; i < _dropAge.Length; i++)
                {
                    if (_dropAge[i] >= 0) continue;
                    _dropAge[i] = 0;
                    _dropSide[i] = -_dropSide[(i + 2) % 3];
                    Rig.SweatDrops[i].gameObject.SetActive(true);
                    break;
                }
                _dropNext = Mathf.Lerp(1.6f, 0.4f, Mathf.Clamp01(level));
            }
            for (int i = 0; i < _dropAge.Length; i++)
            {
                if (_dropAge[i] < 0) continue;
                _dropAge[i] += dt;
                float t = _dropAge[i] / 1.4f;
                if (t >= 1) { _dropAge[i] = -1; Rig.SweatDrops[i].gameObject.SetActive(false); continue; }
                float x = _dropSide[i] * (R.x * (0.72f + 0.04f * i));
                float y = R.y * (0.4f - 0.55f * Util.Smooth01(t));
                var p = CharacterBuilder.Surface(R, x, y, out var n);
                var d = Rig.SweatDrops[i];
                d.localPosition = p + n * 0.01f;
                d.localRotation = Quaternion.LookRotation(n, Vector3.up);
                float sc = 0.013f * (1f + 0.25f * Mathf.Min(1, level)) * Util.Bell(t, 0.15f, 0.2f);
                d.localScale = Vector3.one * Mathf.Max(0.0005f, sc);
            }
        }

        float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
    }
}
