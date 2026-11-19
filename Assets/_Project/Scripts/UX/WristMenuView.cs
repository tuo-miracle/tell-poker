using System.Collections.Generic;
using Poker.Characters;
using Poker.Game;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;
using Hand = Poker.Gestures.Hand;

namespace Poker.UX
{
    /// <summary>
    /// 手腕選單的畫面與點按：掌心朝上時浮在手掌上方的選單按鈕（主要，2026-10-14）、手腕上的按鈕（次要）＋玩家正前方的面板（FoV 內）。
    /// 面板本地座標：x 右、y 上、玩家在 -Z 側；點按用 FingertipButton，把「離面板的距離」當高度。
    /// </summary>
    public sealed class WristMenuView : MonoBehaviour
    {
        // 版面（2026-10-15 選單瘦身）：項目少了，按鈕排成格子、盡量放大（每頁最多 4 列 × 3 欄，按鈕至少 4 × 3 cm），
        // 字 0.0026（在 0.45 m 約 2.1°；放不下先折兩行，再縮字，測試要求 ≥ 1.2°）
        const float FullWidth = 0.256f, ColGap = 0.008f, RowGap = 0.006f, MaxRowHeight = 0.05f;
        const float TitleY = 0.088f, TitleHeight = 0.026f, GridTop = TitleY - TitleHeight * 0.5f - 0.007f, GridBottom = -0.102f;
        const float TextSize = 0.0026f, TitleSize = 0.003f, ConsentTextSize = 0.0021f * 0.75f, ConsentButtonHeight = 0.03f;
        // 外觀（2026-11-03 使用者：「選單也幫我美化」）：私人高額牌室——黃銅細框、深色皮革底、墨綠絨布面、黃銅標題牌、象牙色按鈕、襯線字。
        // 字一律深色印在淺色牌子上（MaterialCache.Text 用字型貼圖當遮罩，淺色字會變黑；與牌桌標籤 WorldLabel 一致）。
        // 全部是不透明的 Unlit：不加燈、不加陰影、不疊透明層（72 fps）。
        static readonly Color FrameColor = new Color(0.78f, 0.61f, 0.30f);       // 黃銅
        static readonly Color PanelColor = new Color(0.17f, 0.10f, 0.07f);       // 深色皮革
        static readonly Color FeltColor = new Color(0.06f, 0.24f, 0.15f);        // 墨綠絨布
        static readonly Color TitlePlateColor = new Color(0.86f, 0.71f, 0.42f);  // 黃銅標題牌
        static readonly Color ButtonColor = new Color(0.94f, 0.90f, 0.80f);      // 象牙色
        static readonly Color HoverColor = new Color(1f, 0.80f, 0.38f);          // 暖金：手指停在上面
        static readonly Color PressedColor = new Color(0.82f, 0.50f, 0.20f);     // 琥珀：按下的那一下
        static readonly Color SelectedColor = new Color(0.62f, 0.86f, 0.70f);    // 正在示範的手勢（淺綠，和絨布同一個色系）
        static readonly Color InkColor = new Color(0.12f, 0.07f, 0.04f);         // 深咖啡色墨水
        static readonly Color WristColor = new Color(0.85f, 0.68f, 0.35f);
        static readonly Color PalmColor = new Color(0.85f, 0.68f, 0.35f);
        static readonly Color PalmFlashColor = new Color(1f, 0.95f, 0.85f);
        /// <summary>黃銅框露出的寬度、絨布離皮革邊的距離（公尺）。</summary>
        const float FrameRim = 0.004f, FeltInset = 0.012f;
        /// <summary>按下後按鈕維持琥珀色的秒數。</summary>
        const float PressedSeconds = 0.25f;
        /// <summary>按鈕字在選單距離（MenuPlacement.Distance）至少多高（度）：比字幕的 1.2° 再大一點，坐著也清楚好按。</summary>
        public const float MinLabelDegrees = 1.3f;
        /// <summary>襯線字（Libre Baskerville，SIL Open Font License，Resources/Fonts）；找不到就用內建字型。</summary>
        const string SerifFont = "Fonts/LibreBaskerville";
        /// <summary>掌上按鈕的字（第一次另附一行提示，用過一次就不再出現）。</summary>
        public const string PalmLabel = "Menu", PalmHint = "Menu\n<size=36>Touch with your other hand</size>";
        const float PalmFlashSeconds = 0.2f;

