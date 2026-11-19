using System;
using System.IO;
using System.Linq;
using Poker.Gestures;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 用 .hands 回放當手部來源（沒戴頭盔時在 Editor 檢查畫面、PlayMode 測試）。
    /// recordingPath 留空時，Editor 會自動找 Assets/_Project/Tests/Gestures/Recordings/ 的第一個 .hands。
    /// </summary>
    public sealed class RecordingHandsSource : MonoBehaviour, IHandsFrameSource
    {
        public const string ProjectRecordingsFolder = "_Project/Tests/Gestures/Recordings";

        [Tooltip("絕對路徑，或相對於專案根目錄的路徑；留空 = 自動找")]
        [SerializeField] string recordingPath = "";
        [SerializeField] bool loop = true;
        [SerializeField] bool playOnStart = true;

        readonly HandsFrame _current = new HandsFrame();
        HandRecordingPlayer _player;
        double _elapsed;

        public HandsFrame Current => _current;
        public event Action<HandsFrame> FrameUpdated;
        public bool IsPlaying => _player != null;
        public string LoadedPath { get; private set; }

        public void Play(HandRecording recording, bool loop)
        {
            _player = new HandRecordingPlayer(recording, loop);
            _elapsed = 0.0;
        }

        public void Stop() => _player = null;

        void Start()
        {
            if (!playOnStart || _player != null) return;
            var path = ResolvePath(recordingPath);
            if (path == null) return;
            try
            {
                Play(HandRecording.Load(path), loop);
                LoadedPath = path;
                Debug.Log("[RecordingHandsSource] playing " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RecordingHandsSource] cannot load " + path + ": " + e.Message);
            }
        }

        void Update()
        {
            if (_player == null) return;
            _elapsed += Time.deltaTime;
            _player.Sample(_elapsed, _current);
            FrameUpdated?.Invoke(_current);
        }

        static string ResolvePath(string configured)
        {
            if (!string.IsNullOrEmpty(configured))
            {
                var p = Path.IsPathRooted(configured) ? configured : Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", configured);
                return File.Exists(p) ? p : null;
            }
            if (!Application.isEditor) return null;
            var dir = Path.Combine(Application.dataPath, ProjectRecordingsFolder);
            if (!Directory.Exists(dir)) return null;
            return Directory.GetFiles(dir, "*.hands").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
        }
    }
}
