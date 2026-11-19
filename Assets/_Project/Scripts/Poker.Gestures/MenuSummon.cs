using System;
using System.Globalization;
using System.Numerics;

namespace Poker.Gestures
{
    /// <summary>叫出選單時擋下來的原因（near-miss log 用）。</summary>
    public enum SummonBlock { None, NotTracked, Showdown, Holding, NearCards, NearChips, PalmAngle, FacingHead, Pinching, FingersCurled, TooLow }

    /// <summary>遊戲層每幀給的狀態：拿著東西、攤牌等玩家亮牌時不叫選單；Head＝頭的位置（牌桌座標，null＝不知道）。</summary>
    public struct PalmMenuContext
    {
        /// <summary>任一手拿著底牌或正在咪牌。</summary>
        public bool HoldingCards;
        /// <summary>任一手拿著籌碼。</summary>
        public bool HoldingChips;
        /// <summary>攤牌等玩家翻牌（翻牌就是掌心朝上）。</summary>
        public bool ShowdownFlip;
        public Vector3? Head;

        public static PalmMenuContext From(GestureRecognizer r, bool showdownFlip, Vector3? head)
        {
            var c = new PalmMenuContext { ShowdownFlip = showdownFlip, Head = head };
            if (r == null) return c;
            c.HoldingChips = r.HeldChips(Hand.Left) > 0 || r.HeldChips(Hand.Right) > 0;
            c.HoldingCards = r.HoldingCards(out _, out _, out _) || r.PeekAmount(Hand.Left) > 0f || r.PeekAmount(Hand.Right) > 0f;
            return c;
        }
    }

    /// <summary>一幀的掌心朝上檢查結果。</summary>
    public struct PalmCheck
    {
        public SummonBlock Block;
        /// <summary>掌心法線與正上方的夾角（度）。</summary>
        public float AngleDeg;
        /// <summary>中指與無名指尖離手掌的平均距離（公尺；張開約 9–10 cm、握拳約 3 cm）。</summary>
        public float Extension;
        /// <summary>手掌離桌面的高度（公尺）。</summary>
        public float Height;
        public bool Ok => Block == SummonBlock.None;
    }

    /// <summary>
    /// 選單 near-miss 的 log：同一個 log 物件最多每 MinInterval 秒一行，中間被吃掉的行數附在下一行（遙測上看得出頻率又不洗版）。
    /// 格式：<c>[UX] menu near-miss: &lt;原因&gt; &lt;細節&gt;</c>。
    /// </summary>
    public sealed class MenuSummonLog
    {
        public double MinInterval = 1.5;
        public Action<string> Sink;
        double _last = double.NegativeInfinity;
        int _suppressed;

        public void NearMiss(double time, string reason, string detail)
        {
            if (time - _last < MinInterval) { _suppressed++; return; }
            _last = time;
            string extra = _suppressed > 0 ? $" (+{_suppressed} suppressed)" : "";
            _suppressed = 0;
            Sink?.Invoke("[UX] menu near-miss: " + reason + (string.IsNullOrEmpty(detail) ? "" : " " + detail) + extra);
        }

        /// <summary>不限流的一般訊息（例如「menu opened via palm」）。</summary>
        public void Info(string message) => Sink?.Invoke("[UX] " + message);

        internal static string Cm(float m) => (m * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "cm";
        internal static string Sec(double s) => s.ToString("0.00", CultureInfo.InvariantCulture) + "s";
    }

    /// <summary>
    /// 碰觸按鈕（純邏輯）：指尖在按鈕座標系裡（y＝沿按鈕法線，x、z 在按鈕面上）。
    /// 按法：進到按鈕範圍內停滿 Dwell 秒，或（PressOnCross）一幀內穿過按鈕面而且穿過點在按鈕面內。不需要先從上方懸停上膛。
    /// 按下後指尖要離開範圍外 ExitPad 才能再按，並有冷卻。追蹤掉幀（傳 null）DropoutGrace 秒內不清狀態（雙手交疊時 Quest 常掉幀）。
    /// 另外記「靠近又沒按到」：指尖到過範圍外 NearPad 內，之後離開超過 FarPad、掉幀太久、或按鈕被收起來，都留一筆 LastMiss。
    /// </summary>
    public sealed class TouchButton
    {
        public enum Shape { Sphere, Box }
        public struct Miss
        {
            /// <summary>"missed"（離開）、"dropout"（指尖掉追蹤太久）、"hidden"（按鈕收起來，原因由呼叫端決定）。</summary>
            public string Kind;
            public float Closest;
            public double Dwell;
        }