        public enum ButtonState { Rest, Hover, Pressed, Selected }

        public static Color StateColor(ButtonState s) =>
            s == ButtonState.Hover ? HoverColor : s == ButtonState.Pressed ? PressedColor : s == ButtonState.Selected ? SelectedColor : ButtonColor;

        /// <summary>按鈕現在的底色（測試用；不在這一頁就丟例外）。</summary>
        public Color ButtonColorOf(MenuItem item)
        {
            foreach (var b in _buttons) if (b.Item == item) return b.Plate.sharedMaterial.color;
            throw new KeyNotFoundException(item + " is not on the current page");
        }

        /// <summary>這個按鈕剛被按下：PressedSeconds 內是琥珀色。</summary>
        public void NotePressed(MenuItem item)
        {
            foreach (var b in _buttons)
                if (b.Item == item)
                {
                    b.PressedUntil = Time.unscaledTime + PressedSeconds;
                    Setup(b.Plate, MaterialCache.Unlit(PlateColor(b, false)));
                }
        }

        sealed class Btn
        {
            public MenuItem Item;
            public Vector2 Center, Half;
            public FingertipButton Left, Right;
            public Renderer Plate;
            public TextMesh Label;
            public float PressedUntil = float.NegativeInfinity;
        }

        MenuModel _menu;
        Transform _rows;
        TextMesh _title;
        readonly List<Btn> _buttons = new List<Btn>();
        MenuPage? _builtPage;
        /// <summary>建這一頁時的項目清單（MenuModel 回傳固定的陣列：換了一組項目就重建，例如教學結束 Skip tutorial 消失）。</summary>
        IReadOnlyList<MenuItem> _builtItems;
        Transform _wristDot;
        Renderer _wristRenderer;
        Transform _palmRoot, _palmDisk, _palmFill;
        Renderer _palmRenderer;
        TextMesh _palmText;
        float _palmFlashUntil = float.NegativeInfinity;
        static Mesh _quad, _sphere, _cylinder;
        static Font _font;

        /// <summary>首次開啟語音對話的同意說明（英文；比賽要求英文介面）。</summary>
        public const string VoiceConsentText =
            "Voice chat sends what you say to Google Gemini\n" +
            "(speech understanding and replies) and to\n" +
            "ElevenLabs (character voices). Audio is sent live\n" +
            "and not stored by Tell. Only public table info is\n" +
            "shared, never your cards. You can turn it off\n" +
            "anytime in Settings.";

        public Transform Panel { get; private set; }
        /// <summary>面板標題（手勢頁選了手勢時是那個手勢的字幕）。</summary>
        public string Title => _title != null ? _title.text : "";
        public bool Visible => Panel != null && Panel.gameObject.activeSelf;

