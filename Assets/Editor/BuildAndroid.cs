using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 一键打包安卓版（IL2CPP + ARM64，锁横屏）。
    /// 菜单：奶龙 / 11. 打包安卓版
    ///
    /// 前提：Unity Hub 已给 2022.3.62f3c1 装上 **Android Build Support** 模块。
    /// 只有 SDK/NDK/OpenJDK 而没有播放器引擎（Variations 目录）时构建必失败，
    /// 所以这里先探测一下，缺了就给出人话提示。
    ///
    /// 产物：Builds/Android/NailongFiveNights.apk（调试签名，adb install 直接侧载；
    /// 要上架应用商店再换成自己的 keystore 和 AAB）。
    /// </summary>
    public static class BuildAndroid
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string OutDir = "Builds/Android";
        private const string ApkName = "NailongFiveNights.apk";
        private const string PanoTexturePath = "Assets/Resources/office_pano.png";
        private const string ApplicationId = "com.lvfool.nailongfivenights";

        [MenuItem("奶龙/11. 打包安卓版", false, 21)]
        public static void Build()
        {
            if (!File.Exists(ScenePath))
            {
                Fail("找不到场景 " + ScenePath);
                return;
            }

            string variations = Path.Combine(EditorApplication.applicationContentsPath,
                "PlaybackEngines", "AndroidPlayer", "Variations");
            if (!Directory.Exists(variations))
            {
                Fail("Android Build Support 没装全：找不到 " + variations +
                     "\nSDK/NDK 在而播放器引擎不在 —— 用 Unity Hub → Installs → " +
                     "2022.3.62f3c1 → Add modules 补装 Android Build Support（含 OpenJDK 和 SDK & NDK Tools）。");
                return;
            }

            ApplyPlayerSettings();
            OverridePanoTextureForAndroid();

            Directory.CreateDirectory(OutDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(OutDir, ApkName),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
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

        /// <summary>
        /// 安卓玩家设置。只在这里设（而不是一次性写死），照 <see cref="BuildWindows"/>
        /// 的习惯：打包即配置，项目设置永远跟脚本走，换机器不用对着文档重设。
        /// </summary>
        private static void ApplyPlayerSettings()
        {
            // productName / companyName 用项目里现成的（奶龙的五夜后宫 / NailongStudio）——
            // 这俩是全局设置，别在这覆盖，否则 Windows 版的窗口标题也跟着变。
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, ApplicationId);
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.bundleVersionCode = 1;

            // IL2CPP + ARM64：2022.3 的安卓默认组合，新手机没有 32 位-only 的了。
            // IL2CPP 构建比 Mono 慢几分钟，但包体更小、运行更快。
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel22;

            // 锁横屏：办公室全景是横向构图，竖屏没法玩
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // 切后台就暂停（游戏本来就是「坐在办公室里盯一整夜」）
            PlayerSettings.runInBackground = false;

            // 默认纹理压缩走 ASTC（现代安卓机全支持；全景贴图另有逐贴图覆盖）
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            // 出 APK 方便 adb 侧载调试；要上架 Google Play 再改 true（AAB）
            EditorUserBuildSettings.buildAppBundle = false;
        }

        /// <summary>
        /// 全景贴图 <see cref="PanoTexturePath"/> 的 maxTextureSize = 8192 是按 Windows 设的，
        /// **纹理导入设置分平台** —— 安卓这边不覆盖，默认 2048 会把 5120 宽的全景压糊。
        /// 顺手指定 ASTC 6x6（大小 / 质量均衡；ASTC 对任意尺寸都能压，不要求 4 的倍数）。
        /// 设过就不重设，避免每次打包都触发无谓的重导入。
        /// </summary>
        private static void OverridePanoTextureForAndroid()
        {
            var importer = AssetImporter.GetAtPath(PanoTexturePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("找不到全景贴图 " + PanoTexturePath + "，跳过安卓导入覆盖");
                return;
            }

            var settings = importer.GetPlatformTextureSettings("Android");
            if (settings.overridden && settings.maxTextureSize == 8192
                && settings.format == TextureImporterFormat.ASTC_6x6)
            {
                return;
            }

            settings.overridden = true;
            settings.maxTextureSize = 8192;
            settings.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();
            Debug.Log("[打包安卓] 已为全景贴图写入 Android 覆盖：8192 + ASTC_6x6");
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