        public Shape Kind;
        public float Radius = 0.03f;
        public Vector2 HalfSize;
        public float Margin;
        public float MinHeight, MaxHeight;
        public double Dwell = 0.15, Cooldown = 0.6, DropoutGrace = 0.25;
        public bool PressOnCross = true;
        public float ExitPad = 0.015f, HoverPad = 0.03f, NearPad = 0.05f, FarPad = 0.09f;
        /// <summary>停留開始後，指尖在範圍外這個距離內仍算停留（追蹤抖動在邊界上不讓計時重來）。</summary>
        public float StayPad = 0.01f;

        public static TouchButton Sphere(float radius) => new TouchButton { Kind = Shape.Sphere, Radius = radius };

        public static TouchButton Box(Vector2 halfSize, float margin, float minHeight, float maxHeight) => new TouchButton
        {
            Kind = Shape.Box, HalfSize = halfSize, Margin = margin, MinHeight = minHeight, MaxHeight = maxHeight,
        };

        bool _latched, _approach, _approachPressed;
        double _insideSince = double.NaN, _lastSeen = double.NaN, _lastPress = double.NegativeInfinity, _maxDwell;
        float _closest = float.MaxValue;
        Vector3? _prev;

        public bool IsHovered { get; private set; }
        public bool IsInside { get; private set; }
        /// <summary>停留進度 0–1（顯示用）。</summary>
        public float Progress { get; private set; }
        /// <summary>這一幀結束了一次「靠近又沒按到」（其他幀為 null）。</summary>
        public Miss? LastMiss { get; private set; }
        /// <summary>這次靠近時最近的距離（範圍外的距離；0＝進到範圍內）。</summary>
        public float Closest => _closest;
        public bool Approaching => _approach;