        public void Init(MenuModel menu)
        {
            _menu = menu;
            if (_quad == null) _quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (_sphere == null) _sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            if (_cylinder == null) _cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            if (_font == null) _font = Resources.Load<Font>(SerifFont);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Panel = new GameObject("WristMenuPanel").transform;
            Panel.SetParent(transform, false);
            // 由後往前：黃銅框（露出一圈細邊）→ 皮革 → 絨布
            Plate(Panel, "Frame", Vector2.zero, MenuPlacement.PanelSize + Vector2.one * (2f * FrameRim), FrameColor, 0.003f);
            Plate(Panel, "Bg", Vector2.zero, MenuPlacement.PanelSize, PanelColor, 0.002f);
            Plate(Panel, "Felt", Vector2.zero, MenuPlacement.PanelSize - Vector2.one * (2f * FeltInset), FeltColor, 0.0015f);
            Plate(Panel, "TitlePlate", new Vector2(0f, TitleY), new Vector2(FullWidth, TitleHeight), TitlePlateColor, 0.001f);
            _title = Text(Panel, "Title", new Vector2(0f, TitleY), TitleSize);
            _rows = new GameObject("Rows").transform;
            _rows.SetParent(Panel, false);
            Panel.gameObject.SetActive(false);

            _wristDot = new GameObject("WristButton").transform;
            _wristDot.SetParent(transform, false);
            // 扁圓盤（直徑 4 cm），貼著手背轉（2026-10-14 從 2.2 cm 的球放大）
            _wristDot.localScale = new Vector3(WristMenuButton.VisualDiameter, 0.012f, WristMenuButton.VisualDiameter);
            _wristDot.gameObject.AddComponent<MeshFilter>().sharedMesh = _sphere;
            _wristRenderer = _wristDot.gameObject.AddComponent<MeshRenderer>();
            Setup(_wristRenderer, MaterialCache.Unlit(WristColor));
            _wristDot.gameObject.SetActive(false);

            // 掌上按鈕：圓盤（內建 Cylinder：直徑 1、高 2）面向頭，上面有「Menu」字；停留時中間的亮圈隨進度長大
            _palmRoot = new GameObject("PalmMenuButton").transform;
            _palmRoot.SetParent(transform, false);
            _palmDisk = new GameObject("Disk").transform;
            _palmDisk.SetParent(_palmRoot, false);
            _palmDisk.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // 圓柱的軸（+Y）轉成 -Z＝朝向頭
            _palmDisk.localScale = new Vector3(0.04f, 0.001f, 0.04f);
            _palmDisk.gameObject.AddComponent<MeshFilter>().sharedMesh = _cylinder;
            _palmRenderer = _palmDisk.gameObject.AddComponent<MeshRenderer>();
            Setup(_palmRenderer, MaterialCache.Unlit(PalmColor));
            _palmFill = new GameObject("Fill").transform;
            _palmFill.SetParent(_palmRoot, false);
            _palmFill.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            _palmFill.localPosition = new Vector3(0f, 0f, -0.0015f);
            _palmFill.gameObject.AddComponent<MeshFilter>().sharedMesh = _cylinder;
            Setup(_palmFill.gameObject.AddComponent<MeshRenderer>(), MaterialCache.Unlit(HoverColor));
            _palmFill.gameObject.SetActive(false);
            _palmText = Text(_palmRoot, "Label", Vector2.zero, TextSize * 0.9f);
            _palmText.richText = true;
            _palmText.text = PalmLabel;
            _palmText.transform.localPosition = new Vector3(0f, 0f, -0.0025f);
            _palmRoot.gameObject.SetActive(false);
        }

        public bool PalmButtonVisible => _palmRoot != null && _palmRoot.gameObject.activeSelf;
        public string PalmButtonText => _palmText != null ? _palmText.text : "";
        public Vector3 PalmButtonWorld => _palmRoot != null ? _palmRoot.position : Vector3.zero;

        /// <summary>掌上按鈕的位置、朝向（面向頭）、高亮與停留進度；hint＝第一次附提示字。</summary>
        public void UpdatePalmButton(PalmMenu palm, TableFrame table, bool hint)
        {
            bool show = table != null && palm != null && palm.Visible;
            if (_palmRoot.gameObject.activeSelf != show) _palmRoot.gameObject.SetActive(show);
            if (!show) return;
            var c = palm.ButtonCenter;
            var n = table.TableToWorldDirection(new Vector3(palm.ButtonNormal.X, palm.ButtonNormal.Y, palm.ButtonNormal.Z));
            var up = table.TableToWorldDirection(Vector3.up);
            _palmRoot.SetPositionAndRotation(table.TableToWorld(new Vector3(c.X, c.Y, c.Z)),
                Quaternion.LookRotation(-n, Mathf.Abs(Vector3.Dot(n, up)) > 0.98f ? table.TableToWorldDirection(Vector3.forward) : up));   // +Z 背向頭：字從頭的方向看是正的
            bool flash = Time.unscaledTime < _palmFlashUntil;
            _palmRoot.localScale = Vector3.one * (palm.Hovered ? 1.12f : 1f);
            Setup(_palmRenderer, MaterialCache.Unlit(flash ? PalmFlashColor : palm.Hovered ? HoverColor : PalmColor));
            float k = palm.Progress;
            _palmFill.gameObject.SetActive(k > 0f && !flash);
            if (k > 0f) _palmFill.localScale = new Vector3(0.04f * k, 0.0005f, 0.04f * k);
            var text = hint ? PalmHint : PalmLabel;
            if (_palmText.text != text) _palmText.text = text;
        }

