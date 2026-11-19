using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Meta.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace Poker.Editor.Build
{
    /// <summary>
    /// 一次套用 Quest 與桌面建置需要的專案設定（可重複執行）。
    /// batchmode：-buildTarget Android -executeMethod Poker.Editor.Build.ProjectConfigurator.ApplyAndExit
    /// </summary>
    public static class ProjectConfigurator
    {
        /// <summary>2026-10-15 實機：往前傾時系統邊界把整個遊戲淡化。MR App 可要求隱藏邊界（OVRManager.shouldBoundaryVisibilityBeSuppressed，需 passthrough）。</summary>
        public const bool HidesBoundary = true;
        public const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
        public const string SettingsFolder = "Assets/_Project/Settings";
        public const string PipelinePath = SettingsFolder + "/Poker_URP.asset";
        public const string RendererPath = SettingsFolder + "/Poker_Renderer.asset";
        public const string ApplicationId = "com.maxlai.tellpoker";

        const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string MetaXRFeatureSetId = "com.meta.openxr.featureset.metaxr";   // MetaXRFeatureSet.cs:69（類別非 public）
        const string TouchProfileId = "com.unity.openxr.feature.input.oculustouch";
        const string SpaceWarpFeatureId = "com.unity.openxr.feature.spacewarp";   // spec §6：ASW 會讓手部延遲，關閉
        const string OvrProjectConfigPath = "Assets/Oculus/OculusProjectConfig.asset";

        // 不要把 GUI/Text Shader 加進來：它屬於 unity default resources（DontSave），列入會讓 Android 建置寫 unity_builtin_extra 失敗；
        // TextMesh 的內建字型與 shader 本來就隨 player 打包。
        static readonly string[] AlwaysIncluded = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit" };

        /// <summary>batchmode 入口：套用後結束 Unity（失敗回傳 1）。</summary>
        public static void ApplyAndExit()
        {
            try
            {
                Apply();
                // 成功標記：本機 Unity 偶爾在關閉時崩潰（libc++abi，exit 134），build-quest.sh 以這行判斷設定其實已完成
                Debug.Log("[ProjectConfigurator] APPLY_OK");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[ProjectConfigurator] Apply failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Poker/Apply Project Settings")]
        public static void Apply()
        {
            ApplyPlayerSettings();
            ApplyRenderPipeline();
            ApplyXr();
            ApplyOvrProjectConfig();
            EnsureMainScene();
            AssetDatabase.SaveAssets();

            // 掃尾：Meta Project Setup 的「全部修正」，之後再套一次自己的設定，避免被覆蓋
            if (!HasArg("-skipFixAll")) FixAllProjectSetup();
            ApplyPlayerSettings();
            ApplyRenderPipeline();
            ApplyXr();
            ApplyOvrProjectConfig();
            AssetDatabase.SaveAssets();

            if (HasArg("-reportFile")) OVRProjectSetupCLI.GenerateProjectSetupReport();
            Debug.Log("[ProjectConfigurator] Apply done");
        }

        // ---- Player ----
        static void ApplyPlayerSettings()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;   // Gamma 會讓 OVRGradleGeneration 丟 BuildFailedException
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, ApplicationId);
            PlayerSettings.productName = "Tell";
            // Meta 套件有用到 Microphone 類別；macOS 建置沒有說明文字會直接失敗
            PlayerSettings.iOS.microphoneUsageDescription = "Voice chat with the characters uses the microphone.";
            // 語音對話：中繼已改 https（https://vr-poker-api.elebymax.com，WSS），release 不允許明文 HTTP（Android 的 usesCleartextTraffic=false）。
            // 本機中繼（tools/voice-dev.sh，http://127.0.0.1:8787 經 adb reverse）要測時：TELL_ALLOW_HTTP=1 tools/build-quest.sh（只給開發建置）。
            // RECORD_AUDIO／INTERNET 寫在 Assets/Plugins/Android/AndroidManifest.xml。
            var http = HttpOptionFor(Environment.GetEnvironmentVariable(AllowHttpEnv));
            PlayerSettings.insecureHttpOption = http;
            if (http != InsecureHttpOption.NotAllowed) Debug.LogWarning($"[ProjectConfigurator] {AllowHttpEnv} set: cleartext HTTP allowed (dev build only, never ship this)");
            PlayerSettings.Android.forceInternetPermission = true;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.Android.androidTVCompatibility = false;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            EditorUserBuildSettings.buildAppBundle = false;
            // Active Input Handling = Both：沒有公開 API，ProjectSettings.asset 的 activeInputHandler 直接設為 2
        }

        /// <summary>開發用的環境變數：設成 1／true 才允許明文 HTTP（連本機中繼）；沒設或其他值＝release 的 NotAllowed。</summary>
        public const string AllowHttpEnv = "TELL_ALLOW_HTTP";

        /// <summary>明文 HTTP 設定：預設 NotAllowed；只有 TELL_ALLOW_HTTP=1（或 true）時 AlwaysAllowed。</summary>
        public static InsecureHttpOption HttpOptionFor(string env)
        {
            var v = (env ?? "").Trim();
            bool allow = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
            return allow ? InsecureHttpOption.AlwaysAllowed : InsecureHttpOption.NotAllowed;
        }

        // ---- URP ----
        static void ApplyRenderPipeline()
        {
            Directory.CreateDirectory(SettingsFolder);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            pipeline.msaaSampleCount = 4;
            pipeline.supportsHDR = false;            // Quest：HDR 增加頻寬
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.shadowDistance = 0f;
            // 陰影 setter 是 internal，用 SerializedObject（spec §6.2：不用即時陰影）
            var so = new SerializedObject(pipeline);
            so.FindProperty("m_MainLightShadowsSupported").boolValue = false;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            so.FindProperty("m_SoftShadowsSupported").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;

            var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = quality.FindProperty("m_QualitySettings");
            for (int i = 0; i < levels.arraySize; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipeline;
                level.FindPropertyRelative("antiAliasing").intValue = 4;
                level.FindPropertyRelative("shadows").intValue = 0;
            }
            quality.ApplyModifiedPropertiesWithoutUndo();

            // Materials.cs 用 Shader.Find 建材質，建置時必須 always included，否則會洋紅色
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in AlwaysIncluded)
            {
                var shader = Shader.Find(name);
                if (shader == null) throw new InvalidOperationException("Shader not found: " + name);
                bool present = false;
                for (int i = 0; i < shaders.arraySize; i++)
                    if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
                if (present) continue;
                shaders.InsertArrayElementAtIndex(shaders.arraySize);
                shaders.GetArrayElementAtIndex(shaders.arraySize - 1).objectReferenceValue = shader;
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- XR Plug-in Management：Android 用 OpenXR；Standalone（Mac 桌面模式）不啟用 XR ----
        static void ApplyXr()
        {
            var android = GetOrCreateXrSettings(BuildTargetGroup.Android);
            android.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(android.AssignedSettings, OpenXRLoaderType, BuildTargetGroup.Android))
                throw new InvalidOperationException("AssignLoader(OpenXRLoader, Android) failed");
            EditorUtility.SetDirty(android);

            var standalone = GetOrCreateXrSettings(BuildTargetGroup.Standalone);
            foreach (var loader in standalone.AssignedSettings.activeLoaders.ToList())
                XRPackageMetadataStore.RemoveLoader(standalone.AssignedSettings, loader.GetType().FullName, BuildTargetGroup.Standalone);
            standalone.InitManagerOnStart = false;
            EditorUtility.SetDirty(standalone);

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var set = OpenXRFeatureSetManager.GetFeatureSetWithId(BuildTargetGroup.Android, MetaXRFeatureSetId);
            if (set == null) throw new InvalidOperationException("Meta XR feature set not found");
            set.isEnabled = true;
            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(BuildTargetGroup.Android);
            EnableFeature(MetaXRFeature.featureId);
            EnableFeature(TouchProfileId);   // Meta 規則：Required
            DisableFeature(SpaceWarpFeatureId);
            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (openXr != null) EditorUtility.SetDirty(openXr);
        }

        static void EnableFeature(string id)
        {
            var f = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, id);
            if (f == null) throw new InvalidOperationException("OpenXR feature not found: " + id);
            f.enabled = true;
            EditorUtility.SetDirty(f);
        }

        static void DisableFeature(string id)
        {
            var f = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, id);
            if (f == null) return;   // 沒有這個 feature 就等於關閉
            f.enabled = false;
            EditorUtility.SetDirty(f);
        }

        /// <summary>GetOrCreate 是 internal，照 Meta 的 OVRProjectSetupXRTasks 用公開 API 建立。</summary>
        static XRGeneralSettings GetOrCreateXrSettings(BuildTargetGroup group)
        {
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            if (general != null) return general;

            EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey, out var perTarget);
            if (perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                AssetDatabase.SaveAssets();
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasManagerSettingsForBuildTarget(group))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            if (general == null) throw new InvalidOperationException("Cannot create XR settings for " + group);
            return general;
        }

        // ---- OVRProjectConfig：手部、Scene、錨點、穿透 → 建置時寫進 AndroidManifest ----
        static void ApplyOvrProjectConfig()
        {
            // 不用 CachedProjectConfig：EditorReady 前會回 null（OVRProjectConfig.cs:260-270）
            var cfg = AssetDatabase.LoadAssetAtPath<OVRProjectConfig>(OvrProjectConfigPath);
            if (cfg == null) throw new InvalidOperationException("OculusProjectConfig.asset not found");
            cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            // manifest 宣告 HIGH（com.oculus.handtracking.frequency，OVRManifestPreprocessor.cs:723-731）讓執行期可以切 Fast Motion Mode；
            // 實際開關由手腕選單 Settings 的 Fast motion 決定，UxController 啟動時明確送一次（預設關），見 OvrHandTrackingMode
            cfg.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.HIGH;
            cfg.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
            // 坐姿 MR：passthrough 一直開著，玩家往前傾看牌時頭會超出靜止邊界 → 要求系統不要淡化畫面（manifest 加 BOUNDARY_VISIBILITY 權限）
            cfg.boundaryVisibilitySupport = HidesBoundary ? OVRProjectConfig.FeatureSupport.Supported : OVRProjectConfig.FeatureSupport.None;
            cfg.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
            cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            OVRProjectConfig.CommitProjectConfig(cfg);
        }

        // ---- 場景 ----
        // Main.unity 由 Poker.Editor.MainSceneBuilder 以程式重建（OVRCameraRig、passthrough、ISDK 手、MRUK、牌桌、錄製工具）
        static void EnsureMainScene()
        {
            if (HasArg("-skipScene") && File.Exists(MainScenePath)) return;
            Poker.Editor.MainSceneBuilder.Build();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
        }

        // ---- Meta Project Setup「全部修正」 ----
        static void FixAllProjectSetup()
        {
            // 公開的 FixAllAsync 是非阻塞的，batchmode 不好等；用 internal 的 blocking 版本（OVRProjectSetup.cs:589）
            var fix = typeof(OVRProjectSetup).GetMethod("FixTasks", BindingFlags.NonPublic | BindingFlags.Static);
            if (fix == null)
            {
                Debug.LogWarning("[ProjectConfigurator] OVRProjectSetup.FixTasks not found; skip FixAll");
                return;
            }
            var parameters = fix.GetParameters();
            var args = new object[parameters.Length];
            args[0] = BuildTargetGroup.Android;
            for (int i = 1; i < parameters.Length; i++) args[i] = parameters[i].DefaultValue;
            try
            {
                fix.Invoke(null, args);
                Debug.Log("[ProjectConfigurator] OVRProjectSetup FixTasks(Android) done");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ProjectConfigurator] FixTasks threw (non-fatal): " + e);
            }
        }

        static bool HasArg(string name) => Environment.GetCommandLineArgs().Contains(name);
    }
}
