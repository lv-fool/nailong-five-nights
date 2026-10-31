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

        /// <summary>
        /// 游戏说明的源文件（版本库里维护），打包后复制到产物目录。
        /// 分发时**必须带上它** —— 玩家第一次运行会撞上 SmartScreen 提示，
        /// 说明里写了怎么绕过，否则会被当成病毒。
        /// </summary>
        private const string ReadmeSource = "docs/游戏说明.txt";

        /// <summary>复制到产物目录后的文件名（中文，玩家一眼能看见）。</summary>
        private const string ReadmeTarget = "游戏说明.txt";

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
                CopyReadme();

                Debug.Log("BUILD-OK 产物 " + Path.GetFullPath(options.locationPathName) +
                          " 大小 " + (summary.totalSize / (1024 * 1024)) + " MB" +
                          " 耗时 " + summary.totalTime.TotalSeconds.ToString("F0") + "s" +
                          " 错误 " + summary.totalErrors + " 警告 " + summary.totalWarnings);
                return;
            }

            Fail("打包失败：result=" + summary.result + " 错误数=" + summary.totalErrors);
        }

        /// <summary>
        /// 把 <see cref="ReadmeSource"/> 复制到产物目录。
        ///
        /// 缺失时不报错、只警告 —— 说明文档没了不该让整个打包失败，
        /// 但要在 Console 里留一句，免得静默漏发。
        /// </summary>
        private static void CopyReadme()
        {
            if (!File.Exists(ReadmeSource))
            {
                Debug.LogWarning("BUILD-WARN 找不到游戏说明 " + ReadmeSource + "，产物里不会有它");
                return;
            }

            string target = Path.Combine(OutDir, ReadmeTarget);

            try
            {
                File.Copy(ReadmeSource, target, true);
                Debug.Log("BUILD-OK 游戏说明已复制 → " + Path.GetFullPath(target));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("BUILD-WARN 游戏说明复制失败：" + e.Message);
            }
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
