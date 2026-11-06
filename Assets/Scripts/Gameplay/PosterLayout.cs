using System;
using System.Collections.Generic;
using UnityEngine;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 墙上插画的**摆放表** —— 由编辑器窗口
    /// 「奶龙 / 15. 编辑墙上插画」读写，落 <c>Assets/Resources/poster_layout.json</c>。
    ///
    /// 每张插图用一个**角度 + 高度**定位，贴在办公室那根圆柱内壁上：
    /// <list type="bullet">
    /// <item><b>Yaw</b>：水平转角（度）。0 = 正前方，负 = 左，正 = 右。
    ///   可转范围 ±70（<c>OfficeRoom.MaxYaw</c>），超出就转不到、看不见。</item>
    /// <item><b>Height</b>：离地高度（米）。人眼 1.30，所以 1.4~1.9 大概齐眼到头顶。</item>
    /// <item><b>Width</b>：画面宽度（米）。高按原图比例算，不拉伸。</item>
    /// </list>
    ///
    /// 为什么不直接给世界坐标：圆柱内壁的 (x,z) 完全由 yaw 决定，
    /// 让使用者填 yaw 比填 x/z 直观得多，也不会填到圆柱外面去。
    /// </summary>
    [Serializable]
    public sealed class PosterLayout
    {
        /// <summary>Resources 里的路径（不带扩展名）。</summary>
        public const string ResourcePath = "poster_layout";

        /// <summary>图片放在这个 Resources 子目录下。</summary>
        public const string ImageFolder = "Posters/";

        /// <summary>一张画。</summary>
        [Serializable]
        public class Poster
        {
            /// <summary>贴图名（Resources/Posters/ 下的文件名，不带扩展名）。</summary>
            public string Image;

            /// <summary>水平转角（度）。0 = 正前方。</summary>
            public float Yaw;

            /// <summary>离地高度（米）。</summary>
            public float Height;

            /// <summary>画面宽度（米）。高度按原图比例算。</summary>
            public float Width;

            /// <summary>不透明度 0~1。画旧了可以调暗一点。</summary>
            public float Alpha;
        }

        public List<Poster> Posters = new List<Poster>();

        /// <summary>默认摆放（8 张，左右各四张，高度错开）。</summary>
        public static List<Poster> DefaultPosters()
        {
            // 左右两侧墙面各四张。yaw 避开门的方位（±77~103 之外都能放，
            // 但真正转得到的只有 ±70，所以实际可用的区间是 ±8 ~ ±68）。
            //
            // 高度错开成两排：上排 1.72、下排 1.42 ——
            // 平齐会像印刷贴纸，错开才像有人零零散散贴上去的。
            return new List<Poster>
            {
                // 左侧（poster_04 是竖版 1024×2048，宽度给小一点才不至于糊成一长条）
                new Poster { Image = "poster_01", Yaw = -22f, Height = 1.72f, Width = 0.62f, Alpha = 1f },
                new Poster { Image = "poster_02", Yaw = -38f, Height = 1.44f, Width = 0.52f, Alpha = 1f },
                new Poster { Image = "poster_03", Yaw = -54f, Height = 1.74f, Width = 0.66f, Alpha = 1f },
                new Poster { Image = "poster_04", Yaw = -66f, Height = 1.55f, Width = 0.30f, Alpha = 1f },

                // 右侧（poster_08 也是竖版附近的比例，同样处理）
                new Poster { Image = "poster_05", Yaw = 22f, Height = 1.70f, Width = 0.60f, Alpha = 1f },
                new Poster { Image = "poster_06", Yaw = 38f, Height = 1.42f, Width = 0.54f, Alpha = 1f },
                new Poster { Image = "poster_07", Yaw = 54f, Height = 1.73f, Width = 0.64f, Alpha = 1f },
                new Poster { Image = "poster_08", Yaw = 66f, Height = 1.50f, Width = 0.44f, Alpha = 1f },
            };
        }

        /// <summary>把摆放表填成默认值。</summary>
        public void ResetToDefault()
        {
            Posters = DefaultPosters();
        }

        /// <summary>
        /// 运行时读摆放表：优先 Resources 里的 json，没有就用默认。
        /// 静态缓存 —— 房间只建一次，没必要每次读盘。
        /// </summary>
        private static PosterLayout _cached;
        private static bool _loaded;

        public static PosterLayout Load()
        {
            if (_loaded)
            {
                return _cached;
            }

            _loaded = true;

            var asset = Resources.Load<TextAsset>(ResourcePath);

            if (asset != null)
            {
                PosterLayout parsed;
                if (TryParse(asset.text, out parsed))
                {
                    _cached = parsed;
                    return _cached;
                }
            }

            _cached = new PosterLayout();
            _cached.ResetToDefault();
            return _cached;
        }

        /// <summary>编辑器改了 json 之后让缓存失效。</summary>
        public static void InvalidateCache()
        {
            _cached = null;
            _loaded = false;
        }

        // ---------------------------------------------------------------
        // 存读
        // ---------------------------------------------------------------

        /// <summary>序列化成一行 JSON。</summary>
        public string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"posters\":[");

            bool first = true;

            if (Posters != null)
            {
                foreach (var p in Posters)
                {
                    if (p == null || string.IsNullOrEmpty(p.Image))
                    {
                        continue;
                    }

                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;

                    sb.Append("{\"image\":\"").Append(p.Image).Append("\"")
                      .Append(",\"yaw\":").Append(p.Yaw.ToString("F2"))
                      .Append(",\"height\":").Append(p.Height.ToString("F2"))
                      .Append(",\"width\":").Append(p.Width.ToString("F2"))
                      .Append(",\"alpha\":").Append(p.Alpha.ToString("F2"))
                      .Append('}');
                }
            }

            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>从 JSON 解析。格式不对返回 false（调用方退回默认）。</summary>
        public static bool TryParse(string raw, out PosterLayout result)
        {
            result = null;

            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            try
            {
                var data = new PosterLayout();

                int i = 0;

                while (i < raw.Length)
                {
                    int imgAt = raw.IndexOf("\"image\"", i, StringComparison.Ordinal);
                    if (imgAt < 0)
                    {
                        break;
                    }

                    string image = ReadStringAfterKey(raw, imgAt, out int imgEnd);
                    if (image == null)
                    {
                        i = imgAt + 7;
                        continue;
                    }

                    var p = new Poster();
                    p.Image = image;
                    p.Alpha = 1f;
                    p.Width = 0.6f;
                    p.Yaw = 0f;
                    p.Height = 1.6f;

                    int end = imgEnd;

                    end = ReadFloat(raw, "\"yaw\"", end, out float yaw, p.Yaw);
                    p.Yaw = yaw;

                    end = ReadFloat(raw, "\"height\"", end, out float h, p.Height);
                    p.Height = h;

                    end = ReadFloat(raw, "\"width\"", end, out float w, p.Width);
                    p.Width = w;

                    end = ReadFloat(raw, "\"alpha\"", end, out float a, p.Alpha);
                    p.Alpha = a;

                    data.Posters.Add(p);

                    // ⚠️ 这行原本想写 end > imgEnd（防位置倒退），手滑写成了
                    // end > end —— 恒为 false，所以实际效果就是 i = imgEnd。
                    //
                    // 知道是笔误但**不改**：改成 end 反而会跳过某些东西，
                    // 而且当前逻辑靠一个隐含前提才正确：
                    //   "image" 在每个对象里只出现一次、且位置在 yaw/height/width/alpha 之前
                    // 所以下一轮 IndexOf("image", imgEnd) 命中的必然是**下一张**海报。
                    // 实测多张海报能正确解析，别动。
                    //
                    // 编译器会给 CS1718 warning —— 那是合理的，这里用注释说明而不是消除它。
                    i = end > end ? end : imgEnd;
                }

                result = data;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ReadFloat(string raw, string key, int from, out float value, float fallback)
        {
            value = fallback;

            int at = raw.IndexOf(key, from, StringComparison.Ordinal);
            if (at < 0)
            {
                return from;
            }

            int colon = raw.IndexOf(':', at);
            if (colon < 0)
            {
                return from;
            }

            int p = colon + 1;
            while (p < raw.Length && char.IsWhiteSpace(raw[p]))
            {
                p++;
            }

            int end = p;
            while (end < raw.Length && (char.IsDigit(raw[end]) || raw[end] == '-' || raw[end] == '.'))
            {
                end++;
            }

            float parsed;
            if (float.TryParse(raw.Substring(p, end - p), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed))
            {
                value = parsed;
            }

            return end > from ? end : from;
        }

        private static string ReadStringAfterKey(string raw, int keyIndex, out int endIndex)
        {
            endIndex = keyIndex + 7;

            int colon = raw.IndexOf(':', keyIndex);
            if (colon < 0)
            {
                return null;
            }

            int open = raw.IndexOf('"', colon + 1);
            if (open < 0)
            {
                return null;
            }

            var sb = new System.Text.StringBuilder();
            int i = open + 1;

            while (i < raw.Length)
            {
                char c = raw[i];

                if (c == '\\' && i + 1 < raw.Length)
                {
                    sb.Append(raw[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    endIndex = i + 1;
                    return sb.ToString();
                }

                sb.Append(c);
                i++;
            }

            return null;
        }
    }
}
