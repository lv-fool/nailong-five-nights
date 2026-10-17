using UnityEditor;
using UnityEngine;

namespace NailongFNAF.Editor
{
    /// <summary>
    /// 把运行时用 Shader.Find 拿的 shader 加进 Always Included Shaders。
    ///
    /// 背景：OfficeRoom 用 Shader.Find("NailongFNAF/UnlitTexture") 建圆柱全景材质。
    /// 自定义 shader 没被任何材质引用时，构建会被剔除 —— 发行版里 Shader.Find 返回
    /// null，办公室崩。加进 Always Included 保证进包。
    ///
    /// ⚠️ 不直接写 UnityEditor.GraphicsSettings.alwaysIncludedShaders —— 这个
    /// 中国版 2022.3 的编辑器程序集里该 API 有兼容性问题（CS0234），改用反射访问。
    /// </summary>
    public static class IncludeRuntimeShaders
    {
        [MenuItem("奶龙/9. 加入运行时 Shader（修发行版黑屏）")]
        public static void AddAlwaysIncludedShaders()
        {
            string[] names =
            {
                "NailongFNAF/UnlitTexture"
            };

            var gsType = System.Type.GetType(
                "UnityEditor.GraphicsSettings, UnityEditor.CoreModule");

            if (gsType == null)
            {
                Debug.LogWarning("[奶龙] 找不到 UnityEditor.GraphicsSettings 类型");
                return;
            }

            // 用反射读写 alwaysIncludedShaders（静态属性）
            var prop = gsType.GetProperty("alwaysIncludedShaders",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (prop == null)
            {
                Debug.LogWarning("[奶龙] GraphicsSettings 没有 alwaysIncludedShaders 属性");
                return;
            }

            var list = new System.Collections.Generic.List<Shader>(
                (Shader[])prop.GetValue(null));

            int added = 0;

            foreach (var name in names)
            {
                var shader = Shader.Find(name);

                if (shader == null)
                {
                    Debug.LogWarning("[奶龙] 找不到 shader: " + name);
                    continue;
                }

                if (!list.Contains(shader))
                {
                    list.Add(shader);
                    added++;
                }
            }

            prop.SetValue(null, list.ToArray());
            AssetDatabase.SaveAssets();
            Debug.Log("[奶龙] Always Included Shaders 加完，新增 " + added
                + " 个，共 " + list.Count + " 个");
        }
    }
}
