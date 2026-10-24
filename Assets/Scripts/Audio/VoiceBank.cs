using System.Collections.Generic;
using UnityEngine;

namespace NailongFNAF.Audio
{
    /// <summary>
    /// 台词配音库：`Assets/Resources/Voice/&lt;id&gt;.mp3`。
    ///
    /// 台词 id 由逻辑侧给出（`guide_3` / `call_early_2` / `apology_1` …），
    /// 文件由 `tools/tts_volc.py` 批量合成。**缺文件不算错误** —— 加载不到就返回 null，
    /// 游戏照旧只显示字幕，方便删掉音频、也方便只录一部分。
    ///
    /// 静态缓存：一句话在同一次运行里只读一次盘。
    /// </summary>
    public static class VoiceBank
    {
        /// <summary>Resources 下的子目录（末尾带斜杠，方便拼）。</summary>
        public const string ResourceRoot = "Voice/";

        private static readonly Dictionary<string, AudioClip> Cache =
            new Dictionary<string, AudioClip>();

        private static readonly HashSet<string> Misses = new HashSet<string>();

        /// <summary>取一句配音；没有就返回 null（不报错、不打日志刷屏）。</summary>
        public static AudioClip Get(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            AudioClip cached;
            if (Cache.TryGetValue(id, out cached))
            {
                return cached;
            }

            if (Misses.Contains(id))
            {
                return null;
            }

            AudioClip clip = Resources.Load<AudioClip>(ResourceRoot + id);

            if (clip == null)
            {
                Misses.Add(id);
                return null;
            }

            Cache[id] = clip;
            return clip;
        }

        /// <summary>这句有没有配音（会真的去加载一次）。</summary>
        public static bool Has(string id)
        {
            return Get(id) != null;
        }

        /// <summary>已经缓存的条数（调试/测试用）。</summary>
        public static int CachedCount
        {
            get { return Cache.Count; }
        }

        /// <summary>清缓存：编辑器里重新导入音频后用。</summary>
        public static void ClearCache()
        {
            Cache.Clear();
            Misses.Clear();
        }
    }
}
