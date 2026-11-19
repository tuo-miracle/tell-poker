using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 貼在桌面上的指尖按鈕判定（牌桌座標）。指尖要先在按鈕上方懸停（上膛），再往下按到門檻高度以下才算一次按下；
    /// 之後要抬起才能再按，並有冷卻時間。這樣手在桌面高度滑進來、或做手勢時掃過，都不會誤觸。
    /// </summary>
    public sealed class FingertipButton
    {
        /// <summary>指尖相對按鈕面的高度低於這個值算按下（真實桌面會擋住手指，所以不能要求穿過桌面）。</summary>
        public float PressHeight = 0.02f;
        /// <summary>高於這個值（且低於 HoverCeiling）算懸停上膛；按下後要抬過這個高度才能再按。</summary>
        public float ArmHeight = 0.035f;
        public float HoverCeiling = 0.15f;
        public float Margin = 0.01f;
        public double Cooldown = 0.5;
        /// <summary>上膛要在按鈕上方懸停至少這麼久（秒；預設 0＝一進懸停高度就上膛）。手勢備援按鈕用：手直接落下（放手、敲桌）不算按。</summary>
        public double ArmDwell;

        public Vector3 Center;
        public Vector2 HalfSize;   // x 沿牌桌 X，y 沿牌桌 Z

        bool _armed;
        bool _latched;
        double _hoverSince = double.NaN;
        double _lastPress = double.NegativeInfinity;

        public FingertipButton(Vector3 center, Vector2 halfSize)
        {
            Center = center;
            HalfSize = halfSize;
        }

        /// <summary>手指正在按鈕上方懸停或按住（顯示用）。</summary>
        public bool IsHovered { get; private set; }
        /// <summary>已上膛（懸停夠久），往下按就會觸發（顯示用）。</summary>
        public bool IsReady => _armed && !_latched;

        /// <summary>每幀呼叫；tip 是食指指尖的牌桌座標（沒追蹤到傳 null）。回傳這一幀是否按下。</summary>
        public bool Step(Vector3? tip, double time)
        {
            IsHovered = false;
            if (!tip.HasValue)
            {
                _armed = false;
                _latched = false;
                _hoverSince = double.NaN;
                return false;
            }

            var p = tip.Value;
            bool inside = Mathf.Abs(p.x - Center.x) <= HalfSize.x + Margin && Mathf.Abs(p.z - Center.z) <= HalfSize.y + Margin;
            float h = p.y - Center.y;
            if (!inside || h > HoverCeiling)
            {
                _armed = false;
                _latched = false;
                _hoverSince = double.NaN;
                return false;
            }

            IsHovered = true;
            if (_latched)
            {
                if (h > ArmHeight) { _latched = false; _hoverSince = time; _armed = ArmDwell <= 0; }
                return false;
            }
            if (h > ArmHeight)
            {
                if (double.IsNaN(_hoverSince)) _hoverSince = time;
                if (time - _hoverSince >= ArmDwell) _armed = true;
                return false;
            }
            if (h < PressHeight) _hoverSince = double.NaN;   // 到了按下高度：懸停重新計（沒上膛的這一下不算）
            if (_armed && h < PressHeight)
            {
                _armed = false;
                if (time - _lastPress < Cooldown) return false;   // 冷卻中：吃掉這次，要重新上膛
                _latched = true;
                _lastPress = time;
                return true;
            }
            return false;
        }
    }
}
