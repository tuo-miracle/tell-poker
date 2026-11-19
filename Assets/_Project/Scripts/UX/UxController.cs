using System;
using Oculus.Interaction.Input;
using Poker.Game;
using Poker.Gestures;
using Poker.Runtime;
using Poker.Voice;
using UnityEngine;
using Hand = Poker.Gestures.Hand;

namespace Poker.UX
{
    /// <summary>
    /// UX 總管：掛在 GameController 的物件上（執行期由 UxBootstrap 掛，不改場景與 GameController）。
    /// 負責：設定存讀、手腕選單、暫停（選單／摘下頭盔／失去焦點分開計算）、手勢靈敏度注入、高對比、字幕大小、ISDK 注視。
    /// </summary>
    public sealed class UxController : MonoBehaviour
    {
        /// <summary>測試用：改設定檔位置。null 時用 persistentDataPath；批次模式（測試、建置）則不讀寫，避免使用者的設定影響其他測試。</summary>
        public static string SettingsPathOverride;

        public GameController Game { get; private set; }
        public PauseState Pause { get; } = new PauseState();
        public UxSettings Settings { get; private set; }
        public MenuModel Menu { get; private set; }
        public PlatformPauseBridge Platform { get; private set; }
        public WristMenuView View { get; private set; }
        public IsdkGazeRig Gaze { get; private set; }
        /// <summary>語音對話（牌局初始化後掛上；依 Settings 的 Voice chat 與同意開關）。</summary>
        public VoiceChatController Voice { get; private set; }
        /// <summary>手部追蹤 Fast Motion Mode 開關（實機 A/B 用；2026-10-15 起選單沒有按鈕，照存檔值套用）。</summary>
        public IHandTrackingMode HandTracking { get; private set; }
        public string SettingsPath { get; private set; }
        /// <summary>選單「How to play」頁選中手勢時，在面板正下方播放的示範手（第一次選時建立）。</summary>
        public GhostHands DemoGhost { get; private set; }
        Transform _demoAnchor;

        /// <summary>手腕按鈕所在的手（非慣用手）。</summary>
        public Hand WristHand => HandednessResolver.WristButtonHand(Settings.Handedness, SystemDominantHand());
        /// <summary>慣用手（給之後的手勢左右偏好用；Poker.Gestures 目前沒有這個參數）。</summary>
        public Hand DominantHand => HandednessResolver.Dominant(Settings.Handedness, SystemDominantHand());

        readonly WristMenuButton _wrist = new WristMenuButton();
        /// <summary>掌心朝上叫出的選單按鈕（主要路徑，2026-10-14）。</summary>
        readonly PalmMenu _palm = new PalmMenu();
        /// <summary>選單 near-miss／開關的 log（[UX] 在遙測白名單內）。</summary>
        readonly MenuSummonLog _summonLog = new MenuSummonLog { Sink = m => Debug.Log(m) };
        AudioSource _click;
        /// <summary>換位置校準成功時播的確認音次數（測試用）。</summary>
        public int RelocateChimes { get; private set; }
        HandSurfaceCalibration _calibration;
        public PalmMenu Palm => _palm;
        IHandsFrameSource _hands;
        GestureTuning _baseTuning;
        GameObject _root;
        bool _autoStepBefore = true, _pausedGame, _afterInitDone;

        public static UxController Attach(GameController game, string settingsPath = null, IMenuActions actions = null, IHandTrackingMode handTracking = null)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (game.TryGetComponent<UxController>(out var existing)) return existing;
            var ux = game.gameObject.AddComponent<UxController>();
            var path = settingsPath ?? SettingsPathOverride ?? (Application.isBatchMode ? null : SettingsStore.DefaultPath);
            ux.Setup(game, path, actions ?? new GameMenuActions(game), handTracking ?? new OvrHandTrackingMode());
            return ux;
        }

