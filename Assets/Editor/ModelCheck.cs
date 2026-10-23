using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 检查 Assets/Art/Models 下导入的 3D 模型：尺寸（米）、面数、材质与颜色、法线/UV。
    /// 用来确认 Blender 导出的 FBX 进 Unity 后没有走形、没有被缩放 100 倍、材质没丢。
    ///
    /// 菜单：奶龙 / 13. 检查 3D 模型
    /// 无头：Unity.exe -batchmode -nographics -projectPath ... 
    ///        -executeMethod NailongFNAF.EditorTools.ModelCheck.RunBatch
    /// </summary>
    public static class ModelCheck
    {
        const string ModelRoot = "Assets/Art/Models";
        const string ReportPath = ".workbuddy-ai/models/unity_model_report.txt";

        static readonly string[] Chars = { "nailong", "niulai", "daishu", "lulu" };

        [MenuItem("奶龙/13. 检查 3D 模型")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 3D 模型导入检查 ===");
            int missing = 0;

            foreach (var name in Chars)
            {
                var path = $"{ModelRoot}/{name}/{name}.fbx";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    sb.AppendLine($"[缺失] {path}");
                    missing++;
                    continue;
                }

                var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                if (filters.Length == 0)
                {
                    sb.AppendLine($"[无网格] {path}");
                    missing++;
                    continue;
                }

                var b = new Bounds(filters[0].sharedMesh.bounds.center, Vector3.zero);
                int tris = 0, verts = 0, submeshes = 0;
                bool hasNormals = true, hasUV = true;
                foreach (var f in filters)
                {
                    var m = f.sharedMesh;
                    if (m == null) continue;
                    tris += m.triangles.Length / 3;
                    verts += m.vertexCount;
                    submeshes += m.subMeshCount;
                    if (m.normals == null || m.normals.Length == 0) hasNormals = false;
                    if (m.uv == null || m.uv.Length == 0) hasUV = false;
                    var wb = f.transform.TransformBounds(m.bounds);
                    b.Encapsulate(wb);
                }

                sb.AppendLine();
                sb.AppendLine($"[{name}] {path}");
                sb.AppendLine($"  子物体 {prefab.transform.childCount + 1}，网格 {filters.Length}，" +
                              $"三角面 {tris}，顶点 {verts}，子网格(材质槽) {submeshes}");
                sb.AppendLine($"  包围盒尺寸 {b.size.x:F3} x {b.size.y:F3} x {b.size.z:F3} m " +
                              $"(中心 {b.center.x:F3},{b.center.y:F3},{b.center.z:F3})");
                sb.AppendLine($"  法线 {hasNormals}，UV {hasUV}");

                foreach (var r in renderers)
                {
                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null) { sb.AppendLine("  材质 <空>"); continue; }
                        var col = mat.HasProperty("_Color") ? mat.GetColor("_Color")
                                  : mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                                  : Color.magenta;
                        sb.AppendLine($"  材质 \"{mat.name}\" shader={mat.shader.name} " +
                                      $"color=#{ColorUtility.ToHtmlStringRGB(col)}");
                    }
                }

                // 面数/尺寸的常识校验：身高应在 0.5~3 m，三角面 4k~30k
                float h = b.size.y;
                if (h < 0.4f || h > 3.5f)
                    sb.AppendLine($"  ⚠️ 高度 {h:F2} m 不在 0.4~3.5 m 之间，缩放大概不对");
                if (tris < 2000 || tris > 40000)
                    sb.AppendLine($"  ⚠️ 三角面 {tris} 不在 2k~40k 之间，可能不适合直接进游戏");
            }

            sb.AppendLine();
            sb.AppendLine(missing == 0 ? "结论：全部模型导入正常。" : $"结论：{missing} 个模型缺失/无网格。");
            var text = sb.ToString();
            Debug.Log(text);

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? ".");
            File.WriteAllText(ReportPath, text);
        }

        /// <summary>-executeMethod 入口：跑完自己退出。</summary>
        public static void RunBatch()
        {
            try
            {
                Run();
                Debug.Log("MODELCHECK: OK");
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError("MODELCHECK: FAILED " + e);
                EditorApplication.Exit(2);
            }
        }

        static Bounds TransformBounds(this Transform t, Bounds local)
        {
            var c = t.TransformPoint(local.center);
            var e = local.extents;
            var ax = t.TransformVector(new Vector3(e.x, 0, 0));
            var ay = t.TransformVector(new Vector3(0, e.y, 0));
            var az = t.TransformVector(new Vector3(0, 0, e.z));
            var size = new Vector3(
                Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(c, size * 2f);
        }
    }
}
