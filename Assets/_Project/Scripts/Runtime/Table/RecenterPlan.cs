namespace Poker.Runtime
{
    /// <summary>
    /// 系統重新置中（長按 Meta 鍵／右手長捏）時牌桌要做的事。2026-10-15 實機：玩家重新置中就是要「把桌子叫到我面前」，
    /// 先前「跟著錨點留在原地」違反直覺。所以不論桌子從哪來：放到面前 → 面前有真實桌面就對齊 → 顯示雙手校準提示。
    /// 新位置校準後會照常存成新的錨點（TableAnchorFlow）。
    /// </summary>
    public readonly struct RecenterPlan
    {
        public readonly bool PlaceInFront, LookForDeskInFront, PromptCalibration, FollowOldAnchor;
        RecenterPlan(bool place, bool desk, bool prompt, bool follow) { PlaceInFront = place; LookForDeskInFront = desk; PromptCalibration = prompt; FollowOldAnchor = follow; }

        public static RecenterPlan For(TablePlacementSource source) => new RecenterPlan(true, true, true, false);
    }
}