        void Setup(GameController game, string path, IMenuActions actions, IHandTrackingMode handTracking)
        {
            Game = game;
            SettingsPath = path;
            Settings = path != null ? SettingsStore.Load(path) : new UxSettings();
            HandTracking = handTracking;
            ApplyFastMotion();   // manifest 允許 HIGH，啟動時明確送一次目前設定（預設關）

            _root = new GameObject("UX");
            View = _root.AddComponent<WristMenuView>();
            _palm.Log = _summonLog;
            _wrist.Log = _summonLog;
            // 按下的「喀」聲：選單一開 AudioListener 就暫停，所以這個來源不受暫停影響
            _click = _root.AddComponent<AudioSource>();
            _click.playOnAwake = false;
            _click.ignoreListenerPause = true;
            _click.spatialBlend = 0f;
            Menu = new MenuModel(actions, Pause, Settings);
            View.Init(Menu);
            Menu.Changed += OnMenuChanged;
            Menu.SettingsChanged += OnSettingsChanged;
            Pause.Changed += OnPauseChanged;
            Platform = new PlatformPauseBridge(Pause);
            if (isActiveAndEnabled) Platform.Subscribe();

            // 靈敏度：在 Gestures 外建立套用倍率的 tuning；Init 前換掉 GameController.Tuning（辨識器會拿到這個實例），
            // Init 後則寫進辨識器已持有的實例（GestureRecognizer 每幀讀 Tuning）。
            _baseTuning = TuningScaler.Clone(game.Tuning);
            if (game.Recognizer != null) TuningScaler.ApplyInto(_baseTuning, Settings.GestureSensitivity, game.Recognizer.Tuning);
            else game.Tuning = TuningScaler.Apply(_baseTuning, Settings.GestureSensitivity);

            game.HintsEnabled = Settings.Hints;
            game.HandHintsEnabled = Settings.HandHints;

            // 換位置校準成功：短短的確認音（沒有適合的預錄荷官台詞，不用語音；牌局中也不打斷講話）
            if (game.Table != null && game.Table.TryGetComponent(out _calibration)) _calibration.Relocated += OnTableRelocated;

            _hands = game.HandsSource as IHandsFrameSource;
            if (_hands != null) { if (isActiveAndEnabled) Subscribe(true); }
            else Debug.LogWarning("[UX] GameController.HandsSource is not an IHandsFrameSource; wrist menu disabled");
        }

        bool _handsSubscribed;

        void Subscribe(bool on)
        {
            if (on) Platform?.Subscribe(); else Platform?.Unsubscribe();
            if (_hands == null || _handsSubscribed == on) return;
            if (on) _hands.FrameUpdated += OnFrame; else _hands.FrameUpdated -= OnFrame;
            _handsSubscribed = on;
        }

        void OnEnable() => Subscribe(true);
        void OnDisable() => Subscribe(false);

        void OnDestroy()
        {
            Subscribe(false);
            if (_calibration != null) _calibration.Relocated -= OnTableRelocated;
            if (_pausedGame)
            {
                // UX 被移除時把遊戲還回去，不能卡在暫停
                if (Game != null) Game.AutoStep = _autoStepBefore;
                AudioListener.pause = false;
                _pausedGame = false;
            }
            DemoGhost?.Dispose();
            if (_root != null) Destroy(_root);
        }

        float _reapplyAt;

        VoiceTutorialState _voiceState;

        /// <summary>
        /// 開發用（實機截選單：頭盔沒人戴、沒有手叫不出選單）：只有寫實預覽版（場景裡有 RealisticPreview）才讀
        /// adb shell setprop debug.tell.menu 1／2／3…＝打開、"settings"／"howtoplay"＝打開到那一頁、0＝關上；值改了才動作。
        /// </summary>
        public const string DevMenuProp = "debug.tell.menu";
        public enum DevMenuAction { None, Open, Close, Page }

