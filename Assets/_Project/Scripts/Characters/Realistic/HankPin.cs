using Poker.AI;

namespace Poker.Characters
{
    public enum HankLook { None, Player, Left, Right, Table }

    /// <summary>
    /// 開發用：把寫實角色固定在某個表情／眼神（實機截圖用，adb shell setprop debug.tell.hankface "talk,happy,player"；0 或空＝不固定）。
    /// 只有預覽版的 RealisticPreview 會讀這個屬性。Left／Right 是玩家視角的左右。
    /// </summary>
    public struct HankPin
    {
        public bool Active, Blink, Talk;
        public Mood? Mood;
        public HankLook Look;

        public static HankPin Parse(string value)
        {
            var p = new HankPin();
            if (string.IsNullOrWhiteSpace(value)) return p;
            foreach (var raw in value.Split(','))
            {
                var t = raw.Trim().ToLowerInvariant();
                switch (t)
                {
                    case "blink": p.Blink = true; break;
                    case "talk": p.Talk = true; break;
                    case "player": p.Look = HankLook.Player; break;
                    case "left": p.Look = HankLook.Left; break;
                    case "right": p.Look = HankLook.Right; break;
                    case "table": p.Look = HankLook.Table; break;
                    case "neutral":
                        p.Mood = AI.Mood.Neutral;
                        if (p.Look == HankLook.None) p.Look = HankLook.Player;
                        break;
                    default:
                        if (t.Length > 0 && char.IsLetter(t[0]) && System.Enum.TryParse<Mood>(t, true, out var m) && System.Enum.IsDefined(typeof(Mood), m)) p.Mood = m;
                        else continue;
                        break;
                }
                p.Active = true;
            }
            return p;
        }
    }
}
