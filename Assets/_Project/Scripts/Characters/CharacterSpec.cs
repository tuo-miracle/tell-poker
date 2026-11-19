using UnityEngine;

namespace Poker.Characters
{
    /// <summary>三個 AI 對手加一個荷官。</summary>
    public enum CharacterKind { Rookie, Loudmouth, Cold, Dealer, Veteran }

    /// <summary>被玩家盯著時的本能反應。</summary>
    public enum GazeResponse { LookAway, StareBack, Smile }

    /// <summary>角色的外型與個性參數。外型給 CharacterBuilder，個性給 CharacterPerformer。</summary>
    public sealed class CharacterSpec
    {
        public CharacterKind Kind;
        public string DisplayName;

        // 外型
        public Color Skin, Shirt, Accent, Hair, Brow, Blush, Lips;
        public float BodyWidth = 1f, BodyHeight = 1f;
        public Vector3 HeadRadii = new Vector3(0.16f, 0.145f, 0.145f);
        public float EyeSize = 1f, EyeSpacing = 0.062f, EyeHeight = 0.0f;
        public float BrowThickness = 1f;
        public float MouthWidth = 1f, MouthHeight = -0.075f;
        public Color CardBack;

        // 個性
        /// <summary>整體演出幅度倍率（新手誇張、冷面收斂）。</summary>
        public float Expressiveness = 1f;
        /// <summary>動作速度（頭與身體的追隨速率）。</summary>
        public float Snappiness = 1f;
        /// <summary>待機小動作頻率倍率。</summary>
        public float IdleBusy = 1f;
        public float BaseLid;
        public float BaseBlush;
        public float BaseLean;
        public float BlinkInterval = 3.5f;
        public GazeResponse OnGazed;
        public bool HandsBehindHead;
        public MouthShape RestMouth = MouthShape.Smile;

        public static CharacterSpec For(CharacterKind k)
        {
            switch (k)
            {
                case CharacterKind.Rookie:
                    return new CharacterSpec
                    {
                        Kind = k, DisplayName = "Pip",
                        Skin = Hex("FFD3B0"), Shirt = Hex("49C6A8"), Accent = Hex("FFC23D"), Hair = Hex("D9772E"),
                        Brow = Hex("9C4A1C"), Blush = Hex("FF8C8C"), Lips = Hex("6B2B2B"), CardBack = Hex("3A7BD5"),
                        BodyWidth = 0.88f, BodyHeight = 0.92f, HeadRadii = new Vector3(0.158f, 0.15f, 0.148f),
                        EyeSize = 1.18f, EyeSpacing = 0.064f, EyeHeight = -0.008f, BrowThickness = 0.85f, MouthWidth = 0.8f,
                        Expressiveness = 1.3f, Snappiness = 1.5f, IdleBusy = 1.5f, BaseBlush = 0.45f,
                        BlinkInterval = 2.2f, OnGazed = GazeResponse.LookAway, RestMouth = MouthShape.SmallSmile,
                    };
                case CharacterKind.Loudmouth:
                    return new CharacterSpec
                    {
                        Kind = k, DisplayName = "Rocco",
                        Skin = Hex("C98A5B"), Shirt = Hex("FF7A2F"), Accent = Hex("FFD23F"), Hair = Hex("2B2024"),
                        Brow = Hex("2B2024"), Blush = Hex("E0605A"), Lips = Hex("4A1C1C"), CardBack = Hex("D63A3A"),
                        BodyWidth = 1.22f, BodyHeight = 1.0f, HeadRadii = new Vector3(0.17f, 0.148f, 0.15f),
                        EyeSize = 0.9f, EyeSpacing = 0.066f, EyeHeight = 0.012f, BrowThickness = 1.6f, MouthWidth = 1.35f, MouthHeight = -0.09f,
                        Expressiveness = 1.2f, Snappiness = 1.2f, IdleBusy = 1.3f, BaseLean = -3f, BaseBlush = 0.1f,
                        BlinkInterval = 3.2f, OnGazed = GazeResponse.Smile, HandsBehindHead = true, RestMouth = MouthShape.Smirk,
                    };
                case CharacterKind.Cold:
                    return new CharacterSpec
                    {
                        Kind = k, DisplayName = "Vex",
                        Skin = Hex("EEE6F2"), Shirt = Hex("3A2A55"), Accent = Hex("C9CCD8"), Hair = Hex("1C1A2A"),
                        Brow = Hex("1C1A2A"), Blush = Hex("C9A0D8"), Lips = Hex("5A2A5E"), CardBack = Hex("2B2B2B"),
                        BodyWidth = 0.95f, BodyHeight = 1.04f, HeadRadii = new Vector3(0.152f, 0.152f, 0.142f),
                        EyeSize = 0.95f, EyeSpacing = 0.06f, EyeHeight = 0.004f, BrowThickness = 0.8f, MouthWidth = 0.75f,
                        Expressiveness = 0.6f, Snappiness = 0.6f, IdleBusy = 0.45f, BaseLid = 0.38f, BaseBlush = 0f,
                        BlinkInterval = 5.5f, OnGazed = GazeResponse.StareBack, RestMouth = MouthShape.Flat,
                    };
                case CharacterKind.Veteran:
                    // 老手 Silas（2026-11-02 第 4 位對手）：只在寫實預覽開這個座位，卡通身體會被寫實模型蓋掉；
                    // 這裡給一個素色的身體（灰西裝、白髮），角色骨架、注視判定、字幕位置照常用
                    return new CharacterSpec
                    {
                        Kind = k, DisplayName = "Silas",
                        Skin = Hex("D9A988"), Shirt = Hex("4A4F57"), Accent = Hex("B8B2A6"), Hair = Hex("D8D6D2"),
                        Brow = Hex("A8A49E"), Blush = Hex("D89080"), Lips = Hex("5A2A2A"), CardBack = Hex("6A4A2A"),
                        BodyWidth = 1.05f, BodyHeight = 1.0f, HeadRadii = new Vector3(0.155f, 0.15f, 0.145f),
                        EyeSize = 0.85f, EyeSpacing = 0.062f, EyeHeight = 0.004f, BrowThickness = 1.1f, MouthWidth = 0.85f,
                        Expressiveness = 0.55f, Snappiness = 0.7f, IdleBusy = 0.5f, BaseLid = 0.3f, BaseBlush = 0f,
                        BlinkInterval = 4.5f, OnGazed = GazeResponse.StareBack, RestMouth = MouthShape.Flat,
                    };
                default:
                    return new CharacterSpec
                    {
                        Kind = CharacterKind.Dealer, DisplayName = "Dot",
                        Skin = Hex("EDB48A"), Shirt = Hex("FAF7F0"), Accent = Hex("8E1F3A"), Hair = Hex("6B3A22"),
                        Brow = Hex("5A2E1A"), Blush = Hex("FF9A8A"), Lips = Hex("6B2B2B"), CardBack = Hex("1F7A4D"),
                        BodyWidth = 1f, BodyHeight = 1f, HeadRadii = new Vector3(0.158f, 0.146f, 0.146f),
                        EyeSize = 1.0f, EyeSpacing = 0.062f, BrowThickness = 1f, MouthWidth = 1f,
                        Expressiveness = 0.8f, Snappiness = 1f, IdleBusy = 0.8f, BaseBlush = 0.35f,
                        BlinkInterval = 3.5f, OnGazed = GazeResponse.Smile, RestMouth = MouthShape.Smile,
                    };
            }
        }

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }
    }
}