        public static DevMenuAction DevMenu(string previous, string now)
        {
            if (now == previous || string.IsNullOrEmpty(now)) return DevMenuAction.None;
            if (now == "0") return DevMenuAction.Close;
            return int.TryParse(now, out _) ? DevMenuAction.Open : DevMenuAction.Page;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        string _devMenu = "";
        float _devMenuNext;
        bool? _devMenuAllowed;
        AndroidJavaClass _sysProps;

        void CheckDevMenu()
        {
            if (Time.unscaledTime < _devMenuNext || Menu == null) return;
            _devMenuNext = Time.unscaledTime + 0.5f;
            if (_devMenuAllowed == null) _devMenuAllowed = FindAnyObjectByType<Poker.Characters.RealisticPreview>() != null;
            if (_devMenuAllowed != true) return;
            if (_sysProps == null) _sysProps = new AndroidJavaClass("android.os.SystemProperties");
            var v = _sysProps.CallStatic<string>("get", DevMenuProp, "");
            var action = DevMenu(_devMenu, v);
            _devMenu = v;
            if (action == DevMenuAction.None) return;
            if (action == DevMenuAction.Close) { Menu.Close(); return; }
            if (!Menu.IsOpen) Menu.Open();
            if (action == DevMenuAction.Page && v == "settings") Menu.Press(MenuItem.Settings);
            if (action == DevMenuAction.Page && v == "howtoplay") Menu.Press(MenuItem.HowToPlay);
            Debug.Log($"[UX] dev menu {v}");
        }
#endif

        void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            CheckDevMenu();
#endif
            HandTracking?.Tick();
            _voiceState?.Tick(Time.unscaledTimeAsDouble);
            // 高對比：牌桌途中才建立的籌碼（例如飛行中的籌碼堆）也要補上外框
            if (_afterInitDone && Settings.HighContrast && Time.unscaledTime >= _reapplyAt)
            {
                _reapplyAt = Time.unscaledTime + 1f;
                HighContrast.Apply(Game, true);
            }
            // 選單暫停時 GameController 不 Step，示範手用真實時間自己播
            if (DemoGhost != null && DemoGhost.Current != null)
            {
                var head = HeadTransform();
                DemoGhost.Tick(Time.unscaledDeltaTime, head != null ? head.position : (Vector3?)null);
            }
            if (_afterInitDone || Game == null || !Game.IsInitialized) return;
            _afterInitDone = true;
            ApplyVisuals();
            // 批次模式（測試、建置）不碰真的麥克風
            Voice = VoiceChatController.Attach(Game, Application.isBatchMode ? new SilentMic() : null);
            _voiceState = new VoiceTutorialState(Voice);
            Game.TutorialVoice = _voiceState;
            ApplyVoice();
            if (XrActive()) InstallIsdkGaze();
            else Debug.Log("[UX] no XR device: gaze uses GameController's head ray (Editor fallback)");
        }

        void OnFrame(HandsFrame frame)
        {
            var hand = WristHand;
            var ctx = SummonContext();
            if (Game.Recognizer != null && _palm.Layout != Game.Recognizer.Layout) _palm.Layout = Game.Recognizer.Layout;
            bool viaPalm = _palm.Step(frame, hand, ctx);
            // 掌心朝上時手背朝下：手腕按鈕收起來
            bool viaWrist = !_palm.Visible && _wrist.Step(frame, hand, frame.Time, blocked: ctx.HoldingCards || ctx.HoldingChips);
            View.UpdateWristButton(frame, hand, Game.Table, _wrist.IsHovered, allowed: !_palm.Visible);
            View.UpdatePalmButton(_palm, Game.Table, hint: !Settings.PalmMenuHintSeen);
            if (viaPalm || viaWrist)
            {
                Menu.Toggle();
                _summonLog.Info($"menu {(Menu.IsOpen ? "opened" : "closed")} via {(viaPalm ? "palm" : "wrist")}");
                PlayClick();
                if (viaPalm)
                {
                    View.FlashPalmButton();
                    MarkPalmHintSeen();
                    View.UpdatePalmButton(_palm, Game.Table, hint: false);
                }
                return;
            }
            if (Menu.IsOpen && View.TryPress(frame, Game.Table, frame.Time, out var item)) Menu.Press(item);
        }

