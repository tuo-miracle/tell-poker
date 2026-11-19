using System;

namespace Poker.UX
{
    /// <summary>暫停來源（可同時多個）。</summary>
    [Flags] public enum PauseSource { None = 0, Menu = 1, HmdUnmounted = 2, FocusLost = 4 }

    /// <summary>
    /// 暫停狀態：每個來源各自開關，只要還有任何一個來源就維持暫停。
    /// 例如選單開著時戴回頭盔，平台來源解除了，選單來源仍在，所以不會恢復。
    /// </summary>
    public sealed class PauseState
    {
        public PauseSource Active { get; private set; }
        public bool IsPaused => Active != PauseSource.None;
        /// <summary>整體暫停狀態改變時觸發（參數為新的 IsPaused）。</summary>
        public event Action<bool> Changed;

        public void Set(PauseSource source, bool on)
        {
            bool was = IsPaused;
            Active = on ? Active | source : Active & ~source;
            if (was != IsPaused) Changed?.Invoke(IsPaused);
        }
    }
}
