using System;
using System.IO;
using Poker.Gestures;

namespace Poker.Runtime
{
    /// <summary>
    /// 一次錄製：Start 建立 &lt;標籤&gt;_&lt;yyyyMMdd_HHmmss&gt;.hands，Write 逐幀寫入，Stop 關檔。
    /// 沒在錄製時 Write／Stop 都是 no-op。
    /// </summary>
    public sealed class RecordingSession : IDisposable
    {
        readonly string _directory;
        FileStream _stream;
        HandRecordingWriter _writer;

        public RecordingSession(string directory) => _directory = directory;

        public bool IsRecording => _writer != null;
        public string CurrentPath { get; private set; }
        public string CurrentLabel { get; private set; }
        public int FrameCount => _writer?.FrameCount ?? 0;

        public static string FileNameFor(string label, DateTime start) => $"{label}_{start:yyyyMMdd_HHmmss}.hands";

        /// <summary>開始錄製，回傳檔案路徑。已在錄製時先關掉上一個檔。</summary>
        public string Start(string label, DateTime start, string extraNote)
        {
            Stop();
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, FileNameFor(label, start));
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(_directory, $"{label}_{start:yyyyMMdd_HHmmss}_{i}.hands");

            string note = $"label={label};start={start:yyyy-MM-ddTHH:mm:ss};{extraNote}";
            _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            try
            {
                _writer = new HandRecordingWriter(_stream, note);
            }
            catch
            {
                _stream.Dispose();
                _stream = null;
                throw;
            }
            CurrentPath = path;
            CurrentLabel = label;
            return path;
        }

        public void Write(HandsFrame frame)
        {
            if (_writer == null) return;
            _writer.Write(frame);
        }

        /// <summary>結束並關檔，回傳剛錄好的路徑（沒在錄製時回 null）。</summary>
        public string Stop()
        {
            if (_writer == null) return null;
            _writer.Dispose();   // BinaryWriter 會一併關閉 stream
            _stream = null;
            _writer = null;
            return CurrentPath;
        }

        public void Dispose() => Stop();
    }
}