        /// <summary>指尖到範圍的距離（範圍內＝0）。</summary>
        public float Distance(Vector3 p)
        {
            if (Kind == Shape.Sphere) return Math.Max(0f, p.Length() - Radius);
            float dx = Math.Max(0f, Math.Abs(p.X) - (HalfSize.X + Margin));
            float dz = Math.Max(0f, Math.Abs(p.Z) - (HalfSize.Y + Margin));
            float dy = Math.Max(0f, Math.Max(MinHeight - p.Y, p.Y - MaxHeight));
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        bool InFootprint(Vector3 p) => Kind == Shape.Sphere
            ? p.X * p.X + p.Z * p.Z <= Radius * Radius
            : Math.Abs(p.X) <= HalfSize.X + Margin && Math.Abs(p.Z) <= HalfSize.Y + Margin;

        bool Crosses(Vector3 a, Vector3 b)
        {
            if (!((a.Y > 0f && b.Y <= 0f) || (a.Y < 0f && b.Y >= 0f))) return false;
            if (Distance(a) > FarPad || Distance(b) > FarPad) return false;   // 追蹤跳點不算
            float t = a.Y / (a.Y - b.Y);
            return InFootprint(a + (b - a) * t);
        }

        /// <summary>每幀呼叫；local＝指尖在按鈕座標系（沒追蹤到傳 null）。回傳這一幀是否按下。</summary>
        public bool Step(Vector3? local, double time)
        {
            LastMiss = null;
            if (!local.HasValue)
            {
                if (double.IsNaN(_lastSeen)) { IsHovered = IsInside = false; Progress = 0f; return false; }
                if (time - _lastSeen > DropoutGrace)
                {
                    EndApproach("dropout");
                    ClearTouch();
                    _latched = false;
                    _lastSeen = double.NaN;
                }
                return false;   // 寬限內：維持狀態（高亮、停留計時繼續走）
            }

            var p = local.Value;
            float d = Distance(p);
            _lastSeen = time;
            bool pressed = false;

            if (d <= NearPad) { _approach = true; _closest = Math.Min(_closest, d); }
            if (_latched)
            {
                if (d > ExitPad) _latched = false;
                _insideSince = double.NaN;
            }
            else
            {
                bool inside = d <= 0f || (!double.IsNaN(_insideSince) && d <= StayPad);
                bool crossed = PressOnCross && _prev.HasValue && Crosses(_prev.Value, p);
                if (inside) { if (double.IsNaN(_insideSince)) _insideSince = time; }
                else _insideSince = double.NaN;
                double dwell = inside ? time - _insideSince : 0;
                if (_approach) _maxDwell = Math.Max(_maxDwell, dwell);
                if ((inside && dwell >= Dwell) || crossed)
                {
                    _latched = true;
                    _insideSince = double.NaN;
                    if (time - _lastPress >= Cooldown)   // 冷卻中：吃掉這次（要離開再進來）
                    {
                        _lastPress = time;
                        _approachPressed = true;
                        pressed = true;
                    }
                }
            }

            if (_approach && d > FarPad) EndApproach("missed");
            IsInside = d <= 0f;
            IsHovered = d <= HoverPad;
            Progress = double.IsNaN(_insideSince) ? 0f : (float)Math.Min(1.0, (time - _insideSince) / Dwell);
            _prev = p;
            return pressed;
        }

        /// <summary>按鈕收起來（或換手）：清狀態；正在靠近又還沒按到就留一筆 "hidden"。冷卻時間保留。</summary>
        public void Cancel()
        {
            LastMiss = null;
            EndApproach("hidden");
            ClearTouch();
            _latched = false;
            _lastSeen = double.NaN;
        }

        void ClearTouch()
        {
            _insideSince = double.NaN;
            _prev = null;
            IsHovered = IsInside = false;
            Progress = 0f;
        }

        void EndApproach(string kind)
        {
            if (_approach && !_approachPressed) LastMiss = new Miss { Kind = kind, Closest = _closest, Dwell = _maxDwell };
            _approach = _approachPressed = false;
            _closest = float.MaxValue;
            _maxDwell = 0;
        }
    }

    /// <summary>
    /// 掌心朝上叫出選單（2026-10-14；實機回報「用手叫選單很不靈敏」）：非慣用手掌心朝上（離正上方 ≤ 35°）、四指大致張開、沒有捏合，
    /// 高於桌面 6 cm、不在自己的牌或籌碼堆旁，停 0.4 s → 手掌上方 9 cm 浮出選單按鈕（跟著手掌平滑移動）。
    /// 另一手食指進到按鈕（半徑 3 cm）停 0.15 s、或穿過按鈕面（面向頭）就按下，不需要先從上方上膛。掌心轉開 &gt; 0.3 s 收起。
    /// 不和系統手勢衝突：Meta 的系統選單是「掌心朝向頭＋捏合」，這裡要掌心朝天花板（掌心正對頭 25° 內就不算）、而且捏合就不出現。
    /// 不和遊戲手勢衝突：咪牌掌心朝下；翻腕咪牌、攤牌翻牌會在牌上方掌心朝上 → 牌旁 15 cm（且低於 15 cm 高）不出現、拿著牌或籌碼不出現、攤牌等翻牌時不出現。
    /// </summary>
    public sealed class PalmMenu
    {
        public const float UpAngleEnter = 35f, UpAngleExit = 50f, NearMissAngle = 65f, FacingHeadAngle = 25f;
        public const float MinExtension = 0.06f, MinExtensionExit = 0.05f;
        public const float MinHeight = 0.06f, MinHeightExit = 0.045f;
        public const float NearCardsRadius = 0.15f, NearChipsRadius = 0.12f, GuardMaxHeight = 0.15f;
        public const float PinchStrength = 0.35f, PinchDistance = 0.025f;
        public const float ButtonLift = 0.09f, ButtonRadius = 0.03f;
        public const double HoldSeconds = 0.4, HideDelay = 0.3, PalmLostGrace = 0.5, PressDwell = 0.15, Cooldown = 0.6, FollowTau = 0.05;
        /// <summary>沒叫出來的姿勢持續這麼久就先記一筆 near-miss（不等手放下）。</summary>
        public const double NearMissAfter = 0.8;

