using Poker.AI;
using Poker.Gestures;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 及時提示（2026-10-14；教學 v4 只教核心玩法，其他手勢在正式牌局第一次用得到時教，見 JustInTimeTips）：
    /// 第一次輪到你而且可以過牌 → 「Nobody bet. Knock twice to check.」＋敲桌示範（取代「輪到你」）；
    /// 第一次輪到你而且牌力提示是 Strong → All-in 的提示＋雙手推示範；第一次攤牌 → 「Toss your cards forward to show.」＋翻牌示範（這一次等 FirstShowdownFlipTimeout 秒）；
    /// 第一次盯著某個 AI 滿 1 秒 → 破綻會放大（語音對話開著時不提：那時盯著 AI 多半是要跟他講話）；語音對話能用時第一次盯著 Dot → 怎麼問她＋講話示範。正在對角色講話時都不插嘴。
    /// 每種每次開機一次；教學那一手、Fool Rocco、自動玩家、選單開著、設定的 Hints 關掉、或那個手勢已經做會了（GestureHints.SuppressAfter 次）不教。
    /// 實機 log：[Tips] &lt;名稱&gt; "&lt;台詞&gt;"、[Playtest] … event=tip name=&lt;名稱&gt;。
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>及時提示這次開機教過哪些。</summary>
        public JustInTimeTips Tips { get; } = new JustInTimeTips();
        /// <summary>第一次攤牌（有教亮牌）時，等玩家自己翻的秒數（平常是 ShowdownFlipTimeout）。</summary>
        public const float FirstShowdownFlipTimeout = 12f;
        /// <summary>及時提示的示範手播多久（玩家一動作就收）。</summary>
        public const float TipGhostSeconds = 6f;

        GhostGesture? _jitGhost;
        double _jitGhostUntil;

        bool TipsAllowed
        {
            get
            {
                Tips.Enabled = HintsEnabled;
                return HintsEnabled && AutoPlayer == null && !MenuOpen && !InTutorialHand && !InFoolRoccoHand && !(Tutorial != null && Tutorial.IsActive);
            }
        }

        bool Knows(GhostGesture g) => Progress != null && Progress.Successes(g) >= GestureHints.SuppressAfter;

        /// <summary>輪到玩家（一般牌局）：要教就教（回傳 true＝已經說了，不用再說「輪到你」）。過牌優先於 All-in。</summary>
        bool TipAtPlayerTurn()
        {
            if (!TipsAllowed) return false;
            var la = Session.Legal(Human);
            if (la.CanCheck && !Knows(GhostGesture.Knock) && !Tips.Used(JitTip.Check)) return Use(JitTip.Check);
            if (la.CanAllIn && HandHintShown == HandHint.Strong && !Knows(GhostGesture.AllIn) && !Tips.Used(JitTip.AllIn)) return Use(JitTip.AllIn);
            return false;
        }

        /// <summary>攤牌等玩家翻牌（排進演出佇列之前問）：第一次就教。</summary>
        bool ShowdownTipDue() => TipsAllowed && !Knows(GhostGesture.Flip) && Tips.TryUse(JitTip.Showdown);

        /// <summary>開始盯著某個角色（滿 1 秒）：第一次盯 AI → 破綻放大的提示；語音能用、第一次盯 Dot → 怎麼問她。</summary>
        void TipOnGaze(int id)
        {
            if (!TipsAllowed || VoiceBusy) return;   // 正在對角色講話：不插嘴
            if (!(Flow == GameFlow.PlayerTurn || Flow == GameFlow.AiThinking || Flow == GameFlow.Presenting)) return;
            bool voice = TutorialVoice != null && TutorialVoice.Available;
            if (id == DealerGazeId)
            {
                if (voice && !Tips.Used(JitTip.Voice)) Use(JitTip.Voice);
                return;
            }
            // 語音對話開著時，盯著 AI 多半是要跟他講話（盯 0.8 秒就收音）：Dot 不要在這時候插一句
            if (!voice && Character(id) != null && !Tips.Used(JitTip.Gaze)) Use(JitTip.Gaze);
        }

        bool Use(JitTip t)
        {
            if (!Tips.TryUse(t)) return false;
            SayTip(t);
            return true;
        }

        /// <summary>Dot 說這個提示（有聲音＋同一句字幕），示範手馬上播（盯人的提示不用：玩家已經在盯了）。</summary>
        void SayTip(JitTip t)
        {
            string line = JustInTimeTips.Line(t);
            Debug.Log($"[Tips] {t} \"{line}\"");
            Playtest.Event("tip", "name=" + t.ToString().ToLowerInvariant());
            Voice.Say(line, Now, "tip", force: true);
            if (t == JitTip.Gaze) return;
            _jitGhost = JustInTimeTips.GhostOf(t);
            _jitGhostUntil = Now + TipGhostSeconds;
            Ghost?.Hide();   // 從頭播
        }
    }
}
