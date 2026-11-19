using Poker.Characters;
using Poker.Core;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>下注標籤的文字。金額全部讀 Core 的狀態（本街下注、跟注水位、剩餘籌碼），不另算規則。</summary>
    public static class BetLabelText
    {
        /// <summary>
        /// 這個座位要跟注還得放入多少（不夠就是全部籌碼）。已棄牌／已 all-in／不在下注中 → 0。
        /// 輪到他時直接用 Core 的 LegalActions.CallAmount；還沒輪到時 LegalActions 沒有值，用同一條式子：min(跟注水位 − 本街已下, 剩餘籌碼)。
        /// </summary>
        public static int AmountToCall(HandState s, int seat)
        {
            if (s == null || s.Phase != HandPhase.Betting || seat < 0 || seat >= s.SeatCount) return 0;
            var me = s.Seat(seat);
            if (me.Status != SeatStatus.Active) return 0;
            if (s.ToAct == seat) return Engine.GetLegalActions(s, seat).CallAmount;
            return Mathf.Min(Mathf.Max(0, s.CurrentBet - me.StreetBet), me.Stack);
        }

        /// <summary>第二行："To call: 80"；輪到你且可過牌 → "Check"；籌碼不夠跟 → "All-in 45"；沒事可做 → null。</summary>
        public static string ToCallLine(HandState s, int seat)
        {
            if (s == null || s.Phase != HandPhase.Betting || seat < 0 || seat >= s.SeatCount) return null;
            var me = s.Seat(seat);
            if (me.Status != SeatStatus.Active) return null;
            int need = AmountToCall(s, seat);
            if (need <= 0) return s.ToAct == seat && Engine.GetLegalActions(s, seat).CanCheck ? "Check" : null;
            return s.CurrentBet - me.StreetBet >= me.Stack ? "All-in " + need : "To call: " + need;
        }

        public static string BetLine(string name, int amount) => name + " bet " + amount;
        public static string PotLine(int pot) => "Pot " + pot;
    }

    /// <summary>
    /// 盯著某堆下注（或底池）時浮在它上方的兩行字（"Rocco bet 120" / "To call: 115"），面向玩家。
    /// 只負責擺放與外觀（字幕大小、高對比）；何時顯示、顯示什麼由 GameController 決定。
    /// </summary>
    public sealed class BetLabel
    {
        /// <summary>底池的目標編號（下注堆用座位號）。</summary>
        public const int PotTarget = 200;
        public const float DwellSeconds = 0.3f, ReleaseSeconds = 0.5f;
        /// <summary>第一行底部離籌碼堆頂端的高度、兩行間距（公尺，會乘上字幕倍率）。</summary>
        // 兩行底板不重疊（兩行底板都高約 3.9 cm，2026-11-02 第二行放大到和第一行一樣；面向低處的玩家時會往後仰，再留 0.6 cm）
        const float Lift = 0.04f, LineGap = 0.046f;

        static readonly Color MainPlate = new Color(1f, 0.88f, 0.4f);      // 和手上籌碼金額同色
        static readonly Color DetailPlate = new Color(0.98f, 0.95f, 0.86f);
        static readonly Color ContrastPlate = new Color(1f, 0.84f, 0.08f);  // HighContrast.MarkColor

        public readonly WorldLabel Main, Detail;
        readonly Renderer _mainPlate, _detailPlate;
        float _scale = 1f;
        bool _contrast;

        public BetLabel(Transform parent)
        {
            Main = new WorldLabel(parent, "BetLabel", 0.0042f, MainPlate);
            // 第二行和第一行一樣大（2026-11-02 真實尺寸的桌子：Rocco 座的下注離玩家約 1.2 m，0.0038 只有 1.17°）
            Detail = new WorldLabel(parent, "BetLabelDetail", 0.0042f, DetailPlate);
            _mainPlate = Main.Plate.GetComponent<Renderer>();
            _detailPlate = Detail.Plate.GetComponent<Renderer>();
        }

        public bool IsVisible => Main.Go.activeSelf;
        /// <summary>目前標的座位（底池 = PotTarget；沒顯示 = -1）。</summary>
        public int Target { get; private set; } = -1;
        public string Line1 => IsVisible ? Main.Text : null;
        public string Line2 => IsVisible && Detail.Go.activeSelf ? Detail.Text : null;

        /// <summary>字幕大小倍率（UX 設定）。</summary>
        public float Scale
        {
            get => _scale;
            set
            {
                _scale = Mathf.Max(0.1f, value);
                Main.Go.transform.localScale = Detail.Go.transform.localScale = Vector3.one * _scale;
            }
        }

        /// <summary>高對比：兩行都改用高對比黃底。</summary>
        public bool HighContrast
        {
            get => _contrast;
            set
            {
                _contrast = value;
                _mainPlate.sharedMaterial = MaterialCache.Unlit(value ? ContrastPlate : MainPlate);
                _detailPlate.sharedMaterial = MaterialCache.Unlit(value ? ContrastPlate : DetailPlate);
            }
        }

        /// <summary>顯示在 pileTop（牌桌座標，籌碼堆頂端）上方；line2 為 null 時只有一行。</summary>
        public void Show(int target, string line1, string line2, Vector3 pileTop)
        {
            Target = target;
            float lift = Lift * _scale;
            if (line2 != null)
            {
                Detail.Show(line2, pileTop + Vector3.up * lift);
                Main.Show(line1, pileTop + Vector3.up * (lift + LineGap * _scale));
            }
            else
            {
                Detail.Hide();
                Main.Show(line1, pileTop + Vector3.up * lift);
            }
        }

        public void Hide()
        {
            Target = -1;
            Main.Hide();
            Detail.Hide();
        }

        public void Face(Vector3 viewerWorld)
        {
            Main.Face(viewerWorld);
            Detail.Face(viewerWorld);
        }
    }
}