        TableLayout _layout;
        /// <summary>牌與籌碼堆的位置（遊戲層用辨識器的 layout；null 時用預設）。</summary>
        public TableLayout Layout { get => _layout; set => _layout = value ?? new TableLayout(); }
        public MenuSummonLog Log;
        public TouchButton Button { get; } = TouchButton.Sphere(ButtonRadius);

        public bool Visible { get; private set; }
        public Vector3 ButtonCenter { get; private set; }
        /// <summary>按鈕面法線（指向頭；不知道頭在哪時朝上）。</summary>
        public Vector3 ButtonNormal { get; private set; } = Vector3.UnitY;
        public bool Hovered => Visible && Button.IsHovered;
        public float Progress => Visible ? Button.Progress : 0f;
        /// <summary>叫出來之前的停留進度 0–1（顯示淡淡的預告用）。</summary>
        public float SummonProgress { get; private set; }
        public PalmCheck Last { get; private set; }

        double _lastTime = double.NaN, _okSince = double.NaN, _awaySince = double.NaN, _palmSeen = double.NaN;
        Hand? _hand;
        // near-miss：一段「大致朝上但沒叫出來」的姿勢
        bool _episode, _episodeLogged;
        double _episodeStart, _okStreakMax;
        readonly int[] _blockFrames = new int[Enum.GetValues(typeof(SummonBlock)).Length];
        float _minAngle, _lastExt, _lastHeight;

        public PalmMenu(TableLayout layout = null, MenuSummonLog log = null)
        {
            _layout = layout ?? new TableLayout();
            Log = log;
            Button.Dwell = PressDwell;
            Button.Cooldown = Cooldown;
        }

        /// <summary>掌心法線與正上方的夾角（度）。</summary>
        public static float UpAngle(HandPose h) => AngleDeg(h.PalmNormal, Vector3.UnitY);

        static float AngleDeg(Vector3 a, Vector3 b)
        {
            float la = a.Length(), lb = b.Length();
            if (la < 1e-6f || lb < 1e-6f) return 180f;
            float c = Vector3.Dot(a, b) / (la * lb);
            return (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, c))) * 180.0 / Math.PI);
        }

        /// <summary>單幀檢查（visible＝已出現，用較寬的離開門檻）。</summary>
        public static PalmCheck Check(HandPose palm, in PalmMenuContext ctx, TableLayout layout, bool visible)
        {
            var r = new PalmCheck { AngleDeg = 180f };
            if (palm == null || !palm.Tracked) { r.Block = SummonBlock.NotTracked; return r; }
            layout = layout ?? new TableLayout();
            var p = palm.Position(HandJoint.Palm);
            r.AngleDeg = UpAngle(palm);
            r.Height = p.Y - layout.TableY;
            r.Extension = 0.5f * (Vector3.Distance(palm.Position(HandJoint.MiddleTip), p) + Vector3.Distance(palm.Position(HandJoint.RingTip), p));

            if (ctx.ShowdownFlip) r.Block = SummonBlock.Showdown;
            else if (ctx.HoldingCards || ctx.HoldingChips) r.Block = SummonBlock.Holding;
            else if (r.Height < GuardMaxHeight && Flat(p - layout.CardsCenter) < NearCardsRadius) r.Block = SummonBlock.NearCards;
            else if (r.Height < GuardMaxHeight && Flat(p - layout.ChipStackCenter) < NearChipsRadius) r.Block = SummonBlock.NearChips;
            else if (r.AngleDeg > (visible ? UpAngleExit : UpAngleEnter)) r.Block = SummonBlock.PalmAngle;
            else if (ctx.Head.HasValue && AngleDeg(palm.PalmNormal, ctx.Head.Value - p) < FacingHeadAngle) r.Block = SummonBlock.FacingHead;
            else if (palm.IndexPinching || palm.IndexPinch >= PinchStrength
                     || Vector3.Distance(palm.Position(HandJoint.ThumbTip), palm.Position(HandJoint.IndexTip)) < PinchDistance) r.Block = SummonBlock.Pinching;
            else if (r.Extension < (visible ? MinExtensionExit : MinExtension)) r.Block = SummonBlock.FingersCurled;
            else if (r.Height < (visible ? MinHeightExit : MinHeight)) r.Block = SummonBlock.TooLow;
            return r;
        }