        /// <summary>按下時閃一下（視覺回饋；聲音由 UxController 播）。</summary>
        public void FlashPalmButton() => _palmFlashUntil = Time.unscaledTime + PalmFlashSeconds;

        public void ShowAt(Pose pose)
        {
            Panel.SetPositionAndRotation(pose.position, pose.rotation);
            Panel.gameObject.SetActive(true);
            Refresh();
        }

        public void Hide()
        {
            if (Panel != null) Panel.gameObject.SetActive(false);
        }

        /// <summary>依目前分頁重建按鈕（換頁時）或只更新文字（同一頁，保留按鈕狀態以免連按）。</summary>
        public void Refresh()
        {
            if (_menu == null) return;
            // 主頁的項目會隨教學進行與否改變：換了一組項目就重建
            if (_builtPage != _menu.Page || !ReferenceEquals(_builtItems, _menu.Items(_menu.Page))) Build(_menu.Page);
            _title.text = TitleOf(_menu.Page);
            _title.characterSize = TitleSize;
            FitText(_title, FullWidth - 0.01f, TitleHeight - 0.002f);
            foreach (var b in _buttons)
            {
                SetLabel(b);
                Setup(b.Plate, MaterialCache.Unlit(PlateColor(b, false)));
            }
        }

        /// <summary>按鈕上現在的字（測試用；不在這一頁就丟例外）。</summary>
        public string LabelText(MenuItem item)
        {
            foreach (var b in _buttons) if (b.Item == item) return b.Label != null ? b.Label.text : "";
            throw new KeyNotFoundException(item + " is not on the current page");
        }

        public Vector2 ButtonCenterLocal(MenuItem item)
        {
            foreach (var b in _buttons) if (b.Item == item) return b.Center;
            throw new KeyNotFoundException(item + " is not on the current page");
        }

        /// <summary>每幀呼叫（選單開著時）。回傳這一幀被按下的項目。</summary>
        public bool TryPress(HandsFrame frame, TableFrame table, double time, out MenuItem item)
        {
            item = default;
            if (!Visible || table == null) return false;
            var l = Local(frame.Left, table);
            var r = Local(frame.Right, table);
            bool pressed = false;
            foreach (var b in _buttons)
            {
                bool hit = b.Left.Step(l, time) | b.Right.Step(r, time);
                if (hit && !pressed) { pressed = true; item = b.Item; b.PressedUntil = Time.unscaledTime + PressedSeconds; }
                Setup(b.Plate, MaterialCache.Unlit(PlateColor(b, b.Left.IsHovered || b.Right.IsHovered)));
            }
            return pressed;
        }

        /// <summary>手腕按鈕的位置與高亮（牌桌座標 → 世界）。</summary>
        public void UpdateWristButton(HandsFrame frame, Hand hand, TableFrame table, bool hovered, bool allowed = true)
        {
            var h = frame.Get(hand);
            bool show = allowed && table != null && h.Tracked;
            _wristDot.gameObject.SetActive(show);
            if (!show) return;
            var c = WristMenuButton.ButtonCenter(h);
            _wristDot.position = table.TableToWorld(new Vector3(c.X, c.Y, c.Z));
            var q = h.Rotation(HandJoint.Wrist);
            _wristDot.rotation = table.TableToWorld(new Quaternion(q.X, q.Y, q.Z, q.W));   // 圓盤貼著手背（+Y＝手背）
            Setup(_wristRenderer, MaterialCache.Unlit(hovered ? HoverColor : WristColor));
        }

        Vector3? Local(HandPose h, TableFrame table)
        {
            if (!h.Tracked) return null;
            var p = h.Position(HandJoint.IndexTip);
            var local = Panel.InverseTransformPoint(table.TableToWorld(new Vector3(p.X, p.Y, p.Z)));
            // FingertipButton：x、z 是按鈕平面，y 是高度（離面板往玩家側的距離）
            return new Vector3(local.x, -local.z, local.y);
        }

