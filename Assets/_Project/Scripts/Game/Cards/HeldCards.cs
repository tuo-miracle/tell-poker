using System;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 玩家拿在手上看的兩張底牌：跟著捏合點（平滑），牌面朝向玩家的頭、牌的上緣朝上（看起來是正的），兩張左右錯開成小扇形。
    /// 放開時用短動畫蓋回原位（背面朝上）；棄牌時只把牌轉平，位置交給棄牌動畫。
    /// 座標一律是牌桌本地座標（TableProps.Root 的本地＝牌桌座標）。只有玩家自己看得到：AI 與訊號從不讀牌（手勢事件、PlayerSignals 都沒有牌的欄位）。
    /// </summary>
    public sealed class HeldCards
    {
        /// <summary>兩張牌中心的左右間距（公尺）與扇形角度（度，上緣往外張）。</summary>
        public const float Gap = 0.04f, FanDegrees = 7f;
        /// <summary>捏住的是牌的下緣：牌心在握點上方這麼遠。</summary>
        public const float AboveGrip = CardView.Length * 0.35f;
        /// <summary>跟手的平滑時間常數：剛拿起時牌從桌上飛到手裡用較長的，之後緊跟。</summary>
        public const float CatchUpTime = 0.08f, FollowTime = 0.04f, CatchUpSeconds = 0.25f;
        /// <summary>放回桌上、棄牌時轉平的動畫秒數。</summary>
        public const float ReturnSeconds = 0.3f, SettleSeconds = 0.25f;

        readonly CardView[] _cards;
        readonly Func<int, Vector3> _rest;
        readonly Vector3[] _fromPos = new Vector3[2];
        readonly Quaternion[] _fromRot = new Quaternion[2];
        float _age, _t;
        bool _settling;

        /// <summary>牌正跟著手。</summary>
        public bool Active { get; private set; }
        /// <summary>正在蓋回原位。</summary>
        public bool Returning { get; private set; }
        public bool Busy => Active || Returning || _settling;

        /// <param name="cards">玩家的兩張牌。</param>
        /// <param name="rest">第 i 張牌在桌上的位置（牌桌本地座標，含牌的厚度高度）。</param>
        public HeldCards(CardView[] cards, Func<int, Vector3> rest)
        {
            _cards = cards;
            _rest = rest;
        }

        /// <summary>
        /// 第 index 張牌拿在手上的姿勢（牌桌本地座標）。牌的本地 −Y 是牌面：轉成朝向頭；+Z 是牌面上緣：轉成朝上。
        /// 第 0 張在玩家看來偏左（和桌上的順序一樣）。
        /// </summary>
        public static void CardPose(int index, Vector3 grip, Vector3 head, out Vector3 position, out Quaternion rotation)
        {
            var toHead = head - grip;
            toHead = toHead.sqrMagnitude > 1e-8f ? toHead.normalized : Vector3.back;
            var up = Vector3.ProjectOnPlane(Vector3.up, toHead);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.forward, toHead);
            up.Normalize();
            var basis = Quaternion.LookRotation(up, -toHead);
            // 牌的本地 +X 在看的人眼中是左邊（桌上翻開的牌也是如此），所以看的人的右邊是 −X
            var viewerRight = basis * Vector3.left;
            float side = index == 0 ? -1f : 1f;
            position = grip + up * AboveGrip + viewerRight * (side * Gap * 0.5f) + toHead * (index * 0.002f);
            // 從看的人那側看，正角度是順時針：右邊那張上緣往右張、左邊那張往左
            rotation = Quaternion.AngleAxis(side * FanDegrees, toHead) * basis;
        }

        /// <summary>拿起來：之後每幀 Follow。</summary>
        public void Lift()
        {
            Active = true;
            Returning = false;
            _settling = false;
            _age = 0f;
            foreach (var c in _cards) c.SetPeek(0f);
        }

        /// <summary>跟著握點（牌桌本地座標）與頭的位置更新。</summary>
        public void Follow(Vector3 grip, Vector3 head, float dt)
        {
            if (!Active) return;
            _age += dt;
            float tau = _age < CatchUpSeconds ? CatchUpTime : FollowTime;
            float a = dt <= 0f ? 0f : 1f - Mathf.Exp(-dt / tau);
            for (int i = 0; i < _cards.Length; i++)
            {
                CardPose(i, grip, head, out var p, out var r);
                var t = _cards[i].transform;
                t.localPosition = Vector3.Lerp(t.localPosition, p, a);
                t.localRotation = Quaternion.Slerp(t.localRotation, r, a);
            }
        }

        /// <summary>放開：用 ReturnSeconds 蓋回桌上原位。</summary>
        public void Return()
        {
            if (!Active) return;
            Active = false;
            Returning = true;
            _settling = false;
            _t = 0f;
            Capture();
        }

        /// <summary>棄牌：不再跟手，牌在原地轉平（位置由棄牌動畫接手）。</summary>
        public void Detach()
        {
            if (!Active && !Returning) return;
            Active = false;
            Returning = false;
            _settling = true;
            _t = 0f;
            Capture();
        }

        /// <summary>立刻放回桌上（攤牌逾時要在原位翻牌時用）。</summary>
        public void SnapBack()
        {
            if (!Busy) return;
            Active = Returning = _settling = false;
            for (int i = 0; i < _cards.Length; i++) LayFlat(i, _rest(i));
        }

        /// <summary>新的一手：狀態清掉（牌由發牌重新擺放）。</summary>
        public void Clear() => Active = Returning = _settling = false;

        public void Tick(float dt)
        {
            if (!Returning && !_settling) return;
            float dur = Returning ? ReturnSeconds : SettleSeconds;
            _t = Mathf.Min(1f, _t + dt / dur);
            float e = _t * _t * (3f - 2f * _t);
            for (int i = 0; i < _cards.Length; i++)
            {
                var t = _cards[i].transform;
                if (Returning) t.localPosition = Vector3.Lerp(_fromPos[i], _rest(i), e);
                t.localRotation = Quaternion.Slerp(_fromRot[i], Flat(_cards[i]), e);
            }
            if (_t < 1f) return;
            for (int i = 0; i < _cards.Length; i++)
            {
                if (Returning) _cards[i].transform.localPosition = _rest(i);
                _cards[i].SetYaw(_cards[i].Yaw); // 角度回到 CardView 自己的狀態（背面朝上、原本的水平朝向）
            }
            Returning = _settling = false;
        }

        void Capture()
        {
            for (int i = 0; i < _cards.Length; i++)
            {
                _fromPos[i] = _cards[i].transform.localPosition;
                _fromRot[i] = _cards[i].transform.localRotation;
            }
        }

        void LayFlat(int i, Vector3 pos)
        {
            _cards[i].transform.localPosition = pos;
            _cards[i].SetYaw(_cards[i].Yaw);
        }

        static Quaternion Flat(CardView c) => Quaternion.Euler(0f, c.Yaw, 0f) * Quaternion.AngleAxis(c.FaceUp ? 180f : 0f, Vector3.forward);
    }
}
