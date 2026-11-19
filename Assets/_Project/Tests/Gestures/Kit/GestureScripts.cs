using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>測試共用：預設牌桌、執行辨識、組合手勢動作。</summary>
    public static class G
    {
        public static readonly TableLayout Layout = new TableLayout();
        public static Vector3 Cards => Layout.CardsCenter;
        public static Vector3 Stack => Layout.ChipStackCenter;

        public static TableContext Ctx(bool turn = true, bool canCheck = true, TablePhase phase = TablePhase.Flop, int chips = 1000, int denom = 50) =>
            new TableContext { IsPlayerTurn = turn, CanCheck = canCheck, Phase = phase, PlayerChips = chips, ChipDenomination = denom };

        /// <summary>統計工具用：每次 Run 結束後把全部事件交給它（預設 null；只在單執行緒的報表工具裡設定）。</summary>
        [System.ThreadStatic] public static System.Action<List<GestureEvent>> RunObserver;

        /// <summary>調參工具用：Run 沒指定 tuning 時改用這組（預設 null＝GestureTuning 預設值；只在單執行緒的報表工具裡設定）。</summary>
        [System.ThreadStatic] public static GestureTuning DefaultTuning;

        public static List<GestureEvent> Run(SyntheticHands s, TableContext? ctx = null, GestureTuning tuning = null)
        {
            var r = new GestureRecognizer(Layout, tuning ?? DefaultTuning);
            var c = ctx ?? Ctx();
            var all = new List<GestureEvent>();
            foreach (var f in s.Frames) all.AddRange(r.Update(f, c));
            RunObserver?.Invoke(all);
            return all;
        }

        /// <summary>依時間切換牌桌狀態（time ≥ 切換點後用新狀態）。</summary>
        public static List<GestureEvent> Run(SyntheticHands s, System.Func<double, TableContext> ctxAt)
        {
            var r = new GestureRecognizer(Layout, DefaultTuning);
            var all = new List<GestureEvent>();
            foreach (var f in s.Frames) all.AddRange(r.Update(f, ctxAt(f.Time)));
            return all;
        }

        public static int Count(this IEnumerable<GestureEvent> ev, GestureKind k) => ev.Count(e => e.Kind == k);
        public static GestureEvent First(this IEnumerable<GestureEvent> ev, GestureKind k) => ev.First(e => e.Kind == k);
        public static GestureEvent Last(this IEnumerable<GestureEvent> ev, GestureKind k) => ev.Last(e => e.Kind == k);
        public static bool AnyAction(this IEnumerable<GestureEvent> ev) => ev.Any(e => e.IsAction);
        public static string Dump(this IEnumerable<GestureEvent> ev) => string.Join("\n", ev.Where(e => e.Kind != GestureKind.PeekChanged));
    }

    /// <summary>
    /// 各手勢的標準動作腳本。參數有預設值（單元測試用），統計測試會在合理範圍內隨機化。
    /// 都假設從 SyntheticHands 的預設姿勢（雙手平放桌上、不在牌或籌碼上）開始。
    /// </summary>
    public static class GestureScripts
    {
        const float Rest = 0.025f; // 手掌平放桌面時手掌中心的高度

        /// <summary>咪牌：手移到牌上、掌心朝下，抬起手腕（俯仰）並稍微抬手，再放下、離開。</summary>
        /// <param name="roll">抬起時手同時側轉（度，正值＝掌心轉向身體中線）：整隻手抬起時掌心常轉到接近垂直（peek_v2_153106：90° 上下）。</param>
        public static SyntheticHands Peek(SyntheticHands s, Hand h = Hand.Right, float dx = 0, float dz = 0, float pitch = 35f, float lift = 0.02f, double hold = 0.6, float roll = 0f)
        {
            var over = G.Cards + new Vector3(dx, 0.03f, dz);
            s.Move(h, 0.5, HandTarget.At(over + new Vector3(0, 0.05f, 0)));
            s.Move(h, 0.25, HandTarget.At(over));
            s.Wait(0.3);
            float side = h == Hand.Right ? 1f : -1f;
            s.Move(h, 0.4, HandTarget.At(over + new Vector3(0, lift, 0)).WithPitch(pitch).WithRoll(side * roll));
            s.Wait(hold);
            s.Move(h, 0.3, HandTarget.At(over).WithPitch(0).WithRoll(0));
            return s;
        }

        /// <summary>翻腕咪牌的手掌位置：牌的近邊（2026-10-13 實機：手掌離牌中心 6.6–9 cm）。</summary>
        public static readonly Vector3 RollPeekSpot = G.Cards + new Vector3(0.01f, 0.03f, -0.03f);

        /// <summary>
        /// 翻腕咪牌（2026-10-13 實機 log 3646.9–3648.5）：手在牌上掌心朝下就位，再翻腕到 roll 度（0＝掌心朝下、180＝朝上；135 約掌心 45°）
        /// 並抬起 lift（實機手掌 4.6–7.4 cm），停 hold 秒，翻回來後回到休息位置。
        /// </summary>
        public static SyntheticHands RollPeek(SyntheticHands s, Hand h = Hand.Right, float roll = 135f, double duration = 0.5, double hold = 0.6, float lift = 0.025f)
        {
            float side = h == Hand.Right ? 1f : -1f;
            s.Move(h, 0.5, HandTarget.At(RollPeekSpot + new Vector3(0, 0.05f, 0)));
            s.Move(h, 0.25, HandTarget.At(RollPeekSpot));
            s.Wait(0.3);
            s.Move(h, duration, HandTarget.At(RollPeekSpot + new Vector3(0, lift, 0)).WithRoll(side * roll));
            s.Wait(hold);
            s.Move(h, 0.4, HandTarget.At(RollPeekSpot).WithRoll(0));
            s.Move(h, 0.5, HandTarget.At(h == Hand.Right ? SyntheticHands.RightRest : SyntheticHands.LeftRest));
            s.Wait(0.3);
            return s;
        }

        /// <summary>咪牌時手腕高出指尖 wristOverTip（公尺）所需的俯仰角（合成手：手平放時差 1.6 cm，手腕在手掌後 7.5 cm、指尖在前 10 cm）。</summary>
        public static float PitchForWristOverTip(float wristOverTip)
        {
            // w−t ≈ 0.016·cos p + 0.175·sin p，在 0–40° 用二分法
            float lo = 0f, hi = 40f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f, r = mid * (float)System.Math.PI / 180f;
                if (0.016f * (float)System.Math.Cos(r) + 0.175f * (float)System.Math.Sin(r) < wristOverTip) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        /// <summary>
        /// 輕捏籌碼（Quest 實機：捏合強度常只有 0.45–0.55、指尖相距約 2 cm 就不再收緊）：移到 pinchPoint、捏到 pinch（合成手 0–1，
        /// 強度＝pinch×0.70、指尖距離≈(1−pinch)×7.5 cm），再往上抬 lift。
        /// </summary>
        public static SyntheticHands WeakPinchAt(SyntheticHands s, Hand h, Vector3 pinchPoint, float pinch = 0.73f, float lift = 0.03f)
        {
            var palm = SyntheticHands.PalmForPinch(h, pinchPoint);
            s.Move(h, 0.4, HandTarget.At(palm + new Vector3(0, 0.06f, 0)));
            s.Move(h, 0.2, HandTarget.At(palm));
            s.Move(h, 0.15, HandTarget.Pinch(pinch));
            s.Wait(0.1);
            s.Move(h, 0.4, HandTarget.At(palm + new Vector3(0, lift, 0)));
            s.Wait(0.2);
            return s;
        }

        /// <summary>
        /// 輕敲兩下（2026-10-12 實機 log pid 22510，2005–2023 s）：第一下從 firstHeight 落下（firstDown 秒），
        /// 手掌只回升 rise（1.3–2 cm）就敲第二下，第二下落點比第一下低 sink（手壓在桌上、追蹤高度往下 0.5–1 cm）。
        /// 峰值 0.4–0.6 m/s、落差 2.3–5 cm、間隔 0.24–0.31 s。
        /// </summary>
        public static SyntheticHands GentleKnock(SyntheticHands s, Hand h, Vector3 at, float firstHeight = 0.04f, double firstDown = 0.10,
            float rise = 0.016f, float sink = 0.006f, double secondDown = 0.07, double pause = 0.05, bool second = true)
        {
            var low = new Vector3(at.X, at.Y + Rest, at.Z);
            s.Move(h, 0.4, HandTarget.At(low + new Vector3(0, 0.05f, 0)));
            s.Move(h, 0.25, HandTarget.At(low));
            s.Wait(0.3);
            s.Move(h, 0.12, HandTarget.At(low + new Vector3(0, firstHeight, 0)), Ease.Out);
            s.Move(h, firstDown, HandTarget.At(low), Ease.In);
            s.Wait(pause);
            if (second)
            {
                s.Move(h, 0.08, HandTarget.At(low + new Vector3(0, rise, 0)), Ease.Out);
                s.Move(h, secondDown, HandTarget.At(low - new Vector3(0, sink, 0)), Ease.In);
            }
            s.Wait(0.6);
            return s;
        }

        /// <summary>
        /// 第一下清楚、第二下慢（2026-10-13 實機 log 3586–3599）：第一下抬 firstHeight、firstDown 秒等加速落下；停 pause 秒；
        /// 第二下花 up 秒抬 secondHeight、secondDown 秒落下（末速 ≈ 2·secondHeight／secondDown）。
        /// </summary>
        public static SyntheticHands SoftSecondKnock(SyntheticHands s, Hand h, Vector3 at, float firstHeight = 0.045f, double firstDown = 0.09,
            float secondHeight = 0.04f, double secondDown = 0.27, double up = 0.1, double pause = 0.03)
        {
            var low = new Vector3(at.X, at.Y + Rest, at.Z);
            s.Move(h, 0.4, HandTarget.At(low + new Vector3(0, 0.05f, 0)));
            s.Move(h, 0.25, HandTarget.At(low));
            s.Wait(0.3);
            s.Move(h, 0.1, HandTarget.At(low + new Vector3(0, firstHeight, 0)), Ease.Out);
            s.Move(h, firstDown, HandTarget.At(low), Ease.In);
            s.Wait(pause);
            s.Move(h, up, HandTarget.At(low + new Vector3(0, secondHeight, 0)), Ease.Out);
            s.Move(h, secondDown, HandTarget.At(low), Ease.In);
            s.Wait(0.6);
            return s;
        }

        /// <summary>挑籌碼：移到籌碼堆、捏合、往上抬 lift（每 2.5 cm 一階）。</summary>
        public static SyntheticHands PickChips(SyntheticHands s, Hand h = Hand.Right, float lift = 0.08f, Vector3? jitter = null)
        {
            var at = G.Stack + new Vector3(0, 0.02f, 0) + (jitter ?? Vector3.Zero);
            var palm = SyntheticHands.PalmForPinch(h, at);
            s.Move(h, 0.4, HandTarget.At(palm + new Vector3(0, 0.06f, 0)));
            s.Move(h, 0.2, HandTarget.At(palm));
            s.Move(h, 0.15, HandTarget.Pinch(1f));
            s.Wait(0.1);
            s.Move(h, 0.5, HandTarget.At(palm + new Vector3(0, lift, 0)));
            s.Wait(0.2);
            return s;
        }

        /// <summary>把手上的籌碼放到某點（捏合點），停住後放開。</summary>
        public static SyntheticHands PlaceAt(SyntheticHands s, Hand h, Vector3 pinchPoint, double travel = 0.6)
        {
            var palm = SyntheticHands.PalmForPinch(h, pinchPoint);
            s.Move(h, travel * 0.6, HandTarget.At(palm + new Vector3(0, 0.08f, 0)));
            s.Move(h, travel * 0.4, HandTarget.At(palm));
            s.Wait(0.15);
            s.Move(h, 0.1, HandTarget.Pinch(0f));
            s.Move(h, 0.3, HandTarget.At(palm + new Vector3(0, 0.06f, 0)));
            return s;
        }

        /// <summary>丟籌碼：從 from（捏合點）往前加速，最後 releaseFrac 那段放開。</summary>
        public static SyntheticHands Throw(SyntheticHands s, Hand h, Vector3 from, Vector3 velocity, double duration = 0.15)
        {
            var palm = SyntheticHands.PalmForPinch(h, from);
            s.Move(h, 0.4, HandTarget.At(palm));
            s.Wait(0.1);
            // 加速段（等加速）然後等速段中放開
            var mid = palm + velocity * (float)(duration * 0.5);
            s.Move(h, duration, HandTarget.At(mid), Ease.In);
            var after = mid + velocity * 0.05f;
            s.Move(h, 0.05, HandTarget.At(after).WithPinch(0f), Ease.Linear);
            s.Move(h, 0.1, HandTarget.At(after + velocity * 0.05f), Ease.Out);
            s.Wait(0.2);
            return s;
        }

        /// <summary>撈回：到下注區某點捏起，抬起。</summary>
        public static SyntheticHands Retract(SyntheticHands s, Hand h, Vector3 pinchPoint)
        {
            var palm = SyntheticHands.PalmForPinch(h, pinchPoint);
            s.Move(h, 0.3, HandTarget.At(palm + new Vector3(0, 0.05f, 0)));
            s.Move(h, 0.15, HandTarget.At(palm));
            s.Move(h, 0.12, HandTarget.Pinch(1f));
            s.Move(h, 0.3, HandTarget.At(palm + new Vector3(0, 0.08f, 0)));
            return s;
        }

        /// <summary>敲桌：在 at 敲 taps 下，每下抬起 height、落下 down 秒（等加速），兩下之間停 gap 秒。</summary>
        public static SyntheticHands Knock(SyntheticHands s, Hand h, Vector3 at, int taps = 2, float height = 0.04f, double up = 0.12, double down = 0.09, double gap = 0.05)
        {
            var low = new Vector3(at.X, at.Y + Rest, at.Z);
            s.Move(h, 0.4, HandTarget.At(low + new Vector3(0, 0.05f, 0)));
            s.Move(h, 0.25, HandTarget.At(low));
            s.Wait(0.3);
            for (int i = 0; i < taps; i++)
            {
                s.Move(h, up, HandTarget.At(low + new Vector3(0, height, 0)), Ease.Out);
                s.Move(h, down, HandTarget.At(low), Ease.In);
                s.Wait(gap);
            }
            s.Wait(0.5);
            return s;
        }

        /// <summary>手指敲桌：手掌平放在 at 不動，食指與中指抬起 lift（0–1，1 = 8 cm）再等加速敲下（down 秒），taps 下。</summary>
        public static SyntheticHands FingerTap(SyntheticHands s, Hand h, Vector3 at, int taps = 2, float lift = 0.75f, double up = 0.12, double down = 0.07, double gap = 0.06)
        {
            var low = new Vector3(at.X, at.Y + Rest, at.Z);
            s.Move(h, 0.4, HandTarget.At(low + new Vector3(0, 0.05f, 0)));
            s.Move(h, 0.25, HandTarget.At(low));
            s.Wait(0.3);
            for (int i = 0; i < taps; i++)
            {
                s.Move(h, up, HandTarget.FingerLift(lift), Ease.Out);
                s.Move(h, down, HandTarget.FingerLift(0f), Ease.In);
                s.Wait(gap);
            }
            s.Wait(0.5);
            return s;
        }

        public static Vector3 KnockSpot(Hand h) => h == Hand.Right ? new Vector3(0.30f, 0, 0.26f) : new Vector3(-0.22f, 0, 0.22f);

        /// <summary>往牌上拍下去（舊的摔牌動作；摔牌手勢已移除，現在當反例）：手在牌上方 height 處，等加速落到桌面（花 duration 秒）。末速 ≈ 2·drop/duration。</summary>
        public static SyntheticHands Slam(SyntheticHands s, Hand h = Hand.Right, float height = 0.25f, double duration = 0.15, float dx = 0.02f, float dz = 0f, bool pinchCards = false)
        {
            var top = G.Cards + new Vector3(dx, height, dz);
            s.Move(h, 0.5, HandTarget.At(top));
            if (pinchCards) s.Move(h, 0.1, HandTarget.Pinch(1f));
            s.Wait(0.2);
            s.Move(h, duration, HandTarget.At(G.Cards + new Vector3(dx, Rest + 0.005f, dz)), Ease.In);
            s.Wait(0.4);
            return s;
        }

        /// <summary>推牌棄牌：手掌壓在牌上，貼著桌面往前推 distance。</summary>
        public static SyntheticHands FoldPush(SyntheticHands s, Hand h = Hand.Right, float distance = 0.18f, double duration = 0.5, float height = Rest, float dx = 0)
        {
            var start = G.Cards + new Vector3(dx, height, 0);
            s.Move(h, 0.5, HandTarget.At(start + new Vector3(0, 0.06f, 0)));
            s.Move(h, 0.3, HandTarget.At(start));
            s.Wait(0.3);
            s.Move(h, duration, HandTarget.At(start + new Vector3(0, 0, distance)));
            s.Wait(0.3);
            return s;
        }

        /// <summary>
        /// 慢慢滑牌棄牌（2026-10-12 第二次實機 log 348.9 s）：手掌在牌後方（指尖壓在牌上），貼桌往前滑 distance、花 duration 秒。
        /// 手掌停在棄牌線前（z≈0.19），是指尖（牌）越過棄牌線。
        /// </summary>
        public static SyntheticHands FoldSlide(SyntheticHands s, Hand h = Hand.Right, float distance = 0.14f, double duration = 1.6, float palmZ = 0.05f, float dx = 0f)
        {
            var start = new Vector3(G.Cards.X + dx, Rest, palmZ);
            s.Move(h, 0.5, HandTarget.At(start + new Vector3(0, 0.06f, 0)));
            s.Move(h, 0.3, HandTarget.At(start));
            s.Wait(0.3);
            s.Move(h, duration, HandTarget.At(start + new Vector3(0, 0, distance)));
            s.Wait(0.3);
            return s;
        }

        /// <summary>
        /// 拿起牌、劃弧線放到棄牌堆（2026-10-12 實機 log 164.8／166.5／168.6 s，使用者確認是棄牌）：
        /// 手碰到牌 (0.03, 0.11)、往前 4 cm，0.1 s 內手掌拱到 peak 高、手側傾（roll 度），往右前方落在 (0.22, landHeight, 0.29)。
        /// backToCards：拱起後又放回牌上（反例：不是棄牌）。
        /// </summary>
        public static SyntheticHands FoldCarryArc(SyntheticHands s, Hand h = Hand.Right, float peak = 0.22f, float roll = 85f, float landHeight = 0.07f, bool backToCards = false)
        {
            float side = h == Hand.Right ? 1f : -1f;
            var touch = new Vector3(side * 0.03f, 0.04f, 0.11f);
            s.Move(h, 0.5, HandTarget.At(touch + new Vector3(side * 0.08f, 0.06f, -0.02f)));
            s.Move(h, 0.25, HandTarget.At(touch));
            s.Wait(0.3);
            s.Move(h, 0.1, HandTarget.At(touch + new Vector3(side * 0.01f, 0, 0.04f)));
            s.Move(h, 0.1, HandTarget.At(new Vector3(side * 0.12f, peak, 0.16f)).WithRoll(side * roll));
            if (backToCards)
            {
                s.Move(h, 0.3, HandTarget.At(touch + new Vector3(0, 0, 0.01f)).WithRoll(0));
                s.Wait(0.8);
                return s;
            }
            s.Move(h, 0.15, HandTarget.At(new Vector3(side * 0.20f, 0.117f, 0.29f)).WithRoll(0));
            s.Move(h, 0.08, HandTarget.At(new Vector3(side * 0.22f, landHeight, 0.29f)));
            s.Wait(0.15);
            s.Move(h, 0.3, HandTarget.At(new Vector3(side * 0.17f, 0.10f, 0.18f)));
            s.Wait(0.5);
            return s;
        }

        /// <summary>All-in：雙手放籌碼堆兩側（兩手距離 gap），一起往前推 distance。lag：左手晚出發的秒數。</summary>
        public static SyntheticHands AllIn(SyntheticHands s, float gap = 0.18f, float distance = 0.26f, double duration = 0.55, double lag = 0, double holdAfter = 1.4, Vector3? center = null, double settle = 0.3)
        {
            var c = (center ?? G.Stack) + new Vector3(0, 0.03f, -0.01f);
            var l = c - new Vector3(gap / 2, 0, 0);
            var r = c + new Vector3(gap / 2, 0, 0);
            s.Move(0.5, HandTarget.At(r + new Vector3(0, 0.05f, 0)), HandTarget.At(l + new Vector3(0, 0.05f, 0)));
            s.Move(0.25, HandTarget.At(r), HandTarget.At(l));
            s.Wait(settle);
            var fwd = new Vector3(0, 0, distance);
            if (lag <= 0)
                s.Move(duration, HandTarget.At(r + fwd), HandTarget.At(l + fwd));
            else
            {
                s.Move(Hand.Right, lag, HandTarget.At(r + fwd * (float)System.Math.Min(1, lag / duration)));
                s.Move(duration, HandTarget.At(r + fwd), HandTarget.At(l + fwd));
            }
            s.Wait(holdAfter);
            return s;
        }

        /// <summary>AllInFromEdge／RestingDrift 開始往前推（漂）的時間（秒）：先靜止 0.8 s 讓 All-in 進入預備。</summary>
        public const double EdgePushStart = 0.8;

        /// <summary>
        /// 從桌緣的準備姿勢出發的 All-in（2026-10-13 實機 log：雙手準備時手掌 z≈0.01–0.19、多在 0.07 上下，左 x≈−0.17、右 +0.13），
        /// 雙手一起往前推 distance、花 duration 秒，之後停住 holdAfter。起點加推的距離不一定到下注線。
        /// 雙手先直接放在起點（不從別處移過來，移過來那段本身就是往前移動）。desync：一隻手多推的距離（正＝右手、負＝左手）。
        /// </summary>
        public static SyntheticHands AllInFromEdge(SyntheticHands s, float startZ = 0.04f, float distance = 0.13f, double duration = 0.45,
            float gap = 0.30f, float centerX = -0.02f, double holdAfter = 1.0, float height = Rest, float desync = 0f)
        {
            var l = new Vector3(centerX - gap / 2, height, startZ);
            var r = new Vector3(centerX + gap / 2, height, startZ);
            s.Set(Hand.Left, l).Set(Hand.Right, r);
            s.Wait(EdgePushStart);
            s.Move(duration, HandTarget.At(r + new Vector3(0, 0, distance + System.Math.Max(0, desync))), HandTarget.At(l + new Vector3(0, 0, distance + System.Math.Max(0, -desync))));
            s.Wait(holdAfter);
            return s;
        }

        /// <summary>雙手放在桌上休息，慢慢一起往前漂 distance（duration 秒，ease 預設平滑加減速），之後停住。不是 All-in。</summary>
        public static SyntheticHands RestingDrift(SyntheticHands s, float distance = 0.12f, double duration = 2.0, float startZ = 0.04f,
            float gap = 0.30f, float centerX = -0.02f, Ease ease = Ease.Smooth)
        {
            var l = new Vector3(centerX - gap / 2, Rest, startZ);
            var r = new Vector3(centerX + gap / 2, Rest, startZ);
            s.Set(Hand.Left, l).Set(Hand.Right, r);
            s.Wait(EdgePushStart);
            s.Move(duration, HandTarget.At(r + new Vector3(0, 0, distance)), HandTarget.At(l + new Vector3(0, 0, distance)), ease);
            s.Wait(1.2);
            return s;
        }

        /// <summary>
        /// 雙手各滑各的：左手壓在牌上往前滑 cardDist（duration 秒）；右手 lag 秒後捏起籌碼、貼著桌面往前滑 chipDist。
        /// lag 為負時右手先動。不是 All-in（左手可能構成推牌棄牌）。
        /// </summary>
        public static SyntheticHands SlideCardsAndChips(SyntheticHands s, double lag = 0.6, float cardDist = 0.12f, float chipDist = 0.14f,
            double duration = 0.6, float pinch = 1f)
        {
            var cardPalm = new Vector3(G.Cards.X - 0.02f, Rest, G.Cards.Z - 0.03f);
            var chipPinch = G.Stack + new Vector3(0, 0.02f, 0);
            var chipPalm = SyntheticHands.PalmForPinch(Hand.Right, chipPinch);
            s.Move(0.5, HandTarget.At(chipPalm + new Vector3(0, 0.06f, 0)), HandTarget.At(cardPalm + new Vector3(0, 0.06f, 0)));
            s.Move(0.25, HandTarget.At(chipPalm), HandTarget.At(cardPalm));
            s.Move(Hand.Right, 0.15, HandTarget.Pinch(pinch));
            s.Wait(0.3);
            // 各自平滑加減速，右手晚 lag 秒（負值＝左手晚）；切成小段直線移動
            double lt = System.Math.Max(0, -lag), rt = System.Math.Max(0, lag), total = System.Math.Max(lt, rt) + duration;
            const double step = 1.0 / 36;
            for (double t = step; t < total + step / 2; t += step)
            {
                float pl = Smooth01((t - lt) / duration), pr = Smooth01((t - rt) / duration);
                s.Move(step, HandTarget.At(chipPalm + new Vector3(0, 0, chipDist * pr)), HandTarget.At(cardPalm + new Vector3(0, 0, cardDist * pl)), Ease.Linear);
            }
            s.Wait(1.6);
            return s;
        }

        static float Smooth01(double u)
        {
            float x = (float)System.Math.Min(1, System.Math.Max(0, u));
            return x * x * (3 - 2 * x);
        }

        /// <summary>單手往前推（籌碼堆旁）。</summary>
        public static SyntheticHands OneHandPush(SyntheticHands s, Hand h = Hand.Right, float distance = 0.26f)
        {
            var c = G.Stack + new Vector3(h == Hand.Right ? 0.09f : -0.09f, 0.03f, -0.01f);
            s.Move(h, 0.5, HandTarget.At(c + new Vector3(0, 0.05f, 0)));
            s.Move(h, 0.25, HandTarget.At(c));
            s.Wait(0.3);
            s.Move(h, 0.55, HandTarget.At(c + new Vector3(0, 0, distance)));
            s.Wait(1.4);
            return s;
        }

        /// <summary>捏底牌的位置：牌心往玩家方向 3 cm、離桌 1 cm（捏住兩張牌靠近自己那一端）。</summary>
        public static readonly Vector3 CardGrip = G.Cards + new Vector3(0f, 0.01f, -0.03f);

        static Vector3 RestOf(Hand h) => h == Hand.Right ? SyntheticHands.RightRest : SyntheticHands.LeftRest;

        /// <summary>手移到牌上方，往下讓捏合點落在 grip，捏住（pinch 0–1）。</summary>
        public static SyntheticHands GrabCards(SyntheticHands s, Hand h = Hand.Right, Vector3? grip = null, float pinch = 1f)
        {
            var palm = SyntheticHands.PalmForPinch(h, grip ?? CardGrip);
            s.Move(h, 0.5, HandTarget.At(palm + new Vector3(0, 0.06f, 0)));
            s.Move(h, 0.3, HandTarget.At(palm));
            s.Move(h, 0.15, HandTarget.Pinch(pinch));
            s.Wait(0.1);
            return s;
        }

        /// <summary>
        /// 拿起底牌看：捏住牌、花 lift 秒把捏合點移到 lookAt（預設往上 21 cm、往自己臉的方向拉回 8 cm），停 hold 秒；
        /// 給了 releaseAt 就先移過去再放開；放開後手回休息處。pitch：抬起時手腕俯仰（負值＝手指朝上，牌面轉向臉）。
        /// </summary>
        public static SyntheticHands LiftCards(SyntheticHands s, Hand h = Hand.Right, Vector3? lookAt = null, double hold = 1.5, double lift = 0.5,
            Vector3? releaseAt = null, float pitch = -25f, Vector3? grip = null, double releaseTravel = 0.5)
        {
            GrabCards(s, h, grip);
            var to = lookAt ?? new Vector3(0f, 0.22f, 0.04f);
            s.Move(h, lift, HandTarget.At(SyntheticHands.PalmForPinch(h, to)).WithPitch(pitch));
            s.Wait(hold);
            if (releaseAt.HasValue) s.Move(h, releaseTravel, HandTarget.At(SyntheticHands.PalmForPinch(h, releaseAt.Value)).WithPitch(0));
            s.Move(h, 0.12, HandTarget.Pinch(0f));
            s.Move(h, 0.3, HandTarget.At(s.Palm(h) + new Vector3(0, 0.06f, 0)));
            s.Move(h, 0.4, HandTarget.At(RestOf(h)));
            s.Wait(0.5);
            return s;
        }

        /// <summary>
        /// 捏起底牌拿到棄牌堆放下（棄牌）：捏住、0.15 s 抬到 peak（手掌高）往右前方，0.25 s 落到棄牌堆中心（手掌離桌 landHeight），放開。
        /// </summary>
        public static SyntheticHands CarryCardsToMuck(SyntheticHands s, Hand h = Hand.Right, float peak = 0.13f, float landHeight = 0.05f, double travel = 0.25)
        {
            GrabCards(s, h);
            var start = s.Palm(h);
            var muck = G.Layout.MuckCenter; // 棄牌堆在右前方（兩隻手都放這裡）
            s.Move(h, 0.15, HandTarget.At(new Vector3((start.X + muck.X) * 0.5f, peak, (start.Z + muck.Z) * 0.5f)));
            s.Move(h, travel, HandTarget.At(new Vector3(muck.X, landHeight, muck.Z)));
            s.Wait(0.1);
            s.Move(h, 0.12, HandTarget.Pinch(0f));
            s.Move(h, 0.3, HandTarget.At(new Vector3(muck.X, 0.12f, muck.Z - 0.1f)));
            s.Move(h, 0.4, HandTarget.At(RestOf(h)));
            s.Wait(0.5);
            return s;
        }

        /// <summary>
        /// 叫出選單：手抬到 palm（手掌位置）並翻成掌心朝上（roll 度，180＝正朝上；tilt 再往小指側多斜幾度），停 hold 秒。
        /// curl＝四指彎曲（0 張開）、pinch＝捏合。不回到休息位置（接著通常是另一手去點按鈕）。
        /// </summary>
        public static SyntheticHands PalmUp(SyntheticHands s, Hand h, Vector3 palm, double hold = 0.6, float tilt = 0f, float curl = 0f, float pinch = 0f, double raise = 0.5)
        {
            float side = h == Hand.Right ? 1f : -1f;
            s.Move(h, raise, HandTarget.At(palm).WithRoll(side * (180f - tilt)).WithCurl(curl).WithPinch(pinch));
            s.Wait(hold);
            return s;
        }

        /// <summary>某隻手（張開、掌心朝下）要讓食指尖落在 tip 時，手掌該放哪裡。</summary>
        public static Vector3 PalmForIndexTip(Hand h, Vector3 tip) => tip - SyntheticHands.JointOffset(h, HandJoint.IndexTip);

        /// <summary>用 h 的食指尖點 tip：從 from（指尖位置）移過來 approach 秒，停 hold 秒。</summary>
        public static SyntheticHands IndexTo(SyntheticHands s, Hand h, Vector3 tip, double approach = 0.35, double hold = 0.3, Vector3? from = null)
        {
            if (from.HasValue) s.Move(h, 0.01, HandTarget.At(PalmForIndexTip(h, from.Value)), Ease.Linear);
            s.Move(h, approach, HandTarget.At(PalmForIndexTip(h, tip)));
            s.Wait(hold);
            return s;
        }

        /// <summary>攤牌翻牌：手在牌上方、掌心朝下，翻成掌心朝上（roll 度）。</summary>
        public static SyntheticHands Flip(SyntheticHands s, Hand h = Hand.Right, float roll = 180f, double duration = 0.4, float height = 0.06f, Vector3? at = null)
        {
            var p = (at ?? G.Cards) + new Vector3(0, height, 0);
            s.Move(h, 0.5, HandTarget.At(p));
            s.Wait(0.3);
            s.Move(h, duration, HandTarget.At(p + new Vector3(0, 0.04f, 0)).WithRoll(roll));
            s.Wait(0.4);
            return s;
        }
    }
}