        void Build(MenuPage page)
        {
            for (int i = _rows.childCount - 1; i >= 0; i--) Destroy(_rows.GetChild(i).gameObject);
            _buttons.Clear();
            _builtPage = page;
            _builtItems = _menu.Items(page);
            if (page == MenuPage.VoiceConsent)
            {
                var body = Text(_rows, "ConsentText", new Vector2(0f, 0.012f), ConsentTextSize);
                body.text = VoiceConsentText;
                FitText(body, FullWidth, 0.11f);
                float bw = (FullWidth - 0.012f) * 0.5f;
                float by = GridBottom + ConsentButtonHeight * 0.5f + 0.002f;
                Button(MenuItem.VoiceAccept, new Vector2(-FullWidth * 0.5f + bw * 0.5f, by), new Vector2(bw, ConsentButtonHeight));
                Button(MenuItem.VoiceDecline, new Vector2(FullWidth * 0.5f - bw * 0.5f, by), new Vector2(bw, ConsentButtonHeight));
                return;
            }
            // How to play 三欄（7 個示範＋Replay tutorial＋Back＝3 × 3）；主頁與 Settings 兩欄（教學中的 Skip tutorial 和 Resume 同一列）
            Grid(_builtItems, page == MenuPage.HowToPlay ? 3 : 2);
        }

        /// <summary>依序排成格子（由左到右、由上到下）；最後一列不滿就置中。列高平分可用高度，最多 MaxRowHeight。</summary>
        void Grid(IReadOnlyList<MenuItem> items, int cols)
        {
            int rows = (items.Count + cols - 1) / cols;
            float rowH = Mathf.Min(MaxRowHeight, (GridTop - GridBottom - (rows - 1) * RowGap) / rows);
            float colW = (FullWidth - (cols - 1) * ColGap) / cols;
            for (int i = 0; i < items.Count; i++)
            {
                int row = i / cols, col = i % cols;
                int inRow = Mathf.Min(cols, items.Count - row * cols);
                float rowWidth = inRow * colW + (inRow - 1) * ColGap;
                float x = -rowWidth * 0.5f + colW * 0.5f + col * (colW + ColGap);
                float y = GridTop - rowH * 0.5f - row * (rowH + RowGap);
                Button(items[i], new Vector2(x, y), new Vector2(colW, rowH));
            }
        }

        void Button(MenuItem item, Vector2 center, Vector2 size)
        {
            var half = size * 0.5f;
            var b = new Btn
            {
                Item = item, Center = center, Half = half,
                Left = MakeTouch(center, half), Right = MakeTouch(center, half),
                Plate = Plate(_rows, item.ToString(), center, size, ButtonColor, 0f).GetComponent<Renderer>(),
                Label = Text(_rows, item + "Label", center, TextSize),
            };
            _buttons.Add(b);
            SetLabel(b);
        }

        /// <summary>按鈕的字：稍微太寬（縮到 85% 以內放得下）就縮字；更寬就在「: 」後面（沒有就在最中間的空白）折成兩行，還是放不下才再縮。</summary>
        void SetLabel(Btn b)
        {
            var text = LabelOf(b.Item);
            float maxW = b.Half.x * 2f - 0.008f, maxH = b.Half.y * 2f - 0.004f;
            b.Label.characterSize = TextSize;
            b.Label.text = text;
            float w = Width(b.Label);
            if (w > maxW && maxW / w < WrapBelow)
            {
                int cut = text.IndexOf(": ", System.StringComparison.Ordinal);
                if (cut >= 0) cut += 1;
                else
                {
                    for (int d = 0; d < text.Length && cut < 0; d++)
                    {
                        int m = text.Length / 2;
                        if (m + d < text.Length && text[m + d] == ' ') cut = m + d;
                        else if (m - d >= 0 && text[m - d] == ' ') cut = m - d;
                    }
                }
                if (cut >= 0) b.Label.text = text.Substring(0, cut) + "\n" + text.Substring(cut + 1);
            }
            FitText(b.Label, maxW, maxH);
        }

        const float WrapBelow = 0.85f;

        static float Width(TextMesh t) => t.TryGetComponent<MeshRenderer>(out var r) ? r.localBounds.size.x : 0f;

        static FingertipButton MakeTouch(Vector2 center, Vector2 half) => new FingertipButton(new Vector3(center.x, 0f, center.y), half)
        {
            PressHeight = 0.008f, ArmHeight = 0.025f, HoverCeiling = 0.10f, Margin = 0.002f, Cooldown = 0.35,
        };

