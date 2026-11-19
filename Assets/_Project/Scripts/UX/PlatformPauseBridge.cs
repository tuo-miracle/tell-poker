namespace Poker.UX
{
    /// <summary>
    /// 平台暫停來源：摘下頭盔（OVRManager.HMDUnmounted）、失去輸入焦點（OVRManager.InputFocusLost，例如叫出系統選單）。
    /// 兩者各自是一個 PauseSource，所以戴回頭盔不會解除選單的暫停。事件定義見 OVRManager.cs:310、:330。
    /// </summary>
    public sealed class PlatformPauseBridge
    {
        readonly PauseState _pause;
        bool _subscribed;

        public PlatformPauseBridge(PauseState pause) { _pause = pause; }

        public void Subscribe()
        {
            if (_subscribed) return;
            OVRManager.HMDUnmounted += OnHmdUnmounted;
            OVRManager.HMDMounted += OnHmdMounted;
            OVRManager.InputFocusLost += OnInputFocusLost;
            OVRManager.InputFocusAcquired += OnInputFocusAcquired;
            _subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!_subscribed) return;
            OVRManager.HMDUnmounted -= OnHmdUnmounted;
            OVRManager.HMDMounted -= OnHmdMounted;
            OVRManager.InputFocusLost -= OnInputFocusLost;
            OVRManager.InputFocusAcquired -= OnInputFocusAcquired;
            _subscribed = false;
        }

        public void OnHmdUnmounted() => _pause.Set(PauseSource.HmdUnmounted, true);
        public void OnHmdMounted() => _pause.Set(PauseSource.HmdUnmounted, false);
        public void OnInputFocusLost() => _pause.Set(PauseSource.FocusLost, true);
        public void OnInputFocusAcquired() => _pause.Set(PauseSource.FocusLost, false);
    }
}
