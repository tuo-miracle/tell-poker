using System.Collections.Generic;
using Poker.Characters;
using Poker.Core;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>一堆籌碼（一個網格、不投影、沒有剛體）。本地座標＝牌桌座標。</summary>
    public sealed class ChipPile
    {
        public readonly GameObject Go;
        readonly MeshFilter _mf;
        public int Chips { get; private set; } = -1;
        public bool Odd { get; private set; }

        public ChipPile(Transform parent, string name)
        {
            Go = new GameObject(name);
            Go.transform.SetParent(parent, false);
            _mf = Go.AddComponent<MeshFilter>();
            var r = Go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ChipStackMesh.Material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            SetChips(0, false);
        }

        public Vector3 Position { get => Go.transform.localPosition; set => Go.transform.localPosition = value; }

        public void SetAmount(int amount, int denom) =>
            SetChips(Mathf.Max(0, amount) / denom, amount > 0 && amount % denom != 0);

        public void SetChips(int chips, bool odd)
        {
            chips = Mathf.Max(0, chips);
            if (chips == Chips && odd == Odd) return;
            Chips = chips; Odd = odd;
            _mf.sharedMesh = ChipStackMesh.Get(chips, odd);
            Go.SetActive(chips > 0 || odd);
        }
    }

    /// <summary>
    /// 世界中的一行字（TextMesh，深色字）＋後面一塊淺色底板，永遠面向玩家。
    /// URP Unlit 文字材質把字型貼圖的 RGB 當黑色，所以用「深字淺底」（和角色字幕泡泡同一種風格）。
    /// </summary>
    public sealed class WorldLabel
    {
        public readonly GameObject Go;
        public readonly GameObject Plate;
        readonly TextMesh _text;
        readonly float _lineHeight;
        static Mesh _quad;

        /// <summary>
        /// 牌室風格的牌子（2026-11-03，同第 5 輪手腕選單）：黃銅細框、墨綠絨布底、象牙色 Libre Baskerville 粗體字；底板不透明 Unlit。
        /// 一手結束的揭曉（攤牌標題、讀人揭曉卡）用這個。
        /// </summary>
        public static WorldLabel Plaque(Transform parent, string name, float charSize) => new WorldLabel(parent, name, charSize, PlaqueFelt, true);

        static readonly Color PlaqueFelt = new Color(0.06f, 0.24f, 0.15f), PlaqueBrass = new Color(0.78f, 0.61f, 0.30f), PlaqueIvory = new Color(0.97f, 0.93f, 0.82f);
        /// <summary>黃銅框露出的寬度（一行字高的比例）。</summary>
        const float RimOfLine = 0.14f;
        readonly Transform _frame;
        static Font _serif;
        static readonly Dictionary<int, Material> TintedText = new Dictionary<int, Material>();

        static Material IvoryText(Texture fontTexture)
        {
            int key = fontTexture != null ? fontTexture.GetInstanceID() : 0;
            if (TintedText.TryGetValue(key, out var m) && m != null) return m;
            var shader = Shader.Find("Poker/TintedText");
            if (shader == null) return MaterialCache.Text(fontTexture, PlaqueIvory);
            m = new Material(shader) { name = "PlaqueText" };
            m.SetTexture("_BaseMap", fontTexture);
            m.SetColor("_BaseColor", PlaqueIvory);
            TintedText[key] = m;
            return m;
        }

        public WorldLabel(Transform parent, string name, float charSize, Color plateColor) : this(parent, name, charSize, plateColor, false) { }

        WorldLabel(Transform parent, string name, float charSize, Color plateColor, bool plaque)
        {
            Go = new GameObject(name);
            Go.transform.SetParent(parent, false);
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(Go.transform, false);
            _text = textGo.AddComponent<TextMesh>();
            var font = plaque ? (_serif != null ? _serif : (_serif = Resources.Load<Font>("Fonts/LibreBaskerville"))) : null;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.font = font;
            _text.fontSize = 64;
            _text.characterSize = charSize;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            if (plaque) _text.fontStyle = FontStyle.Bold;   // 襯線字的細筆畫在頭盔上會糊
            _lineHeight = charSize * _text.fontSize * 0.1f;
            var r = textGo.GetComponent<MeshRenderer>();
            r.sharedMaterial = plaque ? IvoryText(font.material.mainTexture) : MaterialCache.Text(font.material.mainTexture, new Color(0.1f, 0.08f, 0.08f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            Plate = new GameObject("Plate");
            Plate.transform.SetParent(Go.transform, false);
            Plate.transform.localPosition = new Vector3(0f, 0f, 0.0015f); // 在字的後面（離玩家遠一點）
            Plate.AddComponent<MeshFilter>().sharedMesh = Quad;
            var pr = Plate.AddComponent<MeshRenderer>();
            pr.sharedMaterial = MaterialCache.Unlit(plateColor);
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            if (plaque)
            {
                _frame = new GameObject("Frame").transform;
                _frame.SetParent(Go.transform, false);
                _frame.localPosition = new Vector3(0f, 0f, 0.0025f);   // 在底板後面
                _frame.gameObject.AddComponent<MeshFilter>().sharedMesh = Quad;
                var fr = _frame.gameObject.AddComponent<MeshRenderer>();
                fr.sharedMaterial = MaterialCache.Unlit(PlaqueBrass);
                fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fr.receiveShadows = false;
            }
            Go.SetActive(false);
        }

        public string Text => Go.activeSelf ? _text.text : "";
        /// <summary>一行字的高度（公尺，未乘上 Go 的縮放）。</summary>
        public float LineHeight => _lineHeight;

        public void Show(string text, Vector3 localPos)
        {
            // 文字沒變就不重建網格（拿著籌碼時每幀都會呼叫）
            if (!ReferenceEquals(text, _text.text) && text != _text.text)
            {
                _text.text = text;
                var size = PlateSize(text, _lineHeight);
                // 牌室風格的牌子：量得到字的寬度就照實際寬度包（襯線粗體比內建字型寬）；其他標籤照舊用估的大小
                if (_frame != null && _text.TryGetComponent<MeshRenderer>(out var tr) && tr.localBounds.size.x > 0f)
                    size.x = Mathf.Max(size.x, tr.localBounds.size.x * _text.transform.localScale.x + _lineHeight * 0.9f);
                Plate.transform.localScale = new Vector3(size.x, size.y, 1f);
                if (_frame != null)
                {
                    float rim = _lineHeight * RimOfLine * 2f;
                    _frame.localScale = new Vector3(size.x + rim, size.y + rim, 1f);
                }
            }
            Go.transform.localPosition = localPos;
            Go.SetActive(true);
        }

        public void Hide() => Go.SetActive(false);

        /// <summary>底板大小（公尺，未乘縮放）：多行（\n）時寬度看最長的一行、高度看行數。</summary>
        public static Vector2 PlateSize(string text, float lineHeight)
        {
            int chars = 1, lines = 1, run = 0;
            foreach (char ch in text ?? "")
            {
                if (ch == '\n') { lines++; run = 0; continue; }
                chars = Mathf.Max(chars, ++run);
            }
            return new Vector2(chars * lineHeight * 0.52f + lineHeight * 0.9f, lineHeight * (lines + 0.45f));
        }

        public void Face(Vector3 viewerWorld)
        {
            if (!Go.activeSelf) return;
            var d = Go.transform.position - viewerWorld;
            if (d.sqrMagnitude > 1e-6f) Go.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        /// <summary>平躺在桌上（父節點的水平面），字頭朝遠離 viewer 的方向（坐著低頭就能讀）。</summary>
        public void Lay(Vector3 viewerWorld)
        {
            if (!Go.activeSelf) return;
            var parent = Go.transform.parent;
            var up = parent != null ? parent.up : Vector3.up;
            var d = Vector3.ProjectOnPlane(Go.transform.position - viewerWorld, up);
            if (d.sqrMagnitude > 1e-6f) Go.transform.rotation = Quaternion.LookRotation(-up, d);
        }

        /// <summary>XY 平面、面向 −Z 的單位方塊（從字的正面看得到）。</summary>
        static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "LabelPlate" };
                _quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) });
                _quad.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
                _quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
                _quad.RecalculateBounds();
                return _quad;
            }
        }
    }

    /// <summary>
    /// 牌桌上的遊戲物件：每個座位的籌碼堆與牌、底池、標籤、提示光、教學發光、結算看板、遮擋面。
    /// 全部放在 TableFrame 底下（本地座標＝牌桌座標），牌桌重新放置時跟著走。只負責「怎麼擺、怎麼動」，不懂規則。
    /// </summary>
    public sealed class TableProps
    {
        public const float CardY = 0.0035f;
        public readonly Transform Root;
        public readonly TableSpots Spots;
        public readonly int Denomination;
        public readonly int SeatCount;

        /// <summary>每個座位的籌碼堆與本街下注（依座位）。</summary>
        public readonly ChipPile[] Stack, Bet;
        public readonly ChipPile Pot, Pending;
        /// <summary>兩隻手各自拿著的籌碼（依 Hand 索引）。</summary>
        public readonly ChipPile[] InHand = new ChipPile[2];
        readonly List<ChipPile> _flyers = new List<ChipPile>();

        /// <summary>每個座位的兩張底牌（0 號是玩家）。</summary>
        public readonly CardView[][] SeatCards;
        public readonly CardView[] Board = new CardView[5];
        public CardView[] PlayerCards => SeatCards[0];
        /// <summary>第一個 AI 的牌（一對一相容）。</summary>
        public CardView[] AiCards => SeatCards[1];

        /// <summary>每個 AI 座位亮牌時的標題（一行白話，例如 "Bluffing"；2026-11-03 拿掉第二行的補充）。</summary>
        public readonly WorldLabel[] SeatHeadline;
        public WorldLabel Headline => SeatHeadline[1];
        public readonly WorldLabel[] HandCount = new WorldLabel[2];
        readonly GameObject _glowBet, _glowStack;
        readonly Material _glowMat;
        float _glowT = -1f;
        readonly GameObject _occluder;

        // 教學發光（持續脈動，直到換下一步）與小圖示
        readonly Material _tutMat;
        readonly Dictionary<TutorialGlow, GameObject> _tut = new Dictionary<TutorialGlow, GameObject>();
        float _tutT;
        public TutorialGlow TutorialGlowShown { get; private set; } = TutorialGlow.None;

        // 結算看板
        readonly WorldLabel _sumTitle;
        readonly WorldLabel[] _sumLines = new WorldLabel[2];   // 標題＋兩行（2026-11-04 拿掉 Tells read）

        public TableProps(Transform tableRoot, TableSpots spots, int denomination)
        {
            Spots = spots;
            Denomination = denomination;
            SeatCount = spots.SeatCount;
            var root = new GameObject("GameProps");
            root.transform.SetParent(tableRoot, false);
            Root = root.transform;

            Stack = new ChipPile[SeatCount];
            Bet = new ChipPile[SeatCount];
            SeatCards = new CardView[SeatCount][];
            SeatHeadline = new WorldLabel[SeatCount];
            for (int s = 0; s < SeatCount; s++)
            {
                Stack[s] = new ChipPile(Root, "Stack" + s);
                Bet[s] = new ChipPile(Root, "Bet" + s);
                SeatCards[s] = new CardView[2];
                for (int i = 0; i < 2; i++)
                    SeatCards[s][i] = CardView.Create(Root, default, s == 0 ? "PlayerCard" + i : "Seat" + s + "Card" + i);
                // 牌室風格的牌子（同手腕選單，2026-11-03）：荷官兩側、兩端的人離玩家約 1.2–1.3 m，
                // 0.0058 約 1.7°（使用者：1 m 外不用瞇眼；RevealTextTests.RevealMinDegrees 1.6°）
                SeatHeadline[s] = WorldLabel.Plaque(Root, "RevealHeadline" + s, 0.0058f);
            }
            Pot = new ChipPile(Root, "Pot");
            Pot.Position = spots.PotPile;
            InHand[0] = new ChipPile(Root, "InHandLeft");
            InHand[1] = new ChipPile(Root, "InHandRight");
            Pending = new ChipPile(Root, "PendingBet");

            for (int i = 0; i < 5; i++) Board[i] = CardView.Create(Root, default, "Board" + i);
            HideAllCards();

            for (int h = 0; h < 2; h++) HandCount[h] = new WorldLabel(Root, "HandCount" + h, 0.0035f, new Color(1f, 0.88f, 0.4f));

            _glowMat = new Material(MaterialCache.Unlit(new Color(1f, 0.85f, 0.35f))) { name = "HintGlow" };
            MaterialCache.ConfigureTransparent(_glowMat);
            var zone = spots.Layout.BetZone;
            var zoneSize = new Vector2(zone.MaxX - zone.MinX, zone.MaxZ - zone.MinZ);
            var zoneCenter = new Vector3((zone.MinX + zone.MaxX) * 0.5f, 0.0035f, (zone.MinZ + zone.MaxZ) * 0.5f);
            _glowBet = Flat("HintBetZone", FlatMeshes.RectOutline(zoneSize, 0.008f), zoneCenter, _glowMat);
            _glowStack = Flat("HintStack", FlatMeshes.Ring(0.055f, 0.008f, 40), spots.PlayerStack + Vector3.up * 0.0035f, _glowMat);
            _glowBet.SetActive(false);
            _glowStack.SetActive(false);

            _tutMat = new Material(MaterialCache.Unlit(new Color(0.45f, 0.95f, 1f))) { name = "TutorialGlow" };
            MaterialCache.ConfigureTransparent(_tutMat);
            BuildTutorialGlows(spots, zoneSize, zoneCenter);

            var plate = new Color(0.98f, 0.95f, 0.86f);
            _sumTitle = new WorldLabel(Root, "SummaryTitle", 0.006f, new Color(1f, 0.85f, 0.35f));
            for (int i = 0; i < _sumLines.Length; i++) _sumLines[i] = new WorldLabel(Root, "SummaryLine" + i, 0.004f, plate);

            _occluder = BuildOccluder(tableRoot, spots);
        }

        // ---------------- 籌碼 ----------------

        /// <summary>依顯示用的金額擺好靜止的籌碼堆。playerReserved = 玩家手上／待確認的枚數（從籌碼堆扣掉顯示）。</summary>
        public void LayOut(int[] stacks, int[] bets, int pot, int playerReservedChips, int humanSeat)
        {
            for (int s = 0; s < SeatCount; s++)
            {
                int stackAmt = stacks[s] - (s == humanSeat ? playerReservedChips * Denomination : 0);
                Stack[s].SetAmount(stackAmt, Denomination);
                Stack[s].Position = Spots.StackOf(s);
                Bet[s].SetAmount(bets[s], Denomination);
                Bet[s].Position = Spots.BetOf(s);
            }
            Pot.SetAmount(pot, Denomination);
            Pot.Position = Spots.PotPile;
        }

        /// <summary>
        /// 某座位桌上籌碼堆每一柱的底部中心（牌桌座標，System.Numerics），給手勢辨識器判斷「捏在自己的籌碼上」。
        /// 贏來的底池也是併進這一堆（多排幾柱，往旁邊、往後排；ChipStackMesh.ColumnOffset），零頭籌碼另佔一柱。
        /// </summary>
        public void StackFootprint(int seat, List<System.Numerics.Vector3> into)
        {
            into.Clear();
            var pile = Stack[seat];
            if (!pile.Go.activeSelf) return;
            int cols = ChipStackMesh.Columns(pile.Chips) + (pile.Odd ? 1 : 0);
            var basePos = Spots.StackOf(seat);
            for (int c = 0; c < cols; c++)
            {
                var p = basePos + ChipStackMesh.ColumnOffset(c);
                into.Add(new System.Numerics.Vector3(p.x, 0f, p.z));
            }
        }

        public ChipPile Flyer(int amount)
        {
            ChipPile p = null;
            foreach (var f in _flyers) if (!f.Go.activeSelf) { p = f; break; }
            if (p == null) { p = new ChipPile(Root, "Flyer" + _flyers.Count); _flyers.Add(p); }
            p.SetChips(0, false);
            p.SetAmount(amount, Denomination);
            if (amount > 0) p.Go.SetActive(true);
            return p;
        }

        public static void Release(ChipPile p) => p.SetChips(0, false);

        /// <summary>拋物線（arc > 0）或貼桌滑行（arc = 0）的位置。</summary>
        public static Vector3 Path(Vector3 a, Vector3 b, float t, float arc)
        {
            float e = t * t * (3f - 2f * t);
            var p = Vector3.Lerp(a, b, e);
            p.y += arc * 4f * e * (1f - e);
            return p;
        }

        // ---------------- 牌 ----------------

        public void HideAllCards()
        {
            foreach (var seat in SeatCards) foreach (var c in seat) Hide(c);
            foreach (var c in Board) Hide(c);
        }

        static void Hide(CardView c)
        {
            c.SetFaceUp(false);
            c.SetYaw(0f);
            c.gameObject.SetActive(false);
        }

        public Vector3 DeckPos => Spots.Deck + Vector3.up * CardY;

        /// <summary>把牌放在 pos，背面朝上（AI 的牌轉向自己的座位）。</summary>
        public static void Place(CardView c, Card card, Vector3 pos, float yaw)
        {
            c.gameObject.SetActive(true);
            c.SetCard(card);
            c.SetFaceUp(false);
            c.SetYaw(yaw);
            c.SetPeek(0f);
            c.transform.localPosition = pos;
        }

        // ---------------- 標籤 ----------------

        public void HideHeadlines()
        {
            foreach (var l in SeatHeadline) l.Hide();
        }

        /// <summary>所有顯示中的標籤面向玩家。</summary>
        public void FaceLabels(Vector3 viewerWorld)
        {
            foreach (var l in SeatHeadline) l.Face(viewerWorld);
            _sumTitle.Face(viewerWorld);
            foreach (var l in _sumLines) l.Face(viewerWorld);
        }

        // ---------------- 結算看板 ----------------

        public bool SummaryVisible => _sumTitle.Go.activeSelf;
        public string SummaryText
        {
            get
            {
                if (!SummaryVisible) return "";
                var sb = new System.Text.StringBuilder(_sumTitle.Text);
                foreach (var l in _sumLines) if (l.Go.activeSelf) sb.Append('\n').Append(l.Text);
                return sb.ToString();
            }
        }

        /// <summary>結算看板上看得到的每一行（標題在前）的位置（桌面座標），測試排版用。</summary>
        public List<Vector3> SummaryRows
        {
            get
            {
                var r = new List<Vector3>();
                if (!SummaryVisible) return r;
                r.Add(_sumTitle.Go.transform.localPosition);
                foreach (var l in _sumLines) if (l.Go.activeSelf) r.Add(l.Go.transform.localPosition);
                return r;
            }
        }

        /// <summary>桌子中央立起一塊小看板：標題＋兩行短結算（亮牌標題先收起來，免得疊在一起）。</summary>
        public void ShowSummary(string title, IList<string> lines)
        {
            HideHeadlines();
            // 字高 = charSize × 6.4，底板高 = 字高 × 1.45：標題約 5.6 cm、每行約 3.7 cm
            var at = new Vector3(0f, 0.2f, Spots.Layout.FeltSize.Y * 0.45f);
            _sumTitle.Show(title, at);
            for (int i = 0; i < _sumLines.Length; i++)
            {
                if (lines != null && i < lines.Count) _sumLines[i].Show(lines[i], at + new Vector3(0f, -0.05f - 0.046f * i, 0f));   // 行距 > 底板高（約 3.7 cm），從座位看出去也不疊
                else _sumLines[i].Hide();
            }
        }

        public void HideSummary()
        {
            _sumTitle.Hide();
            foreach (var l in _sumLines) l.Hide();
        }

        // ---------------- 提示、教學發光、震動 ----------------

        /// <summary>輪到玩家：下注區與籌碼堆短暫發光（約 2.5 秒，柔和的脈動）。</summary>
        public void Hint(bool show)
        {
            _glowT = show ? 0f : -1f;
            _glowBet.SetActive(show);
            _glowStack.SetActive(show);
        }

        public bool HintVisible => _glowBet.activeSelf;

        /// <summary>教學：只亮這一步要用的地方（None = 全關）。</summary>
        public void ShowTutorialGlow(TutorialGlow g)
        {
            TutorialGlowShown = g;
            foreach (var kv in _tut) kv.Value.SetActive(kv.Key == g);
            _tutT = 0f;
        }

        void BuildTutorialGlows(TableSpots spots, Vector2 zoneSize, Vector3 zoneCenter)
        {
            const float lift = TutorialLift;
            // 咪牌：兩張牌外框＋牌後方一個朝上的箭頭
            var cards = new GameObject("TutorialCards");
            cards.transform.SetParent(Root, false);
            var cc = (spots.PlayerCard(0) + spots.PlayerCard(1)) * 0.5f;
            Child(cards, Flat("Outline", FlatMeshes.RectOutline(new Vector2(0.17f, 0.12f), 0.007f), cc + Vector3.up * lift, _tutMat));
            var up = Flat("IconUp", Arrow(0.05f, 0.03f), cc + new Vector3(0f, 0.02f, 0.075f), _tutMat);
            up.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            Child(cards, up);
            _tut[TutorialGlow.Cards] = cards;

            // 下注：籌碼堆一圈＋下注區外框＋從籌碼指向下注區的箭頭
            var bet = new GameObject("TutorialBet");
            bet.transform.SetParent(Root, false);
            Child(bet, Flat("Stack", FlatMeshes.Ring(0.055f, 0.007f, 40), spots.PlayerStack + Vector3.up * lift, _tutMat));
            Child(bet, Flat("Zone", FlatMeshes.RectOutline(zoneSize, 0.007f), new Vector3(zoneCenter.x, lift, zoneCenter.z), _tutMat));
            var from = spots.PlayerStack + new Vector3(-0.03f, 0f, 0.07f);
            var dir = new Vector3(0.0f, 0f, spots.Layout.BetLineZ + 0.06f) - from; dir.y = 0f;
            var arrow = Flat("IconArrow", Arrow(0.08f, 0.03f), from + dir.normalized * 0.04f + Vector3.up * lift, _tutMat);
            arrow.transform.localRotation = Quaternion.LookRotation(dir.normalized);
            Child(bet, arrow);
            _tut[TutorialGlow.ChipsAndBetZone] = bet;

            // 過牌：敲桌點兩圈漣漪
            var knock = new GameObject("TutorialKnock");
            knock.transform.SetParent(Root, false);
            Child(knock, Flat("Ring0", FlatMeshes.Ring(0.03f, 0.006f, 32), spots.KnockSpot + Vector3.up * lift, _tutMat));
            Child(knock, Flat("Ring1", FlatMeshes.Ring(0.055f, 0.005f, 40), spots.KnockSpot + Vector3.up * lift, _tutMat));
            _tut[TutorialGlow.KnockSpot] = knock;

            // 棄牌：棄牌線發光＋往前推的箭頭
            var fold = new GameObject("TutorialFold");
            fold.transform.SetParent(Root, false);
            Child(fold, Flat("Line", FlatMeshes.Rect(new Vector2(0.26f, 0.008f)), new Vector3(0f, lift, spots.Layout.FoldLineZ), _tutMat));
            Child(fold, Flat("Outline", FlatMeshes.RectOutline(new Vector2(0.17f, 0.12f), 0.006f), cc + Vector3.up * lift, _tutMat));
            Child(fold, Flat("IconPush", Arrow(0.07f, 0.035f), new Vector3(0f, lift, spots.Layout.FoldLineZ + 0.025f), _tutMat));
            _tut[TutorialGlow.FoldLine] = fold;

            BuildGazeGlow(spots);

            foreach (var g in _tut.Values) g.SetActive(false);
        }

        /// <summary>讀破綻（教學 v3）：Rocco 牌的周圍一圈＋從牌桌中央指向他的箭頭（只有四人桌有 Rocco）。</summary>
        void BuildGazeGlow(TableSpots spots)
        {
            const float lift = TutorialLift;
            if (spots.SeatCount <= TutorialScript.ReadSeat) return;
            var gaze = new GameObject("TutorialGaze");
            gaze.transform.SetParent(Root, false);
            var target = (spots.CardOf(TutorialScript.ReadSeat, 0) + spots.CardOf(TutorialScript.ReadSeat, 1)) * 0.5f;
            target.y = 0f;
            Child(gaze, Flat("Ring", FlatMeshes.Ring(0.09f, 0.008f, 40), target + Vector3.up * lift, _tutMat));
            var start = new Vector3(0f, 0f, spots.Layout.BetLineZ + 0.10f);
            var toward = target - start; toward.y = 0f;
            var point = Flat("IconLook", Arrow(0.10f, 0.04f), start + toward * 0.45f + Vector3.up * lift, _tutMat);
            point.transform.localRotation = Quaternion.LookRotation(toward.normalized);
            Child(gaze, point);
            _tut[TutorialGlow.GazeTarget] = gaze;
        }

        const float TutorialLift = 0.004f;

        /// <summary>座位換了（GameController.Seating）：跟著 AI 座位的提示重新擺（目前只有教學「看 Rocco」的光圈）。</summary>
        public void RefreshSeatGlows()
        {
            if (!_tut.TryGetValue(TutorialGlow.GazeTarget, out var old)) return;
            bool on = old.activeSelf;
            _tut.Remove(TutorialGlow.GazeTarget);
            old.transform.SetParent(null, false);   // Destroy 到幀尾才生效：先拿掉，免得同一幀找到舊的
            Object.Destroy(old);
            BuildGazeGlow(Spots);
            if (_tut.TryGetValue(TutorialGlow.GazeTarget, out var fresh)) fresh.SetActive(on);
        }

        static void Child(GameObject parent, GameObject child) => child.transform.SetParent(parent.transform, true);

        /// <summary>平放、指向 +Z 的箭頭（桿＋三角形頭）。</summary>
        static Mesh Arrow(float length, float width)
        {
            float head = length * 0.45f, shaft = width * 0.38f;
            float z0 = -length * 0.5f, zh = z0 + length - head, z1 = length * 0.5f;
            var m = new Mesh { name = "TutorialArrow" };
            m.SetVertices(new[]
            {
                new Vector3(-shaft * 0.5f, 0, z0), new Vector3(-shaft * 0.5f, 0, zh), new Vector3(shaft * 0.5f, 0, zh), new Vector3(shaft * 0.5f, 0, z0),
                new Vector3(-width * 0.5f, 0, zh), new Vector3(0, 0, z1), new Vector3(width * 0.5f, 0, zh),
            });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6 }, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public void Tick(float dt)
        {
            if (_glowT >= 0f)
            {
                _glowT += dt;
                float a = Mathf.Clamp01(_glowT / 0.3f) * Mathf.Clamp01((2.6f - _glowT) / 0.6f);
                float pulse = 0.55f + 0.45f * Mathf.Sin(_glowT * 6f);
                var c = new Color(1f, 0.85f, 0.35f, a * pulse * 0.8f);
                _glowMat.SetColor("_BaseColor", c);
                if (_glowT > 2.6f) Hint(false);
            }
            if (TutorialGlowShown != TutorialGlow.None)
            {
                _tutT += dt;
                float a = Mathf.Clamp01(_tutT / 0.3f) * (0.6f + 0.4f * Mathf.Sin(_tutT * 5f));
                _tutMat.SetColor("_BaseColor", new Color(0.45f, 0.95f, 1f, 0.35f + 0.6f * a));
            }
        }

        // ---------------- 遮擋面 ----------------

        /// <summary>只在真實桌面時啟用（虛擬桌本身就有桌面擋住）。</summary>
        public void SetOccluderActive(bool on)
        {
            if (_occluder != null) _occluder.SetActive(on);
        }

        public bool OccluderActive => _occluder != null && _occluder.activeSelf;

        static GameObject BuildOccluder(Transform tableRoot, TableSpots spots)
        {
            var shader = Shader.Find("Poker/DepthOnly");
            if (shader == null) { Debug.LogWarning("[Game] 找不到 Poker/DepthOnly shader，不建立遮擋面"); return null; }
            // 和虛擬桌的外框一樣大的長圓形（絨布＋一圈扶手）：桌面以下的身體藏起來，兩端半圓外的角不會切到坐在那裡的人
            float rail = TableDimensions.VirtualBorder;
            var size = new Vector2(spots.Layout.FeltSize.X + 2f * rail, spots.Layout.FeltSize.Y + 2f * rail);
            var go = new GameObject("TableOccluder");
            go.transform.SetParent(tableRoot, false);
            go.transform.localPosition = new Vector3(0f, 0f, spots.Layout.FeltSize.Y * 0.5f);
            go.AddComponent<MeshFilter>().sharedMesh = FlatMeshes.Stadium(size);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(shader) { name = "TableOccluder" };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        GameObject Flat(string name, Mesh mesh, Vector3 pos, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }
    }
}