        string LabelOf(MenuItem item)
        {
            var s = _menu.Settings;
            switch (item)
            {
                case MenuItem.Resume: return "Resume";
                case MenuItem.HowToPlay: return "How to play";
                case MenuItem.MoveTable: return "Move table here";
                case MenuItem.Settings: return "Settings";
                case MenuItem.NewGame: return "New game";
                case MenuItem.Quit: return "Quit";
                case MenuItem.SkipTutorial: return "Skip tutorial";
                case MenuItem.FoolRocco: return "Fool Rocco";
                case MenuItem.GhostFlip: return "Show cards";   // 攤牌時把牌翻開（GhostGesture.Flip）
                case MenuItem.ReplayTutorial: return "Replay tutorial";
                case MenuItem.Back: return "Back";
                case MenuItem.VoiceChat: return "Voice chat: " + OnOff(s.VoiceActive);
                case MenuItem.Hints: return "Hints: " + OnOff(s.Hints);
                case MenuItem.TextSize: return "Text size: " + s.SubtitleScale.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + "x";
                case MenuItem.HighContrast: return "High contrast: " + OnOff(s.HighContrast);
                case MenuItem.LeftHanded: return "Left-handed: " + OnOff(s.LeftHanded);
                case MenuItem.VoiceAccept: return "I agree";
                case MenuItem.VoiceDecline: return "Not now";
                default:
                    var g = MenuModel.GhostOf(item);
                    return g.HasValue ? GhostCatalog.MenuLabel(g.Value) : item.ToString();
            }
        }

        static string OnOff(bool on) => on ? "On" : "Off";

        string TitleOf(MenuPage page)
        {
            switch (page)
            {
                case MenuPage.Main: return "Paused";
                case MenuPage.VoiceConsent: return "Voice chat";
                case MenuPage.HowToPlay: return _menu.Demo.HasValue ? GhostCatalog.Caption(_menu.Demo.Value) : "How to play";
                default: return "Settings";
            }
        }

        Color PlateColor(Btn b, bool hovered)
        {
            if (Time.unscaledTime < b.PressedUntil) return PressedColor;
            if (hovered) return HoverColor;
            var g = MenuModel.GhostOf(b.Item);
            return g.HasValue && g == _menu.Demo ? SelectedColor : ButtonColor;
        }

        static GameObject Plate(Transform parent, string name, Vector2 center, Vector2 size, Color color, float z)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(center.x, center.y, z);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;   // 內建 Quad 從 -Z 看是正面（玩家側）
            Setup(go.AddComponent<MeshRenderer>(), MaterialCache.Unlit(color));
            return go;
        }

        static TextMesh Text(Transform parent, string name, Vector2 center, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(center.x, center.y, -0.002f);
            var t = go.AddComponent<TextMesh>();
            t.font = _font;
            t.fontSize = 64;
            t.characterSize = size;
            t.anchor = TextAnchor.MiddleCenter;
            t.alignment = TextAlignment.Center;
            t.fontStyle = FontStyle.Bold;   // 襯線字的細筆畫在頭盔上會糊，用粗體
            Setup(go.GetComponent<MeshRenderer>(), MaterialCache.Text(_font.material.mainTexture, InkColor));
            return t;
        }

        /// <summary>文字太寬（或太高）就縮小字級塞進框內（量 TextMesh 的實際邊界）。</summary>
        static void FitText(TextMesh t, float maxWidth, float maxHeight)
        {
            if (!t.TryGetComponent<MeshRenderer>(out var r)) return;
            for (int i = 0; i < 3; i++)
            {
                var size = r.localBounds.size;   // 文字自己的座標（面板轉向不影響）
                float w = size.x, h = size.y;
                if (w <= 0f || h <= 0f) return;   // 還沒有網格（例如批次模式），保持原字級
                float k = Mathf.Min(maxWidth / w, maxHeight / h);
                if (k >= 1f) return;
                t.characterSize *= k * 0.98f;
            }
        }

        static void Setup(Renderer r, Material m)
        {
            if (r.sharedMaterial != m) r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