        /// <summary>叫選單的閘門：拿著牌或籌碼、攤牌等翻牌時不叫；頭的位置（牌桌座標）用來排除「掌心朝向頭」。</summary>
        PalmMenuContext SummonContext()
        {
            var head = HeadTransform();
            System.Numerics.Vector3? h = null;
            if (head != null && Game.Table != null)
            {
                var p = Game.Table.WorldToTable(head.position);
                h = new System.Numerics.Vector3(p.x, p.y, p.z);
            }
            return PalmMenuContext.From(Game.Recognizer, Game.Flow == GameFlow.ShowdownWait, h);
        }

        void OnTableRelocated(float distance, float yaw)
        {
            RelocateChimes++;
            if (_click == null) return;
            _click.pitch = 1.25f;
            _click.PlayOneShot(GameAudio.Clip(SfxKind.Win), 0.35f);
        }

        void PlayClick()
        {
            if (_click == null) return;
            _click.pitch = 1.7f;
            _click.PlayOneShot(GameAudio.Clip(SfxKind.Knock), 0.5f);
        }

        /// <summary>第一次用掌上按鈕成功開選單：之後不再顯示提示字（存檔；不經 SettingsChanged，免得重套所有設定）。</summary>
        void MarkPalmHintSeen()
        {
            if (Settings.PalmMenuHintSeen) return;
            Settings.PalmMenuHintSeen = true;
            if (SettingsPath == null) return;
            try { SettingsStore.Save(Settings, SettingsPath); }
            catch (Exception e) { Debug.LogWarning($"[UX] could not save settings: {e.Message}"); }
        }

        void OnMenuChanged()
        {
            Game.MenuOpen = Menu.IsOpen;   // 選單開著：牌局的示範手收起來、提示不計時
            UpdateDemo();
            if (!Menu.IsOpen) { View.Hide(); return; }
            if (!View.Visible)
            {
                var head = HeadTransform();
                View.ShowAt(head != null ? MenuPlacement.InFront(head.position, head.rotation) : new Pose(Vector3.forward * 0.45f, Quaternion.identity));
            }
            else View.Refresh();
        }

        void OnSettingsChanged(UxSettings s)
        {
            if (SettingsPath != null)
                try { SettingsStore.Save(s, SettingsPath); }
            catch (Exception e) { Debug.LogWarning($"[UX] could not save settings: {e.Message}"); }
            var target = Game.Recognizer != null ? Game.Recognizer.Tuning : Game.Tuning;
            TuningScaler.ApplyInto(_baseTuning, s.GestureSensitivity, target);
            Game.HintsEnabled = s.Hints;
            Game.HandHintsEnabled = s.HandHints;
            if (s.FastMotion != _appliedFastMotion) ApplyFastMotion();
            if (Game.IsInitialized) ApplyVisuals();
            ApplyVoice();
        }

        void ApplyVoice() { if (Voice != null) Voice.SetEnabled(Settings.VoiceConsent, Settings.VoiceChat); }

        bool _appliedFastMotion;

        void ApplyFastMotion()
        {
            _appliedFastMotion = Settings.FastMotion;
            HandTracking.SetFastMotion(_appliedFastMotion);
        }

        /// <summary>回到前景（例如摘下頭盔後再戴上、休眠喚醒）時重送：不確定 runtime 會不會保留 FMM 狀態。</summary>
        void OnApplicationPause(bool paused)
        {
            if (!paused && HandTracking != null) ApplyFastMotion();
        }

        void ApplyVisuals()
        {
            HighContrast.Apply(Game, Settings.HighContrast);
            SubtitleScaler.Apply(Game, Settings.SubtitleScale);
            if (DemoGhost != null) DemoGhost.CaptionScale = Settings.SubtitleScale;
        }

