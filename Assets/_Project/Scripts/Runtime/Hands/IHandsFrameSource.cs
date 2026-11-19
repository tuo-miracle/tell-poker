using System;
using Poker.Gestures;

namespace Poker.Runtime
{
    /// <summary>
    /// 雙手資料來源（牌桌座標）。遊戲層與測試只依賴這個介面，可替換成實機（ISDK）或 .hands 回放。
    /// </summary>
    public interface IHandsFrameSource
    {
        /// <summary>最新一幀。實作會重用同一個物件，要保存請 Clone 各手。尚無資料時兩手 Tracked = false。</summary>
        HandsFrame Current { get; }

        /// <summary>Current 更新後觸發，每個 Unity 幀最多一次。</summary>
        event Action<HandsFrame> FrameUpdated;
    }

    public static class HandsFrameUtil
    {
        public static void CopyPose(HandPose src, HandPose dst)
        {
            dst.Tracked = src.Tracked;
            dst.Confidence = src.Confidence;
            dst.IndexPinch = src.IndexPinch;
            Array.Copy(src.Positions, dst.Positions, HandPose.JointCount);
            Array.Copy(src.Rotations, dst.Rotations, HandPose.JointCount);
        }

        public static void Copy(HandsFrame src, HandsFrame dst)
        {
            dst.Time = src.Time;
            CopyPose(src.Left, dst.Left);
            CopyPose(src.Right, dst.Right);
        }
    }
}
