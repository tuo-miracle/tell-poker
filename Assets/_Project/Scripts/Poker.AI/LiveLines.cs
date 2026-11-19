using System.Collections.Generic;

namespace Poker.AI
{
    /// <summary>
    /// 即時牌桌對話的觸發（live talk，2026-10-14）：玩家還在做決定時看得到的事。
    /// ReadRight／ReadFooled 是一手結束時「我剛剛在讀你」的收尾（不是玩家回合中的訊號）。
    /// </summary>
    public enum LiveCue { TurnStart, Think3, Think7, Think12, ReLook, Reach, PullBack, ChipsHeld, PutBack, TowardCards, Gaze, PlayerSpoke, ReadRight, ReadFooled }

    /// <summary>
    /// 即時對話的台詞（原創、短、PG、英文；每一句都預錄）。Rocco＝嗆、挑釁；Pip＝緊張、話多；Vex＝少、準、冷；
    /// Silas＝少、乾冷幽默的老手（2026-11-02，只有文字，音檔之後統一 bake）；Dot＝只有中性的提醒。
    /// 和 LineBook 分開放（LineBook 是一般的反應台詞，每個角色 15–25 句）。
    /// </summary>
    public static class LiveLines
    {
        /// <summary>每句最長字數（實機字幕一眼讀完、聲音不拖）。</summary>
        public const int MaxChars = 45;

        static SpeechLine R(string id, string text) => new SpeechLine("rocco.live." + id, "rocco", text);
        static SpeechLine P(string id, string text) => new SpeechLine("pip.live." + id, "pip", text);
        static SpeechLine V(string id, string text) => new SpeechLine("vex.live." + id, "vex", text);
        static SpeechLine S(string id, string text) => new SpeechLine("silas.live." + id, "silas", text);
        static SpeechLine D(string id, string text) => new SpeechLine("dot.live." + id, "dot", text);

        static readonly Dictionary<(string who, LiveCue cue), SpeechLine[]> Book = new Dictionary<(string, LiveCue), SpeechLine[]>();

        static readonly List<SpeechLine> Ordered = new List<SpeechLine>();

        static void Add(string who, LiveCue c, params SpeechLine[] lines) { Book[(who, c)] = lines; Ordered.AddRange(lines); }