        /// <summary>How to play 頁選了某個手勢：在面板正下方播它的示範手（姿勢在選的那一刻定好）；離開這一頁或關選單就收起來。</summary>
        void UpdateDemo()
        {
            var want = Menu.IsOpen && Menu.Page == MenuPage.HowToPlay ? Menu.Demo : null;
            if (want == null) { DemoGhost?.Hide(); return; }
            if (Game == null || !Game.IsInitialized || Game.Table == null) return;
            if (DemoGhost == null)
            {
                _demoAnchor = new GameObject("GhostDemo").transform;
                _demoAnchor.SetParent(_root.transform, false);
                DemoGhost = new GhostHands(_demoAnchor, "DemoGhost");
                DemoGhost.CaptionScale = Settings.SubtitleScale;
            }
            if (DemoGhost.Current == want) return;
            DemoGhost.Play(want.Value);
            var head = HeadTransform();
            var tableRot = Game.Table.transform.rotation;
            var pose = head != null
                ? MenuPlacement.GhostDemo(head.position, head.rotation, tableRot, DemoGhost.ClipCenter)
                : new Pose(Game.Table.transform.position, tableRot);
            _demoAnchor.SetPositionAndRotation(pose.position, pose.rotation);
        }

        void OnPauseChanged(bool paused)
        {
            if (paused)
            {
                _autoStepBefore = Game.AutoStep;
                Game.AutoStep = false;           // GameController 不 Step：AI 計時、流程、手勢→行動全部停住
                AudioListener.pause = true;
                _pausedGame = true;
            }
            else
            {
                // 不呼叫 Recognizer.Reset()：它會無聲清掉待確認的下注與手上的籌碼，GameController 那邊卻還記著（狀態會不同步）。
                // 辨識器自己會把暫停的時間差夾到 0.25 秒（GestureRecognizer.cs:99），做到一半的手勢照常結束。
                Game.AutoStep = _autoStepBefore;
                AudioListener.pause = false;
                _pausedGame = false;
            }
        }

        Transform HeadTransform() => Game.Head != null ? Game.Head : (Camera.main != null ? Camera.main.transform : null);

        internal static bool XrActive()
        {
            if (Application.isBatchMode) return false;   // 測試／建置不裝實機注視
            try { return UnityEngine.XR.XRSettings.isDeviceActive || OVRManager.isHmdPresent; }
            catch { return false; }
        }

        static Hand? SystemDominantHand()
        {
            try
            {
                var h = OVRInput.GetDominantHand();
                return h == OVRInput.Handedness.LeftHanded ? Hand.Left : h == OVRInput.Handedness.RightHanded ? Hand.Right : (Hand?)null;
            }
            catch { return null; }
        }

        /// <summary>
        /// 實機注視：ISDK FromOVREyeGazeDataSource → EyeGaze → GazeConecaster → GazeInteractor。
        /// Quest 3／3S 沒有眼動（OVRPlugin.eyeGazeInteractionsSupported=false，FromOVREyeGazeDataSource.cs:72），
        /// 資料來源只帶相機姿勢（:124-128），所以開 EyeGaze.EmulateGazeWithCameraPose 用頭部朝向（EyeGaze.cs:58-66）。
        /// </summary>
        void InstallIsdkGaze()
        {
            var head = HeadTransform();
            if (head == null || Game.Pip == null) return;
            IGaze gaze;
            var rigRef = FindAnyObjectByType<Oculus.Interaction.Input.OVRCameraRigRef>();
            if (rigRef != null)
            {
                var go = new GameObject("UxEyeGaze");
                go.transform.SetParent(rigRef.transform, false);   // FromOVREyeGazeDataSource.Awake 用 GetComponentInParent 找 OVRCameraRigRef
                var src = go.AddComponent<FromOVREyeGazeDataSource>();
                Camera cam = null;
                if (!head.TryGetComponent(out cam)) cam = Camera.main;
                src.InjectAllFromOVREyeGazeDataSource(DataSource<EyeGazeDataAsset>.UpdateModeFlags.UnityUpdate, null, cam);
                bool eyes = OVRPlugin.eyeGazeInteractionsSupported;
                gaze = IsdkGazeRig.AddEmulatingEyeGaze(go, src, emulateWithCamera: !eyes);
                Debug.Log($"[UX] ISDK gaze installed; eye tracking {(eyes ? "available" : "unavailable -> EyeGaze emulates with camera (head) pose")}");
            }
            else
            {
                var tg = _root.AddComponent<TransformGaze>();
                tg.Source = head;
                gaze = tg;
                Debug.Log("[UX] ISDK gaze installed with head transform (no OVRCameraRigRef)");
            }
            Gaze = IsdkGazeRig.Build(_root.transform, gaze, head);
            RegisterGazeTargets(Gaze);
            GazeTracker.DefaultSource = Gaze;
        }

