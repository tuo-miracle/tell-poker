using Poker.Gestures;
using NVec = System.Numerics.Vector3;
using NQuat = System.Numerics.Quaternion;

namespace Poker.UX
{
    /// <summary>
    /// 非慣用手手腕上的選單按鈕（牌桌座標），用另一手食指點。2026-10-14 起是次要路徑（主要是掌心朝上叫出的 PalmMenu）。
    /// 按鈕在手背那一側（ISDK 關節軸：+Y 手背 Dorsal、+Z 指向手指 Distal，OpenXRHandPrimitives.cs:62、:82），
    /// 所以和系統選單手勢（掌心朝自己＋捏合）不衝突。
    /// 判定用 TouchButton（方盒）：手腕座標系裡「高度」是沿手背法線的距離；指尖在按鈕範圍內停 Dwell 秒就按（不用先懸停上膛）。
    /// 食指碰到手腕時 Quest 常掉按鈕那手的追蹤（手疊手遮擋）：OwnerGrace 秒內沿用最後追蹤到的手腕姿勢。
    /// </summary>
    public sealed class WristMenuButton
    {
        /// <summary>按鈕中心相對手腕關節：手背上方 1.5 cm、往手肘 3 cm（-Z），離捏籌碼的指節遠一點。</summary>
        public static readonly NVec LocalOffset = new NVec(0f, 0.015f, -0.03f);
        /// <summary>顯示的按鈕直徑（公尺）。</summary>
        public const float VisualDiameter = 0.04f;
        /// <summary>判定半寬（沿手腕 X、Z）與邊界：實際可按範圍 9 cm 見方（舊版 7 cm）。</summary>
        public const float HalfSize = 0.03f, Margin = 0.015f;
        /// <summary>沿手背法線、相對按鈕中心的高度範圍：皮膚在 -1.5 cm，上方到 3.5 cm。</summary>
        public const float MinHeight = -0.02f, MaxHeight = 0.035f;
        public const double Dwell = 0.2, Cooldown = 0.6, OwnerGrace = 0.5;

        readonly TouchButton _button = TouchButton.Box(new System.Numerics.Vector2(HalfSize, HalfSize), Margin, MinHeight, MaxHeight);
        Hand? _lastHand;
        HandPose _owner;
        double _ownerSeen = double.NaN;
        double _blockedLogged = double.NegativeInfinity;

        public MenuSummonLog Log;

        public WristMenuButton()
        {
            _button.Dwell = Dwell;
            _button.Cooldown = Cooldown;
            _button.PressOnCross = false;
        }

        public bool IsHovered => _button.IsHovered;
        public float Progress => _button.Progress;

        public static NVec ButtonCenter(HandPose hand)
            => hand.Position(HandJoint.Wrist) + NVec.Transform(LocalOffset, hand.Rotation(HandJoint.Wrist));

        /// <summary>每幀呼叫；buttonHand 是戴按鈕的手；blocked＝拿著牌或籌碼（不按）。回傳這一幀是否按下。</summary>
        public bool Step(HandsFrame frame, Hand buttonHand, double time, bool blocked = false)
        {
            if (_lastHand != buttonHand) { _button.Cancel(); _owner = null; _ownerSeen = double.NaN; _lastHand = buttonHand; }
            var owner = frame.Get(buttonHand);
            var finger = frame.Get(buttonHand == Hand.Left ? Hand.Right : Hand.Left);
            if (owner.Tracked) { _owner = owner.Clone(); _ownerSeen = time; }
            bool ownerOk = _owner != null && time - _ownerSeen <= OwnerGrace;
            if (!ownerOk)
            {
                if (_button.Approaching && _owner != null) { _button.Cancel(); Report(time, "tracking-lost wrist"); }
                _owner = null;
                return false;
            }

            NVec? local = null;
            if (finger.Tracked)
            {
                var rot = _owner.Rotation(HandJoint.Wrist);
                local = NVec.Transform(finger.Position(HandJoint.IndexTip) - ButtonCenter(_owner), NQuat.Inverse(rot));
            }
            if (blocked)
            {
                if (local.HasValue && _button.Distance(local.Value) <= 0f && time - _blockedLogged > 1.0)
                {
                    _blockedLogged = time;
                    Log?.NearMiss(time, "blocked-holding", "via=wrist");
                }
                _button.Cancel();
                return false;
            }
            bool pressed = _button.Step(local, time);
            if (_button.LastMiss.HasValue) Report(time, _button.LastMiss.Value.Kind == "dropout" ? "tracking-lost finger" : "wrist-missed");
            return pressed;
        }

        void Report(double time, string reason)
        {
            if (!_button.LastMiss.HasValue) return;
            var m = _button.LastMiss.Value;
            Log?.NearMiss(time, reason, $"via=wrist closest={(m.Closest * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}cm dwell={m.Dwell.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}s");
        }
    }
}