        static LiveLines()
        {
            // Rocco：嗆、挑釁（Clock's ticking 和 LineBook 的長考台詞同一句、同一個音檔）
            Add("rocco", LiveCue.Think3, R("think3.1", "Something wrong, champ?"), R("think3.2", "Tough one, huh?"), R("think3.3", "Need a minute, champ?"));
            Add("rocco", LiveCue.Think7, R("think7.1", "Clock's ticking, champ."), R("think7.2", "Still thinking? That's a tell."), R("think7.3", "Somebody's scared."));
            Add("rocco", LiveCue.Think12, R("think12.1", "Any day now, champ."), R("think12.2", "We're all getting older here."));
            Add("rocco", LiveCue.ReLook, R("relook.1", "Again? They didn't change."), R("relook.2", "Not loving those cards, huh?"), R("relook.3", "Look all you want. Same cards."));
            Add("rocco", LiveCue.Reach, R("reach.1", "Ooh, going in?"), R("reach.2", "There it is. Reach for it."), R("reach.3", "Go on, grab some chips."));
            Add("rocco", LiveCue.PullBack, R("pullback.1", "Changed your mind? I saw that."), R("pullback.2", "Ooh, cold feet?"), R("pullback.3", "Hand goes in, hand comes back."));
            Add("rocco", LiveCue.ChipsHeld, R("held.1", "Gonna hold those all day?"), R("held.2", "Drop them in, champ."), R("held.3", "Heavy, huh? Put them in."));
            Add("rocco", LiveCue.PutBack, R("putback.1", "Putting them back? Scared."), R("putback.2", "Back in the stack, huh?"));
            Add("rocco", LiveCue.TowardCards, R("cards.1", "Thinking of folding? Do it."), R("cards.2", "Go on, toss them."), R("cards.3", "Folding already?"));
            Add("rocco", LiveCue.Gaze, R("gaze.1", "See something you like?"), R("gaze.2", "Looking for a tell? Good luck."));
            Add("rocco", LiveCue.ReadRight, R("right.1", "Read you like a book."), R("right.2", "Called it. You were weak."));
            Add("rocco", LiveCue.ReadFooled, R("fooled.1", "You faked that? Nice."), R("fooled.2", "Okay. That was a trick."));
            // Pip：緊張、話多、自己說溜嘴
            Add("pip", LiveCue.Think3, P("think3.1", "Oh, um, take your time."), P("think3.2", "Is it hard? It looks hard."));
            Add("pip", LiveCue.Think7, P("think7.1", "I'd be thinking too."), P("think7.2", "Long think. Is that bad for me?"));
            Add("pip", LiveCue.Think12, P("think12.1", "Should I get snacks?"), P("think12.2", "I'm so nervous right now."));
            Add("pip", LiveCue.ReLook, P("relook.1", "Did they change? Mine never do."), P("relook.2", "You checked again! Is that good?"));
            Add("pip", LiveCue.Reach, P("reach.1", "Oh no, here it comes."), P("reach.2", "Wait, you're betting?"));
            Add("pip", LiveCue.PullBack, P("pullback.1", "Oh, thank goodness."), P("pullback.2", "Phew. Wait, you're not?"));
            Add("pip", LiveCue.ChipsHeld, P("held.1", "H-how much are you thinking?"), P("held.2", "That's a lot of chips. Right?"));
            Add("pip", LiveCue.PutBack, P("putback.1", "You put them back. Phew."), P("putback.2", "Not betting? Okay. Okay good."));
            Add("pip", LiveCue.TowardCards, P("cards.1", "Are you folding? I won't tell."), P("cards.2", "Oh, are you out?"));
            Add("pip", LiveCue.Gaze, P("gaze.1", "Why are you looking at me?"), P("gaze.2", "I didn't do anything!"));
            Add("pip", LiveCue.ReadRight, P("right.1", "I, um, kind of saw that coming."));
            Add("pip", LiveCue.ReadFooled, P("fooled.1", "You tricked me!"));
            // Vex：少、準、冷
            Add("vex", LiveCue.Think3, V("think3.1", "Hm."));
            Add("vex", LiveCue.Think7, V("think7.1", "Doubt is expensive."), V("think7.2", "Seven seconds."));
            Add("vex", LiveCue.Think12, V("think12.1", "Twelve seconds. Noted."), V("think12.2", "Decide."));
            Add("vex", LiveCue.ReLook, V("relook.1", "Checking again? Hm."), V("relook.2", "Twice now."));
            Add("vex", LiveCue.Reach, V("reach.1", "Go on."), V("reach.2", "Hm. Interested."));
            Add("vex", LiveCue.PullBack, V("pullback.1", "Hesitation. Noted."), V("pullback.2", "You flinched."));
            Add("vex", LiveCue.ChipsHeld, V("held.1", "Heavy decision."), V("held.2", "Count them twice."));
            Add("vex", LiveCue.PutBack, V("putback.1", "Back in the stack. Hm."), V("putback.2", "Interesting."));
            Add("vex", LiveCue.TowardCards, V("cards.1", "Your cards won't save you."), V("cards.2", "Fold, then."));
            Add("vex", LiveCue.Gaze, V("gaze.1", "Find anything?"), V("gaze.2", "Keep looking."));
            Add("vex", LiveCue.ReadRight, V("right.1", "Readable."));
            Add("vex", LiveCue.ReadFooled, V("fooled.1", "Clever. Once."));
            // Silas：少、乾、看過太多牌局
            Add("silas", LiveCue.Think3, S("think3.1", "No rush."));
            Add("silas", LiveCue.Think7, S("think7.1", "I've got all night."), S("think7.2", "Still deciding? Fine."));
            Add("silas", LiveCue.Think12, S("think12.1", "My coffee's getting cold."), S("think12.2", "I've aged a little here."));
            Add("silas", LiveCue.ReLook, S("relook.1", "Still the same two cards."), S("relook.2", "Second look. I saw."));
            Add("silas", LiveCue.Reach, S("reach.1", "Go on, then."), S("reach.2", "There's the reach."));
            Add("silas", LiveCue.PullBack, S("pullback.1", "Hand came back. Hm."), S("pullback.2", "Second thoughts."));
            Add("silas", LiveCue.ChipsHeld, S("held.1", "They don't get lighter."), S("held.2", "Weighing it, huh?"));
            Add("silas", LiveCue.PutBack, S("putback.1", "Back in the stack."), S("putback.2", "Changed your mind."));
            Add("silas", LiveCue.TowardCards, S("cards.1", "Folding is a choice too."), S("cards.2", "Cards looking tired?"));
            Add("silas", LiveCue.Gaze, S("gaze.1", "Seen enough, kid?"), S("gaze.2", "Look all you like."));
            Add("silas", LiveCue.ReadRight, S("right.1", "Old tricks still work."));
            Add("silas", LiveCue.ReadFooled, S("fooled.1", "Hm. You got me."));
            // Dot：只有很久沒動時的中性提醒
            Add("dot", LiveCue.Think12, D("think12.1", "Take your time."), D("think12.2", "Action is still on you."), D("think12.3", "No rush. Your call."));
        }

        /// <summary>某個聲音（pip／rocco／vex／silas／dot）在某個觸發的台詞（沒有就是空的）。</summary>
        public static IReadOnlyList<SpeechLine> For(string who, LiveCue c) =>
            Book.TryGetValue((who, c), out var l) ? l : (IReadOnlyList<SpeechLine>)System.Array.Empty<SpeechLine>();

        /// <summary>全部台詞（匯出給 bake）。</summary>
        public static IEnumerable<SpeechLine> All() => Ordered;

        public static string VoiceOf(Archetype a) => a == Archetype.Rookie ? "pip" : a == Archetype.Loudmouth ? "rocco" : a == Archetype.Veteran ? "silas" : "vex";
    }
}
