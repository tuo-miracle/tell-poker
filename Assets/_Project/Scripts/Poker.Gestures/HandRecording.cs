using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace Poker.Gestures
{
    /// <summary>
    /// 手部錄製檔（.hands）：在頭盔錄下真實的手部資料，拉回電腦回放，做手勢判斷的回歸測試。
    /// 格式：magic "HNDS" + 版本 int + 備註字串，之後每一幀：time(double) + 左手 + 右手。
    /// 每隻手：flags(byte) + confidence + pinch + 26 × (位置 3 float + 旋轉 4 float)。座標為牌桌座標（見 HandPose）。
    /// flags：bit0 = Tracked、bit1 = IndexPinching（第 2 版新增）。第 1 版的這個 byte 只有 tracked（非 0 即追蹤），讀取時 IndexPinching 一律 false。
    /// </summary>
    public sealed class HandRecording
    {
        public const int Version = 2;
        const byte FlagTracked = 1, FlagPinching = 2;
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("HNDS");

        public string Note = "";
        public readonly List<HandsFrame> Frames = new List<HandsFrame>();

        public static HandRecording Read(Stream stream)
        {
            using (var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            {
                byte[] magic = r.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
                    throw new InvalidDataException("not a hand recording");
                int version = r.ReadInt32();
                if (version != 1 && version != Version) throw new InvalidDataException($"unsupported recording version {version}");
                var rec = new HandRecording { Note = r.ReadString() };
                while (stream.Position < stream.Length)
                {
                    var f = new HandsFrame { Time = r.ReadDouble() };
                    ReadHand(r, f.Left, version);
                    ReadHand(r, f.Right, version);
                    rec.Frames.Add(f);
                }
                return rec;
            }
        }

        public static HandRecording Load(string path)
        {
            using (var fs = File.OpenRead(path)) return Read(fs);
        }

        static void ReadHand(BinaryReader r, HandPose h, int version)
        {
            byte flags = r.ReadByte();
            h.Tracked = version == 1 ? flags != 0 : (flags & FlagTracked) != 0;
            h.IndexPinching = version >= 2 && (flags & FlagPinching) != 0;
            h.Confidence = r.ReadSingle();
            h.IndexPinch = r.ReadSingle();
            for (int j = 0; j < HandPose.JointCount; j++)
            {
                h.Positions[j] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                h.Rotations[j] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            }
        }

        internal static void WriteHeader(BinaryWriter w, string note)
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(note ?? "");
        }

        internal static void WriteHand(BinaryWriter w, HandPose h)
        {
            w.Write((byte)((h.Tracked ? FlagTracked : 0) | (h.IndexPinching ? FlagPinching : 0)));
            w.Write(h.Confidence);
            w.Write(h.IndexPinch);
            for (int j = 0; j < HandPose.JointCount; j++)
            {
                var p = h.Positions[j];
                var q = h.Rotations[j];
                w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
                w.Write(q.X); w.Write(q.Y); w.Write(q.Z); w.Write(q.W);
            }
        }
    }

    /// <summary>逐幀寫入錄製檔（頭盔端每幀呼叫 Write）。</summary>
    public sealed class HandRecordingWriter : IDisposable
    {
        readonly BinaryWriter _w;

        public HandRecordingWriter(Stream stream, string note, bool leaveOpen = false)
        {
            _w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen);
            HandRecording.WriteHeader(_w, note);
        }

        public int FrameCount { get; private set; }

        public void Write(HandsFrame f)
        {
            _w.Write(f.Time);
            HandRecording.WriteHand(_w, f.Left);
            HandRecording.WriteHand(_w, f.Right);
            FrameCount++;
        }

        public void Dispose() => _w.Dispose();
    }
}
