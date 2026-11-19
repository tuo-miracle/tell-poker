using Poker.Gestures;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 「雙手平放在桌上」校準：桌上顯示兩個手印，玩家把雙手平放在真實桌面上 1 秒，牌桌就貼到手掌下緣的高度，
    /// 朝向也轉成雙手面對的方向（兩掌連線為左右軸），玩家側桌緣對齊雙手。
    /// 不需要房間掃描、不怕桌上有雜物，評審在陌生房間也能用（spec §5、比賽要求在沒測過的房間能運作）。
    /// 校準後的牌桌來源是 HandCalibrated，TablePlacer 不會再用 MRUK 或射線覆蓋它。
    /// 換位置校準（2026-10-15）：桌子鎖定（校準過／錨點放回）時不顯示提示，但雙手平放在明顯不同的地方 2 s（TableRelocateDetector）
    /// 就用同樣的算法把桌子搬過去（之後照常存成新錨點），並觸發 Relocated（UX 播確認音）。
    /// </summary>
    public sealed class HandSurfaceCalibration : MonoBehaviour
    {
        /// <summary>雙手中心到玩家側桌緣的距離＝手牌中心的前後位置：雙手掌就在手牌兩側（手印放在這裡）。</summary>
        public static readonly float HandsToEdge = new TableLayout().CardsCenter.Z;
        /// <summary>手印中心離中線的距離：手印（寬 9 cm）內緣離牌邊約 1 cm。</summary>
        const float PrintSpacing = 0.125f;
        /// <summary>重新校準按鈕：牌桌右前角（錄製面板在左前角，兩者對稱）。</summary>
        static readonly Vector3 ButtonCenter = new Vector3(0.38f, 0f, 0.06f);
        static readonly Vector2 ButtonHalf = new Vector2(0.03f, 0.025f);

        [SerializeField] TableFrame table;
        [Tooltip("IHandsFrameSource（場景中的 HandsSourceSelector）")]
        [SerializeField] MonoBehaviour handsSource;

        TableFrame _table;
        IHandsFrameSource _hands;
        readonly SurfaceCalibrator _calibrator = new SurfaceCalibrator();
        readonly TableRelocateDetector _relocate = new TableRelocateDetector(TableDimensions.Layout) { Log = m => Debug.Log(m) };
        Transform _prompt;
        bool _awaiting = true;
        readonly FingertipButton _leftButton = new FingertipButton(ButtonCenter, ButtonHalf);
        readonly FingertipButton _rightButton = new FingertipButton(ButtonCenter, ButtonHalf);
        Renderer _buttonRenderer;
        Material _buttonMaterial;
        Renderer[] _printRenderers;
        Material _printMaterial;
        TextMesh _label;
        /// <summary>提示字的位置（牌桌座標）：兩個手印中間、牌的前方（靠玩家），平躺在桌上。</summary>
        static readonly Vector3 LabelCenter = new Vector3(0f, 0.004f, 0.06f);

        /// <summary>換位置校準成功（雙手中心離原本手牌區的距離 m、新的 yaw 度）。</summary>
        public event System.Action<float, float> Relocated;

        public bool PromptVisible => _prompt != null && _prompt.gameObject.activeSelf;
        public bool IsCalibrated => _table != null && _table.Source == TablePlacementSource.HandCalibrated && !_awaiting;
        /// <summary>重新校準按鈕的中心（牌桌座標）。</summary>
        public Vector3 RecalibrateButtonCenter => ButtonCenter;

        public void Init(TableFrame table, IHandsFrameSource hands)
        {
            if (_hands != null) _hands.FrameUpdated -= OnFrame;
            if (_table != null) _table.Placed -= OnPlaced;
            _table = table;
            _hands = hands;
            _hands.FrameUpdated += OnFrame;
            _table.Placed += OnPlaced;
            BuildPrompt();
            BuildButton();
            UpdateLabel();
        }

        /// <summary>
        /// 牌桌從空間錨點放回、而且當初是親手校準的：同一張桌子，不用再請玩家按一次（重新校準按鈕照樣能用）。
        /// 其他來源（MRUK、射線、虛擬桌）照舊顯示手印。
        /// </summary>
        void OnPlaced(TableFrame t)
        {
            _relocate.Reset();   // 牌桌座標換了
            if (t.Source == TablePlacementSource.Restored && t.RestoredFrom == TablePlacementSource.HandCalibrated && _awaiting)
            {
                _awaiting = false;
                _calibrator.Reset();
                if (_prompt != null) _prompt.gameObject.SetActive(false);
                Debug.Log("[HandSurfaceCalibration] restored calibrated table: prompt hidden");
            }
            // 虛擬桌（沒找到真的桌子）一定要請玩家用雙手對齊
            if (t.Source == TablePlacementSource.Virtual && !_awaiting) Recalibrate();
            UpdateLabel();
        }

        /// <summary>鎖定的桌子（校準過或錨點放回）不會自動重放，靠換位置校準搬。</summary>
        bool Locked => _table != null && (_table.Source == TablePlacementSource.HandCalibrated || _table.Source == TablePlacementSource.Restored);

        /// <summary>手印中間的一行小字：有真的桌面就請玩家平放在桌上；虛擬桌（房間裡沒找到桌子）就說手放的高度就是桌子的高度。</summary>
        void UpdateLabel()
        {
            if (_label == null || _table == null) return;
            bool virtualTable = _table.Source == TablePlacementSource.Virtual || _table.Source == TablePlacementSource.None;
            _label.text = virtualTable ? "Hold both palms flat here\nto set the table height" : "Rest both palms flat\non your desk here";
        }

        /// <summary>重新校準（例如換了桌子）。</summary>
        public void Recalibrate()
        {
            _awaiting = true;
            _calibrator.Reset();
            if (_prompt != null) _prompt.gameObject.SetActive(true);
        }

        void Start()
        {
            if (_hands == null && table != null && handsSource is IHandsFrameSource src) Init(table, src);
        }

        void OnDestroy()
        {
            if (_hands != null) _hands.FrameUpdated -= OnFrame;
            if (_table != null) _table.Placed -= OnPlaced;
        }

        void OnFrame(HandsFrame frame)
        {
            if (_table == null) return;
            double time = frame.Time;
            bool pressed = _leftButton.Step(Tip(frame.Left), time) | _rightButton.Step(Tip(frame.Right), time);
            if (_buttonMaterial != null) SetColor(_buttonMaterial, _leftButton.IsHovered || _rightButton.IsHovered ? new Color(1f, 0.85f, 0.3f) : new Color(0.85f, 0.85f, 0.9f));
            if (pressed)
            {
                Recalibrate();
                Debug.Log("[HandSurfaceCalibration] recalibrate requested");
                return;
            }
            var head = HeadInTable();
            if (_awaiting)
            {
                var result = _calibrator.Update(frame);
                UpdatePromptGlow(_calibrator.Progress);
                if (result.HasValue)
                {
                    PlaceAt(result.Value, head);
                    _awaiting = false;
                    _prompt.gameObject.SetActive(false);
                    return;
                }
            }
            if (!Locked) { _relocate.Reset(); return; }
            var moved = _relocate.Step(frame, head);
            if (!moved.HasValue) return;
            var r = moved.Value;
            PlaceAt(r.Calibration, head);
            _awaiting = false;
            _calibrator.Reset();
            if (_prompt != null) _prompt.gameObject.SetActive(false);
            Debug.Log($"[Table] recalibrated at new spot (dist={r.Distance:F2}m yaw={r.YawDegrees:F0}°)");
            Relocated?.Invoke(r.Distance, r.YawDegrees);
        }

        /// <summary>玩家的頭（沒設就用 Camera.main，實機是 CenterEyeAnchor）。</summary>
        public Transform Head { get; set; }

        System.Numerics.Vector3? HeadInTable()
        {
            var head = Head;
            if (head == null) { var cam = Camera.main; if (cam != null) head = cam.transform; }
            if (head == null) return null;
            var h = _table.WorldToTable(head.position);
            return new System.Numerics.Vector3(h.x, h.y, h.z);
        }

        /// <summary>把牌桌放到校準結果：高度＝手掌下緣、朝向跟著雙手（推算不可靠時保留原朝向）、雙手在新手牌區兩側。</summary>
        void PlaceAt(SurfaceCalibration c, System.Numerics.Vector3? head)
        {
            // 牌桌朝向也跟著雙手：右方＝左掌→右掌，前方遠離玩家；推算不可靠時保留原朝向，只改高度
            var yaw = SurfaceCalibrator.EstimateYaw(c, head);
            var forwardTable = yaw.Apply ? Quaternion.Euler(0f, yaw.YawDegrees, 0f) * Vector3.forward : Vector3.forward;
            var center = new Vector3(c.HandsCenter.X, c.SurfaceY, c.HandsCenter.Z);
            var originTable = center - forwardTable * HandsToEdge;
            var rotation = _table.transform.rotation;
            if (yaw.Apply)
            {
                var forwardWorld = Vector3.ProjectOnPlane(_table.TableToWorldDirection(forwardTable), Vector3.up);
                if (forwardWorld.sqrMagnitude > 1e-6f) rotation = Quaternion.LookRotation(forwardWorld, Vector3.up);   // 只轉 yaw，保持水平
            }
            Debug.Log($"[HandSurfaceCalibration] yaw {(yaw.Apply ? yaw.YawDegrees : 0f):F1} {(yaw.Apply ? "applied" : "kept")} ({yaw.Reason})");
            var world = new Pose(_table.TableToWorld(originTable), rotation);
            _table.Place(world, TablePlacementSource.HandCalibrated);
            Debug.Log($"[HandSurfaceCalibration] table surface at y={world.position.y:F3} (moved {c.SurfaceY * 100f:F1} cm)");
        }

        static Vector3? Tip(HandPose h)
        {
            if (!h.Tracked) return null;
            var p = h.Position(HandJoint.IndexTip);
            return new Vector3(p.X, p.Y, p.Z);
        }

        static void SetColor(Material m, Color c)
        {
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        /// <summary>右前角的重新校準按鈕：一個淺色圓鈕，中間一圈深色的環（「重新來過」的意思），手指懸停時變黃。</summary>
        void BuildButton()
        {
            if (_buttonRenderer != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _buttonMaterial = new Material(shader);
            var dark = new Material(shader);
            SetColor(dark, new Color(0.15f, 0.15f, 0.2f));
            var button = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(button.GetComponent<Collider>());
            button.name = "RecalibrateButton";
            button.transform.SetParent(transform, false);
            button.transform.localPosition = ButtonCenter + Vector3.up * 0.003f;
            button.transform.localScale = new Vector3(ButtonHalf.x * 2f, 0.002f, ButtonHalf.x * 2f);
            _buttonRenderer = button.GetComponent<Renderer>();
            _buttonRenderer.sharedMaterial = _buttonMaterial;
            _buttonRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "Ring";
            ring.transform.SetParent(button.transform, false);
            ring.transform.localPosition = Vector3.up * 0.6f;
            ring.transform.localScale = new Vector3(0.55f, 0.2f, 0.55f);
            var rr = ring.GetComponent<Renderer>();
            rr.sharedMaterial = dark;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetColor(_buttonMaterial, new Color(0.85f, 0.85f, 0.9f));
        }

        void BuildPrompt()
        {
            if (_prompt != null) return;
            _prompt = new GameObject("HandPrints").transform;
            _prompt.SetParent(transform, false);
            _printMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            _printRenderers = new Renderer[2];
            for (int i = 0; i < 2; i++)
            {
                var print = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(print.GetComponent<Collider>());
                print.name = i == 0 ? "LeftPrint" : "RightPrint";
                print.transform.SetParent(_prompt, false);
                print.transform.localPosition = new Vector3(i == 0 ? -PrintSpacing : PrintSpacing, 0.004f, HandsToEdge);
                print.transform.localScale = new Vector3(0.09f, 0.001f, 0.11f);
                var r = print.GetComponent<Renderer>();
                r.sharedMaterial = _printMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _printRenderers[i] = r;
            }
            BuildLabel();
            UpdatePromptGlow(0f);
        }

        void BuildLabel()
        {
            var go = new GameObject("PromptLabel");
            go.transform.SetParent(_prompt, false);
            go.transform.localPosition = LabelCenter;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 平躺、字頭朝牌桌中央，坐著低頭讀得到
            _label = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.font = font;
            _label.fontSize = 48;
            _label.characterSize = 0.0028f;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = new Color(1f, 1f, 1f, 0.9f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = font.material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void UpdatePromptGlow(float progress)
        {
            if (_printMaterial == null) return;
            float pulse = 0.55f + 0.25f * Mathf.Sin(Time.time * 4f);
            var c = Color.Lerp(new Color(1f, 1f, 1f, 1f) * pulse, new Color(0.35f, 1f, 0.55f), progress);
            _printMaterial.color = c;
            if (_printMaterial.HasProperty("_BaseColor")) _printMaterial.SetColor("_BaseColor", c);
        }

        void Update()
        {
            if (PromptVisible && _calibrator.Progress <= 0f) UpdatePromptGlow(0f);
        }
    }
}