        static float Flat(Vector3 v) => (float)Math.Sqrt(v.X * v.X + v.Z * v.Z);

        public static string ReasonOf(SummonBlock b)
        {
            switch (b)
            {
                case SummonBlock.NotTracked: return "tracking-lost palm";
                case SummonBlock.Showdown: return "blocked-showdown";
                case SummonBlock.Holding: return "blocked-holding";
                case SummonBlock.NearCards: return "blocked-by-cards";
                case SummonBlock.NearChips: return "blocked-by-chips";
                case SummonBlock.PalmAngle: return "palm-angle";
                case SummonBlock.FacingHead: return "facing-head";
                case SummonBlock.Pinching: return "pinching";
                case SummonBlock.FingersCurled: return "fingers-curled";
                case SummonBlock.TooLow: return "too-low";
                default: return "none";
            }
        }

        public void Reset()
        {
            if (Visible) Hide(null);
            _okSince = _awaySince = _palmSeen = _lastTime = double.NaN;
            SummonProgress = 0f;
            _episode = false;
        }

        /// <summary>每幀呼叫；palmHand＝叫選單的手（非慣用手），另一手按。回傳這一幀是否按下按鈕。</summary>
        public bool Step(HandsFrame frame, Hand palmHand, in PalmMenuContext ctx)
        {
            if (_hand != palmHand) { Reset(); _hand = palmHand; }
            double time = frame.Time;
            double dt = double.IsNaN(_lastTime) ? 0 : Math.Max(0, Math.Min(0.1, time - _lastTime));
            _lastTime = time;
            var palm = frame.Get(palmHand);
            var finger = frame.Get(palmHand == Hand.Left ? Hand.Right : Hand.Left);
            var check = Check(palm, ctx, _layout, Visible);
            Last = check;
            if (palm.Tracked) _palmSeen = time;

            if (!Visible)
            {
                if (check.Ok) { if (double.IsNaN(_okSince)) _okSince = time; }
                else if (!(check.Block == SummonBlock.NotTracked && time - _palmSeen <= 0.25)) _okSince = double.NaN;   // 掉幾幀不重來
                double held = double.IsNaN(_okSince) ? 0 : time - _okSince;
                SummonProgress = (float)Math.Min(1.0, held / HoldSeconds);
                TrackEpisode(check, held, time, dt);
                if (held >= HoldSeconds && check.Ok)
                {
                    Visible = true;
                    _okSince = _awaySince = double.NaN;
                    SummonProgress = 0f;
                    _episode = false;
                    ButtonCenter = Target(palm);
                    Log?.Info("palm menu button shown");
                }
                else return false;
            }
            else
            {
                if (palm.Tracked)
                {
                    if (check.Ok) _awaySince = double.NaN;
                    else if (double.IsNaN(_awaySince)) _awaySince = time;
                    if (!double.IsNaN(_awaySince) && time - _awaySince > HideDelay) { Hide("palm-away"); return false; }
                    float a = dt <= 0 ? 1f : (float)(1 - Math.Exp(-dt / FollowTau));
                    ButtonCenter += (Target(palm) - ButtonCenter) * a;
                }
                else if (double.IsNaN(_palmSeen) || time - _palmSeen > PalmLostGrace) { Hide("tracking-lost palm"); return false; }
            }

            var n = ctx.Head.HasValue && Vector3.DistanceSquared(ctx.Head.Value, ButtonCenter) > 1e-6f
                ? Vector3.Normalize(ctx.Head.Value - ButtonCenter) : Vector3.UnitY;
            ButtonNormal = n;
            Vector3? local = null;
            if (finger.Tracked)
            {
                var d = finger.Position(HandJoint.IndexTip) - ButtonCenter;
                Basis(n, out var u, out var v);
                local = new Vector3(Vector3.Dot(d, u), Vector3.Dot(d, n), Vector3.Dot(d, v));
            }
            bool pressed = Button.Step(local, time);
            if (Button.LastMiss.HasValue)
            {
                var m = Button.LastMiss.Value;
                Log?.NearMiss(time, m.Kind == "dropout" ? "tracking-lost finger" : "finger-missed", MissDetail(m, "palm"));
            }
            return pressed;
        }

