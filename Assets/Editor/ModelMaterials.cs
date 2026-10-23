using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 把 FBX 里带的材质颜色改成「参考图上的颜色」。
    ///
    /// 为什么要这一步：
    /// 1. Blender 的 Principled Base Color 是**线性**值，FBX 按线性存；而 Unity 的
    ///    Standard 材质 _Color 在 Linear 色彩空间下也是按线性解释的 —— 这条链本身没错，
    ///    但 Blender 里我填的是「看着像」的 sRGB 十六进制，经过两边的色彩管理很容易偏。
    /// 2. 最省事的做法就是：颜色只在这里定义一次（sRGB 十六进制，肉眼可核对），
    ///    代码里显式做 sRGB→线性，Unity 里看到的就是这个色。
    ///
    /// 菜单：奶龙 / 15. 修正模型材质颜色
    /// 无头：-executeMethod NailongFNAF.EditorTools.ModelMaterials.Apply
    /// </summary>
    public static class ModelMaterials
    {
        static readonly string[] Chars = { "nailong", "niulai", "daishu", "lulu" };

        // 材质名 -> (sRGB 十六进制, 光滑度)
        static readonly Dictionary<string, (string hex, float smooth)> Table =
            new Dictionary<string, (string, float)>
            {
                // 奶龙：黄身体 + 奶油白肚皮 + 深橄榄手脚 + 青绿眼
                { "nailong_body",   ("#F2CE4E", 0.30f) },
                { "nailong_belly",  ("#FBF4DC", 0.22f) },
                { "nailong_paw",    ("#6B6347", 0.32f) },
                { "nailong_sclera", ("#F7F8F0", 0.55f) },
                { "nailong_iris",   ("#8ED3B2", 0.62f) },
                { "nailong_pupil",  ("#111110", 0.70f) },
                { "nailong_mouth",  ("#4A3A28", 0.10f) },

                // 牛来：金黄毛 + 淡紫口鼻/手脚 + 深角
                { "niulai_fur",   ("#EFC242", 0.28f) },
                { "niulai_muzzle",("#C9B0C4", 0.35f) },
                { "niulai_horn",  ("#6E5E4A", 0.30f) },
                { "niulai_eye",   ("#2A2118", 0.65f) },

                // 袋鼠：亮黄小狗 + 深棕鼻头
                { "daishu_body",   ("#F5CC3C", 0.28f) },
                { "daishu_nose",   ("#8A3E17", 0.45f) },
                { "daishu_sclera", ("#FBFBF2", 0.55f) },
                { "daishu_pupil",  ("#3A2A18", 0.65f) },

                // 噜噜：黄身 + 橘口鼻 + 橘短裤 + 南瓜
                { "lulu_body",   ("#F0D071", 0.28f) },
                { "lulu_muzzle", ("#F79A16", 0.30f) },
                { "lulu_shorts", ("#C4794A", 0.30f) },
                { "lulu_pumpkin",("#F08A12", 0.35f) },
                { "lulu_stem",   ("#6F8C31", 0.30f) },
                { "lulu_eye",    ("#4A3A22", 0.65f) },
            };

        [MenuItem("奶龙/15. 修正模型材质颜色")]
        public static void Apply()
        {
            int touched = 0, missing = 0;

            foreach (var name in Chars)
            {
                var path = $"Assets/Art/Models/{name}/{name}.fbx";
                var objs = AssetDatabase.LoadAllAssetsAtPath(path);
                if (objs == null || objs.Length == 0) continue;

                foreach (var o in objs)
                {
                    if (!(o is Material m)) continue;
                    if (!Table.TryGetValue(m.name, out var e)) continue;

                    if (!ColorUtility.TryParseHtmlString(e.hex, out var srgb))
                    {
                        Debug.LogWarning($"[mat] 颜色解析失败 {m.name} {e.hex}");
                        continue;
                    }

                    var lin = new Color(SrgbToLinear(srgb.r), SrgbToLinear(srgb.g), SrgbToLinear(srgb.b), 1f);
                    m.SetColor("_Color", lin);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", e.smooth);
                    m.SetFloat("_Metallic", 0f);
                    EditorUtility.SetDirty(m);
                    touched++;
                }
            }

            foreach (var kv in Table)
            {
                bool found = false;
                foreach (var name in Chars)
                {
                    var path = $"Assets/Art/Models/{name}/{name}.fbx";
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (o is Material m && m.name == kv.Key) { found = true; break; }
                    if (found) break;
                }
                if (!found) missing++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[mat] 已改 {touched} 个材质，表里还有 {missing} 个没对上（模型还没建就先忽略）");
        }

        /// <summary>sRGB → 线性（IEC 61966-2-1）。</summary>
        static float SrgbToLinear(float v)
        {
            v = Mathf.Clamp01(v);
            return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        public static void ApplyBatch()
        {
            try { Apply(); Debug.Log("MATERIALS: OK"); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogError("MATERIALS: FAILED " + e); EditorApplication.Exit(2); }
        }
    }
}
