using System.Collections.Generic;
using Poker.AI;
using UnityEngine;

namespace Poker.Characters
{
    /// <summary>
    /// 整桌一個發言權：所有角色的 SpeechChannel 加入同一個 SpeechFloor，同時只有一個角色在講
    /// （2026-10-13 實機：玩家長考時 Pip、Rocco、Vex 和荷官同一瞬間開口）。規則：
    /// - 別人在講、或別人剛講完不到 Gap 秒：先排隊（log：[Speech] floor: who waits for speaker）。
    /// - 發言權空出來時，所有排隊的話裡最重要的先講（Reply ＞ Info ＞ Chatter），同等級先來先講。
    ///   排太久（SpeechChannel.MaxWait）的丟掉；Filler 不等（log：[Speech] floor: who dropped (floor busy) …）。
    /// - 不打斷別人講到一半，唯一例外是回話（Reply，玩家在等的答案）：切掉別人的閒聊／Filler（log：cuts … (reply)）。
    ///   別人在講 Info／Reply 時，整段音檔的回話排第一個等；串流回話的聲音已經在來、不能等，照播（log：overlaps … (stream reply)）。
    /// - 被 HoldBelow 擋住的話（玩家正在講話時的閒聊）不擋別人。
    /// 同一個角色自己的排隊與打斷規則照舊（SpeechChannel）。
    /// </summary>
    public sealed class SpeechFloor
    {
        /// <summary>換人講話前的自然停頓（秒）。</summary>
        public const float Gap = 0.3f;

        readonly List<SpeechChannel> _channels = new List<SpeechChannel>();
        long _seq;

        public IReadOnlyList<SpeechChannel> Channels => _channels;

        /// <summary>正在講的角色（沒有就是 null；串流重疊時是第一個）。</summary>
        public SpeechChannel Speaker
        {
            get
            {
                foreach (var c in _channels) if (c != null && c.IsSpeaking) return c;
                return null;
            }
        }

        public void Join(SpeechChannel ch)
        {
            if (ch == null || _channels.Contains(ch)) return;
            ch.Floor?.Leave(ch);
            _channels.Add(ch);
            ch.Floor = this;
        }

        public void Leave(SpeechChannel ch)
        {
            if (ch == null) return;
            _channels.Remove(ch);
            if (ch.Floor == this) ch.Floor = null;
        }

        internal long NextSeq() => ++_seq;

        /// <summary>
        /// ch 現在可以開始講 u 嗎？不行時 blocker 是擋住它的人（正在講的、剛講完的、或排在它前面的）。
        /// 回話要切掉別人的閒聊時在這裡切（cut = true）。
        /// </summary>
        internal bool MayStart(SpeechChannel ch, Utterance u, out SpeechChannel blocker)
        {
            blocker = null;
            bool reply = u.Priority == SpeechPriority.Reply;
            // 1. 別人正在講
            foreach (var o in _channels)
            {
                if (o == ch || o == null || !o.IsSpeaking) continue;
                if (reply && o.Current.Priority < SpeechPriority.Info) continue;   // 回話可以切掉閒聊（下面切）
                if (reply && u.IsStream) continue;                                  // 串流不能等（下面記重疊）
                blocker = o;
                return false;
            }
            bool cutting = false;
            if (reply)
            {
                foreach (var o in _channels.ToArray())
                {
                    if (o == ch || o == null || !o.IsSpeaking) continue;
                    if (o.Current.Priority < SpeechPriority.Info)
                    {
                        Debug.Log($"[Speech] floor: {ch.Who} cuts {o.Who} (reply)");
                        o.CutForFloor(u);
                        cutting = true;
                    }
                    else Debug.Log($"[Speech] floor: {ch.Who} overlaps {o.Who} (stream reply)");
                }
            }
            if (cutting || u.IsStream) return true;
            // 2. 別人剛講完：停一下再換人
            foreach (var o in _channels)
                if (o != ch && o != null && o.SinceEnd < Gap) { blocker = o; return false; }
            // 3. 別人排著更重要（或同等級但先來）的話
            foreach (var o in _channels)
            {
                if (o == ch || o == null) continue;
                var w = o.NextEligible();
                if (w != null && Ahead(w, u)) { blocker = o; return false; }
            }
            return true;
        }

        static bool Ahead(Utterance a, Utterance b) => a.Priority > b.Priority || (a.Priority == b.Priority && a.Seq < b.Seq);
    }
}
