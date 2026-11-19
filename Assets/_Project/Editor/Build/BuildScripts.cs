using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Poker.Editor.Build
{
    /// <summary>batchmode 建置入口（tools/build-quest.sh、tools/build-mac.sh）。</summary>
    public static class BuildScripts
    {
        public const string QuestApkPath = "Builds/Quest/TellPoker.apk";
        public const string MacAppPath = "Builds/Mac/TellPoker.app";

        public static void BuildQuest() => Run(BuildTarget.Android, BuildTargetGroup.Android, QuestApkPath);

        public static void BuildMac() => Run(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, MacAppPath);

        static void Run(BuildTarget target, BuildTargetGroup group, string path)
        {
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != target)
                    throw new InvalidOperationException($"Active build target is {EditorUserBuildSettings.activeBuildTarget}; launch Unity with -buildTarget for {target}");

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (scenes.Length == 0) scenes = new[] { ProjectConfigurator.MainScenePath };
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = path,
                    target = target,
                    targetGroup = group,
                    options = BuildOptions.None,
                });
                var s = report.summary;
                Debug.Log($"[BuildScripts] RESULT={s.result} target={target} path={path} size={s.totalSize} errors={s.totalErrors} warnings={s.totalWarnings} time={s.totalTime}");
                EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError("[BuildScripts] RESULT=Exception " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
