using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 媒體音量是 0 時在牌桌上方顯示一行字（2026-10-13 實機：媒體音量 0，Android 把遊戲音訊靜音，玩家聽不到任何台詞）。
    /// 聲音被系統關掉，這一行只能用字；不是角色說的話，不走 SpeechChannel。
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>媒體音量來源（測試換成假的）；null＝Android 實機讀取，Editor／batchmode 回傳不知道。要在 Init 之前設定。</summary>
        public IMediaVolumeSource MediaVolume;
        /// <summary>提示字的位置（牌桌本地座標）：玩家前方、公共牌上方的視線外一點，對著玩家的頭。</summary>
        public static readonly Vector3 VolumeNoticeSpot = new Vector3(0f, 0.20f, 0.30f);

        public MediaVolumeGuard VolumeGuard { get; private set; }
        /// <summary>「Sound is off…」那一行。</summary>
        public WorldLabel VolumeNotice { get; private set; }

        void InitVolume()
        {
            VolumeGuard = new MediaVolumeGuard(MediaVolume, m => Debug.Log(m));
            VolumeNotice = new WorldLabel(Props.Root, "VolumeNotice", 0.0022f, new Color(1f, 0.85f, 0.4f));
        }

        void TickVolume(float dt)
        {
            VolumeGuard.Tick(dt);
            if (!VolumeGuard.Muted)
            {
                VolumeNotice.Hide();
                return;
            }
            VolumeNotice.Show(MediaVolumeGuard.Notice, VolumeNoticeSpot);
            if (Head != null) VolumeNotice.Face(Head.position);
        }
    }
}
