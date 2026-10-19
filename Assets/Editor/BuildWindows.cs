using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 一键打包 Windows 64 位版（Mono 后端，不需要额外装 IL2CPP 模块）。
    /// 菜单：奶龙 / 10. 打包 Windows 版
    /// 命令行：Unity.exe -batchmode -quit -projectPath "..." -executeMethod NailongFNAF.EditorTools.BuildWindows.Build
    /// 产物：Builds/Windows/NailongFiveNights.exe
    /// </summary>
    public static class BuildWindows
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string OutDir = "Builds/Windows";
        private const string ExeName = "NailongFiveNights.exe";

        [MenuItem("奶龙/10. 打包 Windows 版", false, 20)]
        public static void Build()
        {
            if (!File.Exists(ScenePath))
            {
                Fail("找不到场景 " + ScenePath);
                return;
            }

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            Directory.CreateDirectory(OutDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(OutDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("BUILD-OK 产物 " + Path.GetFullPath(options.locationPathName) +
                          " 大小 " + (summary.totalSize / (1024 * 1024)) + " MB" +
                          " 耗时 " + summary.totalTime.TotalSeconds.ToString("F0") + "s" +
                          " 错误 " + summary.totalErrors + " 警告 " + summary.totalWarnings);
                return;
            }

            Fail("打包失败：result=" + summary.result + " 错误数=" + summary.totalErrors);
        }

        private static void Fail(string message)
        {
            Debug.LogError("BUILD-FAIL " + message);
#if UNITY_EDITOR
            // 命令行跑的时候让退出码能反映出失败
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
#endif
        }
    }
}
