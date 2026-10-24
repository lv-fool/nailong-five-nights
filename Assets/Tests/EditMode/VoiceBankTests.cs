using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NailongFNAF.Audio;
using NUnit.Framework;
using UnityEngine;

namespace NailongFNAF.Tests
{
    /// <summary>
    /// 配音文件的「账」对得上不对得上。
    ///
    /// 台词文本在 <c>tools/voice_lines.json</c>，音频在 <c>Assets/Resources/Voice</c>。
    /// 改了词忘了重新合成、或者删了 mp3 忘了改 json，都会在这里红。
    /// 缺文件本身不算游戏崩（VoiceBank 返回 null、只显示字幕），但那是**事故**，不该静默。
    /// </summary>
    public sealed class VoiceBankTests
    {
        private static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        /// <summary>行首的 "键": —— 不引 JSON 库，够用且不加依赖。</summary>
        private const string KeyPattern = "^\\s*\"([^\"]+)\"";

        private static string VoiceDir
        {
            get { return Path.Combine(Application.dataPath, "Resources", "Voice"); }
        }

        [Test]
        public void 每句奶包台词都有配音文件()
        {
            string manifest = Path.Combine(ProjectRoot, "tools", "voice_lines.json");
            Assert.IsTrue(File.Exists(manifest), "找不到 tools/voice_lines.json");

            // 不引 JSON 库：这份文件是我们自己写的，键都在行首，正则足够且不增加依赖
            MatchCollection keys = Regex.Matches(File.ReadAllText(manifest), KeyPattern, RegexOptions.Multiline);
            int checkedIds = 0;

            foreach (Match m in keys)
            {
                string id = m.Groups[1].Value;
                if (id.StartsWith("_"))
                {
                    continue;
                }

                checkedIds++;

                string file = Path.Combine(VoiceDir, id + ".mp3");
                Assert.IsTrue(File.Exists(file),
                    "缺少配音：" + id + ".mp3（改了台词就重跑 tools/tts_volc.py）");
            }

            Assert.Greater(checkedIds, 20, "台词条数少得不对劲，json 是不是被截了？");
        }

        [Test]
        public void 配音文件都在Resources里能被加载到()
        {
            // 抽查几句最常出现的：引导第一句、道歉第一句、第一夜通话第一句
            Assert.IsNotNull(VoiceBank.Get("guide_1"), "guide_1 加载不到");
            Assert.IsNotNull(VoiceBank.Get("apology_1"), "apology_1 加载不到");
            Assert.IsNotNull(VoiceBank.Get("call_guide_first_1"), "call_guide_first_1 加载不到");
            Assert.Greater(VoiceBank.Get("guide_1").length, 1f, "guide_1 时长不对劲");
        }

        [Test]
        public void 没有的配音返回空而不是报错()
        {
            Assert.IsNull(VoiceBank.Get("没有这句"));
            Assert.IsNull(VoiceBank.Get(string.Empty));
            Assert.IsFalse(VoiceBank.Has("unknown_line_xyz"));
        }
    }
}
