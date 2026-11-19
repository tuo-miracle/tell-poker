using Poker.Core;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 看對手丟進來的籌碼：頭部射線停在某個 AI 的本街下注堆（或底池）約 0.3 秒，上方浮出金額與「還要跟多少」。
    /// 用自己的 GazeTracker（不接 ISDK 注視來源、不發 PlayerSignals），所以盯籌碼不算盯角色；
    /// 射線同時打到角色與籌碼堆時取比較近的那個。
    /// </summary>
    public sealed partial class GameController
    {
        public BetLabel BetLabel { get; private set; }

        /// <summary>只用頭部射線（不讓 GazeTracker.DefaultSource 的角色注視蓋掉籌碼堆命中）。</summary>
        sealed class HeadRayOnly : IGazeTargetSource
        {
            public bool TryGetTarget(out int target) { target = -1; return false; }
        }

        readonly GazeTracker _pileGaze = new GazeTracker
        {
            Source = new HeadRayOnly(), DwellSeconds = BetLabel.DwellSeconds, ReleaseSeconds = BetLabel.ReleaseSeconds,
        };
        const float BetPileRadius = 0.06f, PotPileRadius = 0.07f;
        bool _betLabelLogged;
        int _shownTarget = -1;
        HandState _shownState;

        void InitBetLabel() => BetLabel = new BetLabel(Props.Root);

        /// <summary>射線打到的下注堆（AI 座位號）或底池（BetLabel.PotTarget）；空堆不算。多個都打到時取最貼近射線的那個。</summary>
        int HitBetPile(Vector3 o, Vector3 d, out float distance)
        {
            int hit = -1;
            float bestMiss = float.MaxValue, along = float.MaxValue;
            var root = Table.transform;
            void Test(int id, Vector3 local, float radius)
            {
                var c = root.TransformPoint(local);
                if (!GazeTracker.RayHitsSphere(o, d, c, radius, out float t)) return;
                float miss = (c - (o + d * t)).sqrMagnitude;
                if (miss < bestMiss) { bestMiss = miss; hit = id; along = t; }
            }
            distance = float.MaxValue;
            if (Session.State.Phase != HandPhase.Betting) return -1;
            foreach (var seat in Session.AiSeats)
                if (Props.Bet[seat].Go.activeSelf && Session.State.Seat(seat).StreetBet > 0)
                    Test(seat, PileTop(Props.Bet[seat], Spots.BetOf(seat)) * 0.5f + Spots.BetOf(seat) * 0.5f, BetPileRadius);
            if (Props.Pot.Go.activeSelf && Session.Pot > 0)
                Test(BetLabel.PotTarget, PileTop(Props.Pot, Spots.PotPile) * 0.5f + Spots.PotPile * 0.5f, PotPileRadius);
            distance = along;
            return hit;
        }

        static Vector3 PileTop(ChipPile pile, Vector3 spot) =>
            spot + Vector3.up * (Mathf.Min(Mathf.Max(0, pile.Chips) + (pile.Odd ? 1 : 0), ChipStackMesh.MaxPerColumn) * ChipStackMesh.Thickness);

        void TickBetLabel()
        {
            int target = _pileGaze.Current;
            var s = Session.State;
            // Core 狀態不可變：同一個 HandState、同一個目標 → 字不用重算（每幀呼叫，不要每幀配置字串）
            if (target == _shownTarget && ReferenceEquals(s, _shownState) && BetLabel.IsVisible == (target >= 0))
            {
                if (BetLabel.IsVisible) Place(target);
                return;
            }
            _shownTarget = target;
            _shownState = s;
            int amount = 0, toCall = 0;
            string line1 = null, line2 = null;
            if (target >= 0 && s.Phase == HandPhase.Betting)
            {
                if (target == BetLabel.PotTarget)
                {
                    amount = Session.Pot;
                    if (amount > 0 && Props.Pot.Go.activeSelf) line1 = BetLabelText.PotLine(amount);
                }
                else if (Character(target) != null)
                {
                    amount = s.Seat(target).StreetBet;
                    if (amount > 0 && Props.Bet[target].Go.activeSelf)
                    {
                        line1 = BetLabelText.BetLine(Session.NameOf(target), amount);
                        line2 = BetLabelText.ToCallLine(s, Human);
                        toCall = BetLabelText.AmountToCall(s, Human);
                    }
                }
            }
            if (line1 == null)
            {
                if (BetLabel.IsVisible) BetLabel.Hide();
                _betLabelLogged = false;
                _shownTarget = -1;   // 籌碼堆空了又補上（例如下一街）時重新判斷
                return;
            }
            BetLabel.Show(target, line1, line2, TopOf(target));
            if (!_betLabelLogged)
            {
                _betLabelLogged = true;
                string seat = target == BetLabel.PotTarget ? "pot" : target.ToString();
                Debug.Log($"[UX] bet label seat={seat} amount={amount} toCall={toCall}");
            }
            if (Head != null) BetLabel.Face(Head.position);
        }

        /// <summary>籌碼堆高度會變（飛進來的籌碼落地），位置每幀跟著走。</summary>
        void Place(int target)
        {
            BetLabel.Show(target, BetLabel.Line1, BetLabel.Line2, TopOf(target));
            if (Head != null) BetLabel.Face(Head.position);
        }

        Vector3 TopOf(int target) =>
            target == BetLabel.PotTarget ? PileTop(Props.Pot, Spots.PotPile) : PileTop(Props.Bet[target], Spots.BetOf(target));
    }
}