        internal static string MissDetail(TouchButton.Miss m, string via) =>
            $"via={via} closest={MenuSummonLog.Cm(m.Closest)} dwell={MenuSummonLog.Sec(m.Dwell)}";

        static Vector3 Target(HandPose palm) => palm.Position(HandJoint.Palm) + new Vector3(0f, ButtonLift, 0f);

        static void Basis(Vector3 n, out Vector3 u, out Vector3 v)
        {
            var refAxis = Math.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            u = Vector3.Normalize(Vector3.Cross(refAxis, n));
            v = Vector3.Cross(n, u);
        }

        void Hide(string why)
        {
            Button.Cancel();
            if (why != null && Button.LastMiss.HasValue)
                Log?.NearMiss(_lastTime, why == "palm-away" ? "palm-away" : why, MissDetail(Button.LastMiss.Value, "palm"));
            Visible = false;
            _awaySince = double.NaN;
        }

        void TrackEpisode(PalmCheck c, double held, double time, double dt)
        {
            bool roughlyUp = c.Block != SummonBlock.NotTracked && c.AngleDeg <= NearMissAngle;
            bool lostBriefly = c.Block == SummonBlock.NotTracked && time - _palmSeen <= 0.25;
            if (roughlyUp)
            {
                if (!_episode)
                {
                    _episode = true; _episodeLogged = false; _episodeStart = time; _okStreakMax = 0;
                    Array.Clear(_blockFrames, 0, _blockFrames.Length);
                    _minAngle = 180f;
                }
                _okStreakMax = Math.Max(_okStreakMax, held);
                if (!c.Ok) _blockFrames[(int)c.Block]++;
                _minAngle = Math.Min(_minAngle, c.AngleDeg);
                _lastExt = c.Extension; _lastHeight = c.Height;
                if (!_episodeLogged && time - _episodeStart >= NearMissAfter) { EmitEpisode(time, false); _episodeLogged = true; }
                return;
            }
            if (lostBriefly || !_episode) return;
            if (!_episodeLogged) EmitEpisode(time, c.Block == SummonBlock.NotTracked);
            _episode = false;
        }

        void EmitEpisode(double time, bool lost)
        {
            double dur = time - _episodeStart;
            string reason = null;
            if (lost && _okStreakMax >= 0.15) reason = "tracking-lost palm";
            else if (dur >= HoldSeconds)
            {
                int best = 0;
                for (int i = 1; i < _blockFrames.Length; i++) if (_blockFrames[i] > _blockFrames[best]) best = i;
                reason = _blockFrames[best] > 0 ? ReasonOf((SummonBlock)best) : "hold-short";
            }
            else if (_okStreakMax >= 0.15) reason = "hold-short";
            if (reason == null) return;
            Log?.NearMiss(time, reason, string.Format(CultureInfo.InvariantCulture,
                "angle={0:0}deg hold={1} ext={2} height={3:0}cm", _minAngle, MenuSummonLog.Sec(_okStreakMax), MenuSummonLog.Cm(_lastExt), _lastHeight * 100f));
        }
    }
}