        /// <summary>把每個角色登記成注視目標，編號用 GameController.GazeTargets（AI＝座位號、荷官＝DealerGazeId）；頭 0.16 m、胸 0.17 m 與 GameController.TickGaze 一致。</summary>
        public void RegisterGazeTargets(IsdkGazeRig rig)
        {
            foreach (var (who, id) in Game.GazeTargets())
            {
                if (who == null || who.Rig == null) continue;
                if (who.Rig.Head != null) rig.AddTarget(who.Rig.Head, 0.16f, id);
                if (who.Rig.Chest != null) rig.AddTarget(who.Rig.Chest, 0.17f, id);
            }
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }

    /// <summary>
    /// 教學與提示要的語音狀態（Poker.Game 不能引用 Poker.Voice）。
    /// Available＝同意過、打開、麥克風開著，而且中繼服務連得到（語音開著時每 ProbeEverySeconds 秒打一次 GET /health；
    /// 還沒回來或失敗都算連不到，教學的語音那一步就安靜跳過）。2026-10-13 教學 v3。
    /// </summary>
    public sealed class VoiceTutorialState : ITutorialVoice
    {
        public const double ProbeEverySeconds = 20;
        readonly Func<bool> _on, _busy;
        readonly Func<int> _utterances;
        readonly Action<Action<bool>> _probe;
        bool _reachable, _probing;
        double _probedAt = double.NegativeInfinity;

        public VoiceTutorialState(VoiceChatController voice) : this(
            () => voice != null && voice.Session.Enabled && voice.MicOn,
            () => voice != null && (voice.Session.State == VoiceState.Capturing || voice.Session.State == VoiceState.Waiting),
            () => voice != null ? voice.Session.RequestsSent : 0,
            done => HealthCheck(voice != null ? voice.Config.ProxyUrl : null, done)) { }

        public VoiceTutorialState(Func<bool> on, Func<bool> busy, Func<int> utterances, Action<Action<bool>> probe)
        {
            _on = on; _busy = busy; _utterances = utterances; _probe = probe;
        }

        public bool Reachable => _reachable;
        public bool Available => _on() && _reachable;
        public bool Busy => _busy();
        public int Utterances => _utterances();

        /// <summary>每幀呼叫（now＝秒）：語音開著就定期確認中繼連得到。</summary>
        public void Tick(double now)
        {
            if (!_on() || _probing || now - _probedAt < ProbeEverySeconds) return;
            _probedAt = now;
            _probing = true;
            _probe(ok => { _reachable = ok; _probing = false; });
        }

        static void HealthCheck(string proxyUrl, Action<bool> done)
        {
            if (string.IsNullOrEmpty(proxyUrl)) { done(false); return; }
            var req = UnityEngine.Networking.UnityWebRequest.Get(proxyUrl.TrimEnd('/') + "/health");
            req.timeout = 3;
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                bool ok = req.result == UnityEngine.Networking.UnityWebRequest.Result.Success && req.responseCode == 200;
                if (!ok) Debug.Log("[Voice] proxy health check failed: " + req.error);
                req.Dispose();
                done(ok);
            };
        }
    }
}
