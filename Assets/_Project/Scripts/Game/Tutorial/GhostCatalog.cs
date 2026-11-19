using System.Collections.Generic;
using System.IO;
using Poker.Gestures;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 示範手（ghost hands）示範的手勢。Talk、Gaze 沒有手，只有字幕（Gaze 另有桌上指向 Rocco 的箭頭）。
    /// Raise＝教學加注那一步：用下注的片段配加注的字幕。Raise、Gaze 只在教學出現，不在選單的手勢頁（All）。
    /// </summary>
    public enum GhostGesture { Peek, RollPeek, LookAtCards, PickChips, Bet, Knock, Fold, AllIn, Flip, Talk, Raise, Gaze }

    /// <summary>
    /// 每個示範手勢的字幕（英文一句）、選單上的短名、片段檔。
    /// 片段是真實錄製（或沒有乾淨錄製時用測試套件的合成動作）裁出來的手勢片段，牌桌座標，
    /// 由 Tests/Gestures/GhostBake 烘焙到 Resources/Ghosts/&lt;名稱&gt;.bytes（v2 .hands 格式）。
    /// </summary>
    public static class GhostCatalog
    {
        public const string ResourceFolder = "Ghosts";

        /// <summary>選單（手勢頁）的排列順序。</summary>
        public static readonly GhostGesture[] All =
        {
            GhostGesture.Peek, GhostGesture.RollPeek, GhostGesture.LookAtCards,
            GhostGesture.PickChips, GhostGesture.Bet, GhostGesture.Knock,
            GhostGesture.Fold, GhostGesture.AllIn, GhostGesture.Flip,
            GhostGesture.Talk,
        };

        static readonly Dictionary<GhostGesture, HandRecording> Cache = new Dictionary<GhostGesture, HandRecording>();

        public static string Caption(GhostGesture g)
        {
            switch (g)
            {
                case GhostGesture.Peek: return "Cup your cards and lift your wrist to peek";
                case GhostGesture.RollPeek: return "Or roll your wrist up to peek";
                case GhostGesture.LookAtCards: return "Pick up your cards to look";
                case GhostGesture.PickChips: return "Pinch chips, lift for more";
                case GhostGesture.Bet: return "Toss into the bet zone";
                case GhostGesture.Knock: return "Knock twice to check";
                case GhostGesture.Fold: return "Push your cards forward to fold";
                case GhostGesture.AllIn: return "Both hands push to go all-in";
                case GhostGesture.Flip: return "Flip your cards at showdown";
                case GhostGesture.Talk: return "Look at a player and talk";
                case GhostGesture.Raise: return "Toss in more than his bet to raise";
                case GhostGesture.Gaze: return "Look at Rocco for a second";
                default: return "";
            }
        }

        public static string MenuLabel(GhostGesture g)
        {
            switch (g)
            {
                case GhostGesture.Peek: return "Peek";
                case GhostGesture.RollPeek: return "Roll peek";
                case GhostGesture.LookAtCards: return "Pick up";
                case GhostGesture.PickChips: return "Chips";
                case GhostGesture.Bet: return "Bet";
                case GhostGesture.Knock: return "Check";
                case GhostGesture.Fold: return "Fold";
                case GhostGesture.AllIn: return "All-in";
                case GhostGesture.Flip: return "Flip";
                case GhostGesture.Talk: return "Talk";
                case GhostGesture.Raise: return "Raise";
                case GhostGesture.Gaze: return "Read";
                default: return g.ToString();
            }
        }

        /// <summary>Resources/Ghosts 裡的檔名（不含副檔名）；Talk 沒有片段 → null。名稱要和 GhostBake.Specs 一致。</summary>
        public static string ClipName(GhostGesture g)
        {
            switch (g)
            {
                case GhostGesture.Peek: return "peek";
                case GhostGesture.RollPeek: return "rollpeek";
                case GhostGesture.LookAtCards: return "look";
                case GhostGesture.PickChips: return "chips";
                case GhostGesture.Bet:
                case GhostGesture.Raise: return "bet";
                case GhostGesture.Knock: return "knock";
                case GhostGesture.Fold: return "fold";
                case GhostGesture.AllIn: return "allin";
                case GhostGesture.Flip: return "flip";
                default: return null;
            }
        }

        /// <summary>讀片段（第一次讀完就快取）。沒有片段或讀不到 → null。</summary>
        public static HandRecording Load(GhostGesture g)
        {
            if (Cache.TryGetValue(g, out var rec)) return rec;
            var name = ClipName(g);
            if (name != null)
            {
                var asset = Resources.Load<TextAsset>(ResourceFolder + "/" + name);
                if (asset != null)
                {
                    try
                    {
                        using (var ms = new MemoryStream(asset.bytes)) rec = HandRecording.Read(ms);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("[Ghost] cannot read " + name + ": " + e.Message);
                    }
                    Resources.UnloadAsset(asset);
                }
                else Debug.LogWarning("[Ghost] missing Resources/" + ResourceFolder + "/" + name);
            }
            Cache[g] = rec;
            return rec;
        }
    }
}
