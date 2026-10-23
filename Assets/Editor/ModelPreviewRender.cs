using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 在 Unity 里把 Assets/Art/Models 下的模型渲成四视角 PNG。
    /// 目的是「用引擎自己的眼睛看一遍」：朝向、比例、材质颜色（含 sRGB 修正）
    /// 到底对不对，光看 Blender 的预览是看不出来的。
    ///
    /// 菜单：奶龙 / 14. 渲染模型预览（Unity 内）
    /// 无头：Unity.exe -batchmode -quit -projectPath . 
    ///        -executeMethod NailongFNAF.EditorTools.ModelPreviewRender.RunBatch
    /// 注意：不要加 -nographics，那样没有图形设备，RenderTexture 会是空的。
    /// </summary>
    public static class ModelPreviewRender
    {
        const string ModelRoot = "Assets/Art/Models";
        const string OutDir = ".workbuddy-ai/models";
        const int W = 560, H = 720;

        static readonly string[] Chars = { "nailong", "niulai", "daishu", "lulu" };

        // 与 Blender 侧 render_views 的取景一致：模型面朝 +Z，正面机位在 -Z
        static readonly (string name, float deg)[] Views =
        {
            ("front", 0f), ("q34", 35f), ("side", 90f), ("back", 180f),
        };

        [MenuItem("奶龙/14. 渲染模型预览（Unity 内）")]
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.34f, 0.34f);
            RenderSettings.skybox = null;

            var key = new GameObject("Key");
            var kl = key.AddComponent<Light>();
            kl.type = LightType.Directional;
            kl.intensity = 1.15f;
            kl.color = new Color(1f, 0.98f, 0.94f);
            key.transform.rotation = Quaternion.Euler(42f, 135f, 0f);

            var fill = new GameObject("Fill");
            var fl = fill.AddComponent<Light>();
            fl.type = LightType.Directional;
            fl.intensity = 0.45f;
            fl.color = new Color(0.85f, 0.9f, 1f);
            fill.transform.rotation = Quaternion.Euler(20f, -60f, 0f);

            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.42f, 0.42f, 1f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;

            // 灯挂到相机下面：每个视角都是「从机位侧上方打光」，
            // 否则背面视角全是黑的，看不出颜色对不对。
            key.transform.SetParent(cam.transform, false);
            key.transform.localRotation = Quaternion.Euler(38f, 24f, 0f);
            fill.transform.SetParent(cam.transform, false);
            fill.transform.localRotation = Quaternion.Euler(-6f, -52f, 0f);

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);

            int done = 0;
            foreach (var name in Chars)
            {
                var path = $"{ModelRoot}/{name}/{name}.fbx";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.LogWarning("[preview] 缺模型 " + path); continue; }

                // 注意：绝不能把 root 的 rotation 重置成 identity —— FBX 导入时
                // Z-up→Y-up 的转换就挂在这个 root 上，抹掉它模型就会躺下。
                var inst = Object.Instantiate(prefab);
                Debug.Log($"[preview] {name} root pos={inst.transform.position} rot={inst.transform.rotation.eulerAngles} scale={inst.transform.localScale}");

                var rends = inst.GetComponentsInChildren<Renderer>(true);
                if (rends.Length == 0) { Object.DestroyImmediate(inst); continue; }

                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);

                float radius = Mathf.Max(b.size.x, b.size.y, b.size.z) * 0.5f;
                cam.orthographicSize = radius * 1.12f;
                var center = b.center;

                foreach (var (vname, deg) in Views)
                {
                    float rad = deg * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(rad), 0f, -Mathf.Cos(rad));
                    cam.transform.position = center + dir * (radius * 6f + 2f);
                    cam.transform.LookAt(center, Vector3.up);

                    cam.targetTexture = rt;
                    cam.Render();

                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = null;

                    var outPath = $"{OutDir}/unity_{name}_{vname}.png";
                    File.WriteAllBytes(outPath, tex.EncodeToPNG());
                    done++;
                }

                Debug.Log($"[preview] {name} 包围盒 {b.size.x:F3} x {b.size.y:F3} x {b.size.z:F3} m " +
                          $"中心 {b.center.x:F3},{b.center.y:F3},{b.center.z:F3}");
                Object.DestroyImmediate(inst);
            }

            rt.Release();
            Object.DestroyImmediate(tex);
            Debug.Log($"[preview] 共渲染 {done} 张 -> {OutDir}");
        }

        /// <summary>-executeMethod 入口。</summary>
        public static void RunBatch()
        {
            try
            {
                Run();
                Debug.Log("PREVIEW: OK");
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError("PREVIEW: FAILED " + e);
                EditorApplication.Exit(2);
            }
        }
    }
}
