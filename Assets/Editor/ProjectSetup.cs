using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 项目初始化工具。
    ///
    /// 装好 Unity 后打开本项目，菜单栏会出现「奶龙」一项，
    /// 依次执行下面三个命令即可把工程配好。
    ///
    /// 这个文件放在 Assets/Editor/ 下，属于编辑器专用程序集，
    /// 打包时不会进入游戏本体。
    /// </summary>
    public static class ProjectSetup
    {
        private const string CompanyName = "NailongStudio";
        private const string ProductName = "奶龙的五夜后宫";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        /// <summary>项目必需的全部资源目录。缺哪个补哪个。</summary>
        private static readonly string[] RequiredFolders =
        {
            "Assets/Art/Backgrounds",
            "Assets/Art/Characters",
            "Assets/Art/Monitoring",
            "Assets/Art/UI",
            "Assets/Audio/BGM",
            "Assets/Audio/SFX",
            "Assets/Audio/Ambient",
            "Assets/Prefabs",
            "Assets/Resources",
            "Assets/Scenes",
            "Assets/Scripts/Core",
            "Assets/Scripts/Gameplay",
            "Assets/Scripts/UI",
            "Assets/Scripts/Audio",
            "Assets/Shaders",
        };

        // ---------------------------------------------------------------
        // 1. 项目设置
        // ---------------------------------------------------------------

        [MenuItem("奶龙/1. 初始化项目设置", false, 10)]
        public static void ApplyPlayerSettings()
        {
            // 基础信息
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;

            // 窗口：默认 1920x1080 窗口化，方便调试；发布时再改全屏
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            // 恐怖游戏常常需要玩家切出去看攻略（笑），别在失焦时暂停
            PlayerSettings.runInBackground = true;

            // 2D 预渲染素材用 Gamma 更接近所见即所得
            PlayerSettings.colorSpace = ColorSpace.Gamma;

            // 静态图 + 热点交互，垂直同步就够了，不必跑满帧
            QualitySettings.vSyncCount = 1;

            AssetDatabase.SaveAssets();

            Debug.Log($"[奶龙] 项目设置已应用：{CompanyName} / {ProductName}，1920x1080 窗口化，Gamma 色彩空间。");
        }

        // ---------------------------------------------------------------
        // 2. 主场景骨架
        // ---------------------------------------------------------------

        [MenuItem("奶龙/2. 生成主场景骨架", false, 11)]
        public static void CreateMainScene()
        {
            // 当前场景有未保存改动时先问一句，避免误丢工作
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (File.Exists(MainScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "主场景已存在",
                    $"{MainScenePath} 已经存在，要覆盖重建吗？\n（已有的改动会丢失）",
                    "覆盖重建", "算了");
                if (!overwrite)
                {
                    return;
                }
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- 主相机：正交投影，黑色背景 ---
            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);

            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;               // 视野高度 10 个世界单位
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;      // 恐怖游戏的底：全黑
            cameraGo.AddComponent<AudioListener>();

            // --- UI 画布 ---
            var canvasGo = new GameObject("UICanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // 按 1920x1080 设计，等比缩放到任意分辨率
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // --- 事件系统 ---
            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            AddInputModule(eventSystemGo);

            // --- 目录 ---
            EnsureFolders();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, MainScenePath);

            // --- 写进 Build Settings，否则打包出来是黑屏 ---
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainScenePath, true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[奶龙] 主场景骨架已生成：{MainScenePath}（相机 + UI 画布 + 事件系统），并已加入 Build Settings。");
        }

        /// <summary>
        /// 挂输入模块。Unity 6 可能用新的 Input System，也可能用旧的 Input Manager，
        /// 这里动态判断，两种情况下都能跑起来。
        /// </summary>
        private static void AddInputModule(GameObject go)
        {
            var newModuleType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");

            if (newModuleType != null)
            {
                go.AddComponent(newModuleType);
            }
            else
            {
                go.AddComponent<StandaloneInputModule>();
            }
        }

        // ---------------------------------------------------------------
        // 3. 目录检查
        // ---------------------------------------------------------------

        [MenuItem("奶龙/3. 检查并补全目录", false, 12)]
        public static void CheckFolders()
        {
            int created = EnsureFolders();
            AssetDatabase.Refresh();

            Debug.Log(created == 0
                ? "[奶龙] 目录结构完整，无需补全。"
                : $"[奶龙] 补全了 {created} 个缺失目录。");
        }

        /// <summary>确保所有必需目录存在，返回新建的数量。</summary>
        private static int EnsureFolders()
        {
            int created = 0;

            foreach (string folder in RequiredFolders)
            {
                if (Directory.Exists(folder))
                {
                    continue;
                }

                Directory.CreateDirectory(folder);
                created++;
            }

            if (created > 0)
            {
                AssetDatabase.Refresh();
            }

            return created;
        }
    }
}
