using System.Text;
using Poker.Gestures;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 頭盔內除錯看板：每隻手的追蹤狀態、信心、捏合、手腕離桌高度、掌心法線（牌桌座標）。立在牌桌遠端，面向玩家。
    /// </summary>
    public sealed class HandDebugReadout : MonoBehaviour
    {
        [SerializeField] MonoBehaviour handsSource;
        [SerializeField] TablePlacer placer;
        [SerializeField] Vector3 position = new Vector3(0f, 0.16f, TableDimensions.Depth + 0.02f);
        [SerializeField] float refreshInterval = 0.1f;

        IHandsFrameSource _source;
        TextMesh _text;
        float _next;
        readonly StringBuilder _sb = new StringBuilder();

        void Start()
        {
            _source = handsSource as IHandsFrameSource;
            var go = new GameObject("DebugText");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            _text = go.AddComponent<TextMesh>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = _text.font.material;
            _text.fontSize = 64;
            _text.characterSize = 0.0028f;
            _text.anchor = TextAnchor.LowerCenter;
            _text.alignment = TextAlignment.Left;
            _text.color = new Color(0.9f, 1f, 0.9f);
        }

        void Update()
        {
            if (_source == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + refreshInterval;
            var f = _source.Current;
            _sb.Clear();
            var frame = GetComponentInParent<TableFrame>();
            _sb.Append("table: ").Append(frame != null ? frame.Source.ToString() : "?");
            if (placer != null) _sb.Append("  mruk: ").Append(placer.LastSceneStatus);
            _sb.Append('\n');
            Line("L", f.Left);
            Line("R", f.Right);
            _text.text = _sb.ToString();
        }

        void Line(string name, HandPose h)
        {
            if (!h.Tracked)
            {
                _sb.Append(name).Append(": --\n");
                return;
            }
            var n = h.PalmNormal;
            var w = h.Position(HandJoint.Wrist);
            _sb.Append(name)
                .Append(": conf ").Append(h.Confidence.ToString("F2"))
                .Append("  pinch ").Append(h.IndexPinch.ToString("F2"))
                .Append("  wristY ").Append((w.Y * 100f).ToString("F1")).Append("cm")
                .Append("  palm (").Append(n.X.ToString("F2")).Append(", ").Append(n.Y.ToString("F2")).Append(", ").Append(n.Z.ToString("F2")).Append(")\n");
        }
    }
}
