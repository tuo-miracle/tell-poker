using System;
using System.IO;
using Poker.Gestures;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 頭盔內的手部錄製工具（只用手）：牌桌左前角三個食指按鈕 —— 「◀」「▶」切換標籤、「REC/STOP」開始或結束錄製。
    /// 錄製時每幀把 IHandsFrameSource 的 HandsFrame 寫進
    /// Application.persistentDataPath/recordings/&lt;標籤&gt;_&lt;時間&gt;.hands，桌面上有紅點與秒數。
    /// </summary>
    public sealed class RecorderPanel : MonoBehaviour
    {
        public static readonly string[] DefaultLabels =
            { "peek", "pick_bet", "knock", "fold_push", "allin", "flip", "idle" };

        [SerializeField] TableFrame table;
        [Tooltip("實作 IHandsFrameSource 的元件")]
        [SerializeField] MonoBehaviour handsSource;
        [SerializeField] string[] labels = DefaultLabels;
        [Tooltip("面板中心（牌桌座標，公尺）")]
        [SerializeField] Vector3 panelCenter = new Vector3(-0.32f, 0f, 0.11f);
        [Tooltip("食指指尖的追蹤信心低於這個值就不當作按鈕輸入")]
        [SerializeField] float minConfidence = 0.5f;

        IHandsFrameSource _source;
        RecordingSession _session;
        int _labelIndex;
        float _recordStart;
        string _lastSaved = "";

        Button _prev, _next, _record;
        TextMesh _labelText, _statusText;
        GameObject _recDot;

        string _outputDirectory;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        int _shownTenths = -1, _shownFrames = -1;
        bool _shownRecording;
        string _shownLabel, _shownSaved;

        public static string RecordingsDirectory => Path.Combine(Application.persistentDataPath, "recordings");
        /// <summary>REC/STOP 按鈕中心（牌桌座標）。</summary>
        public Vector3 RecordButtonCenter => panelCenter + new Vector3(0f, 0f, 0.035f);

        /// <summary>程式設定（測試或執行期組裝用），要在 Start 之前呼叫。outputDirectory 為 null 時用 RecordingsDirectory。</summary>
        public void Configure(TableFrame tableFrame, IHandsFrameSource source, string outputDirectory = null)
        {
            table = tableFrame;
            _source = source;
            _outputDirectory = outputDirectory;
        }
        public bool IsRecording => _session != null && _session.IsRecording;
        public string CurrentLabel => labels[_labelIndex];

        sealed class Button
        {
            public FingertipButton Left, Right;
            public Renderer Face;
            public Color Idle;
            public float FlashUntil;
            public Color Shown;
        }

        void Start()
        {
            if (table == null) table = GetComponentInParent<TableFrame>();
            if (_source == null) _source = handsSource as IHandsFrameSource;
            if (_source == null)
            {
                Debug.LogWarning("[RecorderPanel] handsSource does not implement IHandsFrameSource; panel disabled");
                enabled = false;
                return;
            }
            if (labels == null || labels.Length == 0) labels = DefaultLabels;
            _session = new RecordingSession(_outputDirectory ?? RecordingsDirectory);
            _source.FrameUpdated += OnFrame;
            table.Placed += OnTableMoved;
            BuildVisuals();
            _statusText.color = new Color(0.85f, 0.95f, 0.85f);
            _statusText.text = "ready";
            RefreshTexts();
        }

        void OnDestroy()
        {
            if (_source != null) _source.FrameUpdated -= OnFrame;
            if (table != null) table.Placed -= OnTableMoved;
            StopRecording();
        }

        // Quest 拿下頭盔或按 Home 時會 pause；先把檔案關好，避免留下半幀
        void OnApplicationPause(bool paused)
        {
            if (paused) StopRecording();
        }

        // 錄製中牌桌被重新放置（MRUK 晚到、recenter），之後的牌桌座標會跳一段：直接結束這個檔，避免污染回歸資料
        void OnTableMoved(TableFrame frame)
        {
            if (!IsRecording) return;
            StopRecording();
            _lastSaved += "  (stopped: table moved)";
        }

        void OnFrame(HandsFrame frame)
        {
            if (_session != null && _session.IsRecording) _session.Write(frame);
        }

        void Update()
        {
            var frame = _source.Current;
            double now = Time.unscaledTimeAsDouble;
            Vector3? left = Tip(frame.Left), right = Tip(frame.Right);

            if (Pressed(_prev, left, right, now) && !IsRecording) _labelIndex = (_labelIndex + labels.Length - 1) % labels.Length;
            if (Pressed(_next, left, right, now) && !IsRecording) _labelIndex = (_labelIndex + 1) % labels.Length;
            if (Pressed(_record, left, right, now))
            {
                if (IsRecording) StopRecording(); else StartRecording();
            }

            _recDot.SetActive(IsRecording && (Time.unscaledTime * 2f) % 2f < 1.4f);   // 閃爍紅點
            RefreshTexts();
        }

        Vector3? Tip(HandPose hand)
        {
            if (!hand.Tracked || hand.Confidence < minConfidence) return null;
            return hand.Position(HandJoint.IndexTip).ToUnity();
        }

        static bool Pressed(Button b, Vector3? left, Vector3? right, double now)
        {
            bool l = b.Left.Step(left, now);
            bool r = b.Right.Step(right, now);
            bool hover = b.Left.IsHovered || b.Right.IsHovered;
            if (l || r) b.FlashUntil = Time.unscaledTime + 0.25f;
            var c = Time.unscaledTime < b.FlashUntil ? Color.white : hover ? Color.Lerp(b.Idle, Color.white, 0.35f) : b.Idle;
            if (c != b.Shown)
            {
                b.Shown = c;
                b.Face.sharedMaterial.SetColor(BaseColorId, c);   // 每個按鈕有自己的材質，不用 .material（會複製一份）
            }
            return l || r;
        }

        void StartRecording()
        {
            try
            {
                string path = _session.Start(CurrentLabel, DateTime.Now,
                    $"device={SystemInfo.deviceModel};app={Application.version};table={table.Source}");
                _recordStart = Time.unscaledTime;
                Debug.Log("[RecorderPanel] recording " + path);
            }
            catch (Exception e)
            {
                _lastSaved = "ERROR: " + e.Message;
                Debug.LogError("[RecorderPanel] cannot start recording: " + e);
            }
        }

        void StopRecording()
        {
            if (_session == null || !_session.IsRecording) return;
            int frames = _session.FrameCount;
            float seconds = Time.unscaledTime - _recordStart;
            string path = _session.Stop();
            _lastSaved = $"saved {Path.GetFileName(path)}  {frames} f / {seconds:F1} s";
            Debug.Log("[RecorderPanel] " + _lastSaved + " -> " + path);
        }

        /// <summary>只有內容變了才重設文字（錄製中約每 0.1 s 一次），避免每幀配置字串、重建 TextMesh。</summary>
        void RefreshTexts()
        {
            if (_shownLabel != CurrentLabel)
            {
                _shownLabel = CurrentLabel;
                _labelText.text = CurrentLabel;
            }
            bool rec = IsRecording;
            int tenths = rec ? (int)((Time.unscaledTime - _recordStart) * 10f) : -1;
            int frames = rec ? _session.FrameCount / 10 : -1;
            if (rec == _shownRecording && tenths == _shownTenths && frames == _shownFrames && _shownSaved == _lastSaved) return;
            if (rec != _shownRecording) _statusText.color = rec ? new Color(1f, 0.3f, 0.3f) : new Color(0.85f, 0.95f, 0.85f);
            _shownRecording = rec;
            _shownTenths = tenths;
            _shownFrames = frames;
            _shownSaved = _lastSaved;
            _statusText.text = rec
                ? $"REC {tenths / 10f:F1}s  {_session.FrameCount} f"
                : string.IsNullOrEmpty(_lastSaved) ? "ready" : _lastSaved;
        }

        // ---- 外觀：全部是本物件（掛在 TableFrame 底下）的子物件，本地座標 = 牌桌座標 ----
        void BuildVisuals()
        {
            var c = panelCenter;
            _prev = MakeButton("Prev", c + new Vector3(-0.09f, 0, -0.04f), new Vector2(0.025f, 0.022f), new Color(0.2f, 0.3f, 0.45f), "<");
            _next = MakeButton("Next", c + new Vector3(0.09f, 0, -0.04f), new Vector2(0.025f, 0.022f), new Color(0.2f, 0.3f, 0.45f), ">");
            _record = MakeButton("Record", RecordButtonCenter, new Vector2(0.05f, 0.025f), new Color(0.55f, 0.12f, 0.12f), "REC / STOP");
            _labelText = MakeText("Label", c + new Vector3(0f, 0.004f, -0.04f), 0.0045f, Color.white);
            _statusText = MakeText("Status", c + new Vector3(0.02f, 0.004f, 0.09f), 0.0032f, Color.white);

            _recDot = new GameObject("RecDot");
            _recDot.transform.SetParent(transform, false);
            _recDot.transform.localPosition = c + new Vector3(-0.1f, 0.006f, 0.09f);
            _recDot.AddComponent<MeshFilter>().sharedMesh = FlatMeshes.Ring(0.006f, 0.012f, 24);   // 內徑 0 的實心圓
            _recDot.AddComponent<MeshRenderer>().sharedMaterial = RuntimeMaterials.Unlit(new Color(0.95f, 0.1f, 0.1f));
            _recDot.SetActive(false);
        }

        Button MakeButton(string name, Vector3 center, Vector2 half, Color color, string caption)
        {
            var go = new GameObject("Button_" + name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = center + new Vector3(0f, 0.003f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = FlatMeshes.Rect(half * 2f);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = RuntimeMaterials.Unlit(color);
            MakeText(name + "_Caption", center + new Vector3(0f, 0.0045f, 0f), 0.0035f, Color.white).text = caption;
            return new Button
            {
                Left = new FingertipButton(center, half),
                Right = new FingertipButton(center, half),
                Face = r,
                Idle = color,
            };
        }

        TextMesh MakeText(string name, Vector3 pos, float charSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 平躺桌面、字頂朝桌子中央，玩家坐著讀
            var t = go.AddComponent<TextMesh>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = t.font.material;
            t.fontSize = 64;
            t.characterSize = charSize;
            t.anchor = TextAnchor.MiddleCenter;
            t.alignment = TextAlignment.Center;
            t.color = color;
            return t;
        }
    }
}
