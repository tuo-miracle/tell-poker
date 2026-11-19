using System;
using System.IO;
using System.Linq;
using Meta.XR;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Poker.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Poker.Editor
{
    /// <summary>
    /// 以程式重建 Main.unity（可在 batchmode 重跑，結果固定）：
    /// OVRCameraRig（OVRManager：passthrough、FloorLevel）＋ OVRPassthroughLayer ＋ ISDK OVRComprehensiveInteractionRig（手、poke、grab）
    /// ＋ MixedReality（MRUK、EnvironmentRaycastManager，預設停用）＋ Table（放置、外觀、錄製面板、除錯看板）＋ Hands（來源）＋ Systems。
    /// batchmode：-executeMethod Poker.Editor.MainSceneBuilder.BuildAndExit
    /// </summary>
    public static class MainSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        const string OvrCameraRigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        const string IsdkRigPrefab = "Packages/com.meta.xr.sdk.interaction.ovr/Runtime/Prefabs/OVRComprehensiveInteractionRig.prefab";
        const string IsdkRigName = "OVRComprehensiveInteractionRig";
        /// <summary>坐著玩、不需要移動：關掉 rig 內的移動相關物件，避免手勢誤觸瞬移，也避免 TunnelingEffect 缺相機的斷言。</summary>
        static readonly string[] DisabledRigParts =
        {
            "Locomotor", "LocomotionControllerInteractorGroup", "ScrollLocomotionGate",
            "MicroGesturesLocomotionHandInteractorGroup", "LocomotionOutput",
        };

        public static void BuildAndExit()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[MainSceneBuilder] failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>環境變數 TELL_DEV_TOOLS=1 時才把錄製面板與除錯看板放進場景。</summary>
        public static bool DevToolsEnabled => System.Environment.GetEnvironmentVariable("TELL_DEV_TOOLS") == "1";

        /// <summary>環境變數 TELL_REALISTIC_PREVIEW=1 時才放寫實角色預覽（Rocco 換成 Hank、Dot 換成寫實荷官，開發用）；預設關，模型不會被打包。</summary>
        public static bool RealisticPreviewEnabled =>
            Poker.Characters.RealisticPreview.IsOn(System.Environment.GetEnvironmentVariable(Poker.Characters.RealisticPreview.EnvVar));
        // 用 Baked 預製物（貼圖 ASTC 壓縮，見 Poker.Editor.Characters.RealisticBake）；GLB 自己的貼圖是未壓縮的，不要直接引用 GLB
        const string RealisticPreviewModel = "Assets/_Project/Art/Characters/Hank/Baked/Hank.prefab";
        const string RealisticDealerModel = "Assets/_Project/Art/Characters/Dealer/Baked/Dealer.prefab";
        /// <summary>預覽陣容可選的寫實角色（GLB 檔名＝名字）。</summary>
        static readonly string[] RealisticCast = { "Hank", "Marcus", "Jace", "Reginald", "Erik" };

        [MenuItem("Poker/Rebuild Main Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- 燈光：MR 用的主光＋補光＋平均環境光（Poker.Game.MrLighting） ----
            var light = new GameObject("Directional Light").AddComponent<Light>();
            Poker.Game.MrLighting.ConfigureKey(light);
            var fill = new GameObject("Fill Light").AddComponent<Light>();
            Poker.Game.MrLighting.ConfigureFill(fill);
            light.gameObject.AddComponent<Poker.Game.MrLighting>();
            Poker.Game.MrLighting.ApplyAmbient();

            // ---- OVRCameraRig + OVRManager + passthrough ----
            var cameraRig = InstantiatePrefab<OVRCameraRig>(OvrCameraRigPrefab, null, "OVRCameraRig");
            cameraRig.EnsureGameObjectIntegrity();
            var manager = cameraRig.GetComponent<OVRManager>();
            if (manager == null) throw new InvalidOperationException("OVRCameraRig prefab has no OVRManager");
            Set(manager, "isInsightPassthroughEnabled", true);
            Set(manager, "shouldBoundaryVisibilityBeSuppressed", true);   // 坐姿 MR：往前傾看牌時不要被系統邊界淡化（2026-10-15 實機）
            Set(manager, "_trackingOriginType", (int)OVRManager.TrackingOrigin.FloorLevel);
            if (!cameraRig.TryGetComponent<OVRPassthroughLayer>(out _)) cameraRig.gameObject.AddComponent<OVRPassthroughLayer>();

            var cam = cameraRig.centerEyeAnchor.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;   // passthrough 需要透明背景（OVRPassthroughHelper.cs:72-73）
            cam.nearClipPlane = 0.02f;
            cam.gameObject.tag = "MainCamera";
            var head = cameraRig.centerEyeAnchor;

            // ---- ISDK 手部 rig（OVRCameraRig 根物件的子物件，同官方精靈 OVRComprehensiveInteractionRigWizard.cs:89-110）----
            var isdkRig = InstantiatePrefab<Transform>(IsdkRigPrefab, cameraRig.transform, IsdkRigName);
            var rigRef = isdkRig.GetComponent<OVRCameraRigRef>();
            if (rigRef == null) throw new InvalidOperationException("ISDK rig has no OVRCameraRigRef");
            Set(rigRef, "_ovrCameraRig", cameraRig);
            Set(rigRef, "_requireOvrHands", false);   // ISDK rig 自帶 OVRHand（OVRHandsDataSource.prefab:54,140），相機錨點下不需要
            int disabled = 0;
            foreach (var t in isdkRig.GetComponentsInChildren<Transform>(true))
            {
                if (Array.IndexOf(DisabledRigParts, t.name) < 0) continue;
                t.gameObject.SetActive(false);
                disabled++;
            }
            if (disabled == 0) throw new InvalidOperationException("ISDK rig: locomotion parts not found (prefab changed?)");
            // 用 OVRHands 上的原始 Hand（LastKnownGood + 濾波後、未經 poke 限制的合成手），錄到的才是真手的位置。
            // 左右以資料來源物件名稱判斷（OVRHandsDataSource.prefab:225,241）；Editor 階段不能讀 Handedness（OVRAutoWiring.cs:163-165）
            Hand leftHand = null, rightHand = null;
            var ovrHands = FindDeep(isdkRig, "OVRHands");
            if (ovrHands == null) throw new InvalidOperationException("ISDK rig: OVRHands not found");
            foreach (var h in ovrHands.GetComponents<Hand>())
            {
                var src = new SerializedObject(h).FindProperty("_iModifyDataFromSourceMono")?.objectReferenceValue as Component;
                if (src == null) continue;
                if (src.gameObject.name.EndsWith("Left")) leftHand = h;
                else if (src.gameObject.name.EndsWith("Right")) rightHand = h;
            }
            if (leftHand == null || rightHand == null) throw new InvalidOperationException("ISDK rig: raw left/right Hand on OVRHands not found");
            OVRHand leftOvr = null, rightOvr = null;
            foreach (var h in isdkRig.GetComponentsInChildren<OVRHand>(true))
            {
                int type = new SerializedObject(h).FindProperty("HandType").intValue;
                if (type == (int)OVRHand.Hand.HandLeft) leftOvr = h;
                else if (type == (int)OVRHand.Hand.HandRight) rightOvr = h;
            }
            if (leftOvr == null || rightOvr == null) throw new InvalidOperationException("ISDK rig: left/right OVRHand not found");

            // ---- MRUK + EnvironmentRaycast：預設停用，TablePlacer 在有 XR 時才啟用（Mac Editor 不碰 MRUK native）----
            var mr = new GameObject("MixedReality");
            mr.SetActive(false);
            var mruk = mr.AddComponent<MRUK>();
            Set(mruk, "SceneSettings.DataSource", 0);              // SceneDataSource.Device（MRUK.cs:71-99）：裝置上不載入假房間
            Set(mruk, "SceneSettings.LoadSceneOnStartup", false);  // 由 TablePlacer 在權限到位後載入
            mr.AddComponent<EnvironmentRaycastManager>();

            // ---- 手部來源 ----
            var handsGo = new GameObject("Hands");
            var isdkSource = handsGo.AddComponent<IsdkHandsSource>();
            var playback = handsGo.AddComponent<RecordingHandsSource>();
            var selector = handsGo.AddComponent<HandsSourceSelector>();
            Set(selector, "live", isdkSource);
            Set(selector, "playback", playback);

            // ---- 牌桌 ----
            var tableGo = new GameObject("Table");
            var frame = tableGo.AddComponent<TableFrame>();
            tableGo.AddComponent<TableView>();
            var placer = tableGo.AddComponent<TablePlacer>();
            var calibration = tableGo.AddComponent<HandSurfaceCalibration>();
            Set(calibration, "table", frame);
            Set(calibration, "handsSource", selector);
            Set(placer, "table", frame);
            Set(placer, "head", head);
            Set(placer, "mixedRealityRoot", mr);

            // 開發工具（手勢錄製面板、手部除錯看板）：玩家版不放；錄手勢時用 TELL_DEV_TOOLS=1 tools/build-quest.sh 建
            if (DevToolsEnabled)
            {
                var panel = new GameObject("RecorderPanel").AddComponent<RecorderPanel>();
                panel.transform.SetParent(tableGo.transform, false);
                Set(panel, "table", frame);
                Set(panel, "handsSource", selector);

                var readout = new GameObject("HandDebug").AddComponent<HandDebugReadout>();
                readout.transform.SetParent(tableGo.transform, false);
                Set(readout, "handsSource", selector);
                Set(readout, "placer", placer);
                // 除錯看板放在玩家左前方的桌角外（角色都坐在遠半邊，不會被它擋住）
                var readoutSo = new SerializedObject(readout);
                readoutSo.FindProperty("position").vector3Value = new Vector3(-0.62f, 0.14f, 0.06f);
                readoutSo.ApplyModifiedPropertiesWithoutUndo();

            }

            // 寫實角色預覽（可行性驗證）：TELL_REALISTIC_PREVIEW=1 tools/build-quest.sh 才有
            if (RealisticPreviewEnabled)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(RealisticPreviewModel);
                if (model == null) throw new InvalidOperationException("realistic preview model missing: " + RealisticPreviewModel);
                var dealer = AssetDatabase.LoadAssetAtPath<GameObject>(RealisticDealerModel);
                if (dealer == null) throw new InvalidOperationException("realistic preview model missing: " + RealisticDealerModel);
                var preview = new GameObject("RealisticPreview").AddComponent<Poker.Characters.RealisticPreview>();
                preview.Model = model;
                preview.DealerModel = dealer;
                preview.Cast = RealisticCast.Select(n =>
                {
                    var path = $"Assets/_Project/Art/Characters/{n}/Baked/{n}.prefab";
                    var g = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (g == null) throw new InvalidOperationException("realistic preview model missing: " + path);
                    return g;
                }).ToArray();
            }

            Set(isdkSource, "table", frame);
            Set(isdkSource, "leftHand", leftHand);
            Set(isdkSource, "rightHand", rightHand);
            Set(isdkSource, "leftOvrHand", leftOvr);
            Set(isdkSource, "rightOvrHand", rightOvr);

            // ---- 牌局（Poker.Game）：角色、籌碼、牌都放在 Table 底下，跟著牌桌走 ----
            var game = new GameObject("Game").AddComponent<Poker.Game.GameController>();
            game.Config.Seed = 0; // 每次開遊戲抽新種子，牌序才不會每次都一樣
            // 寫實預覽是真實尺寸的 5 人局（2026-11-02：荷官右側多一位老手，只有寫實角色）；正式版維持 3 位卡通對手
            if (RealisticPreviewEnabled) game.Config.Opponents = (Poker.AI.Archetype[])Poker.Characters.RealisticPreview.Opponents.Clone();
            Set(game, "Table", frame);
            Set(game, "HandsSource", selector);
            Set(game, "Head", head);

            // ---- 系統 ----
            var systems = new GameObject("Systems");
            systems.AddComponent<QuestDisplaySettings>();
            var sim = systems.AddComponent<EditorSimulation>();
            Set(sim, "head", head);
            Set(sim, "selector", selector);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Save scene failed");
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath && s.enabled))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Verify();
            Debug.Log("[MainSceneBuilder] built " + ScenePath);
        }

        /// <summary>讀回存好的場景，檢查關鍵接線（缺了就丟例外，batchmode 會回傳 1）。</summary>
        static void Verify()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            T One<T>() where T : Component
            {
                var all = roots.SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
                if (all.Length != 1) throw new InvalidOperationException($"Main scene should have exactly 1 {typeof(T).Name}, found {all.Length}");
                return all[0];
            }
            One<OVRCameraRig>();
            var manager = One<OVRManager>();
            if (!new SerializedObject(manager).FindProperty("isInsightPassthroughEnabled").boolValue) throw new InvalidOperationException("passthrough not enabled");
            One<OVRPassthroughLayer>();
            One<MRUK>();
            One<EnvironmentRaycastManager>();
            One<TablePlacer>();
            // 開發工具只在 TELL_DEV_TOOLS=1 時才有；玩家版一個都不能有
            int devPanels = roots.SelectMany(r => r.GetComponentsInChildren<RecorderPanel>(true)).Count()
                          + roots.SelectMany(r => r.GetComponentsInChildren<HandDebugReadout>(true)).Count();
            if (devPanels != (DevToolsEnabled ? 2 : 0)) throw new InvalidOperationException($"dev tools in scene: {devPanels} (TELL_DEV_TOOLS={(DevToolsEnabled ? 1 : 0)})");
            // 寫實角色預覽同理：只有 TELL_REALISTIC_PREVIEW=1 才有，而且要有模型
            var previews = roots.SelectMany(r => r.GetComponentsInChildren<Poker.Characters.RealisticPreview>(true)).ToList();
            if (previews.Count != (RealisticPreviewEnabled ? 1 : 0) || previews.Any(p => p.Model == null || p.DealerModel == null || p.Cast == null || p.Cast.Length != RealisticCast.Length))
                throw new InvalidOperationException($"realistic preview in scene: {previews.Count} (TELL_REALISTIC_PREVIEW={(RealisticPreviewEnabled ? 1 : 0)})");
            int opponents = One<Poker.Game.GameController>().Config.Opponents.Length;
            if (opponents != (RealisticPreviewEnabled ? Poker.Characters.RealisticPreview.Opponents.Length : new Poker.Game.GameConfig().Opponents.Length))
                throw new InvalidOperationException($"opponents in scene: {opponents} (TELL_REALISTIC_PREVIEW={(RealisticPreviewEnabled ? 1 : 0)})");
            var game = new SerializedObject(One<Poker.Game.GameController>());
            foreach (var f in new[] { "Table", "HandsSource", "Head" })
                if (game.FindProperty(f).objectReferenceValue == null) throw new InvalidOperationException("GameController." + f + " not wired");
            var rigRef = One<OVRCameraRigRef>();
            if (new SerializedObject(rigRef).FindProperty("_ovrCameraRig").objectReferenceValue == null) throw new InvalidOperationException("OVRCameraRigRef not wired");
            var src = new SerializedObject(One<IsdkHandsSource>());
            foreach (var f in new[] { "table", "leftHand", "rightHand", "leftOvrHand", "rightOvrHand" })
                if (src.FindProperty(f).objectReferenceValue == null) throw new InvalidOperationException("IsdkHandsSource." + f + " not wired");
            var hands = roots.SelectMany(r => r.GetComponentsInChildren<OVRHand>(true)).ToArray();
            Debug.Log($"[MainSceneBuilder] verify ok: OVRHand x{hands.Length}, left={src.FindProperty("leftHand").objectReferenceValue.name}, right={src.FindProperty("rightHand").objectReferenceValue.name}");
        }

        /// <summary>診斷：列出 ISDK rig 的階層（深度 ≤ 4）與元件，batchmode 用。</summary>
        public static void DumpIsdkRig()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var rig = Object.FindAnyObjectByType<OVRCameraRigRef>(FindObjectsInactive.Include).transform;
            var sb = new System.Text.StringBuilder();
            void Walk(Transform t, int depth)
            {
                sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (inactive)").Append("  [")
                  .Append(string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name))).Append("]\n");
                if (depth < 4) foreach (Transform c in t) Walk(c, depth + 1);
            }
            Walk(rig, 0);
            File.WriteAllText("Logs/isdk-rig.txt", sb.ToString());
            EditorApplication.Exit(0);
        }

        static T InstantiatePrefab<T>(string path, Transform parent, string name) where T : Component
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new InvalidOperationException("Prefab not found: " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (parent != null) go.transform.SetParent(parent, false);
            go.name = name;
            var c = go.GetComponent<T>();
            if (c == null) throw new InvalidOperationException($"{path} has no {typeof(T).Name}");
            return c;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        static void Set(Object target, string property, object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(property);
            if (p == null) throw new InvalidOperationException($"{target.GetType().Name}.{property} not found");
            switch (value)
            {
                case bool b: p.boolValue = b; break;
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case Object o: p.objectReferenceValue = o; break;
                default: throw new ArgumentException("unsupported value for " + property);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
