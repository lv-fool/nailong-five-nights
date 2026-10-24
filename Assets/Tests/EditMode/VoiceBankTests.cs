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
    /// 录音是一句一句来的（豆包网页朗读 + 环回录音，见 docs/04 的语音一节），
    /// 所以**允许只录了一部分**：没录的句子 VoiceBank 返回 null，游戏只显示字幕。
    /// 这里守的是三条线：
    ///   1. Voice 目录里的每个文件都得是 json 里有的台词 id（不许有野文件/打错名）；
    ///   2. 录了的文件都得能真的加载出来、时长正常；
    ///   3. 已经录好的那批核心台词不许凭空消失。
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

        private static HashSet<string> ManifestIds()
        {
            string manifest = Path.Combine(ProjectRoot, "tools", "voice_lines.json");
            Assert.IsTrue(File.Exists(manifest), "找不到 tools/voice_lines.json");

            HashSet<string> ids = new HashSet<string>();
            MatchCollection keys = Regex.Matches(File.ReadAllText(manifest), KeyPattern, RegexOptions.Multiline);
            foreach (Match m in keys)
            {
                string id = m.Groups[1].Value;
                if (!id.StartsWith("_"))
                {
                    ids.Add(id);
                }
            }

            Assert.Greater(ids.Count, 20, "台词条数少得不对劲，json 是不是被截了？");
            return ids;
        }

        [Test]
        public void 配音目录里不许有台词表里没有的文件()
        {
            HashSet<string> known = ManifestIds();
            string[] files = Directory.GetFiles(VoiceDir, "*.wav");
            Assert.Greater(files.Length, 10, "配音文件太少，路走歪了？");

            foreach (string file in files)
            {
                string id = Path.GetFileNameWithoutExtension(file);
                Assert.IsTrue(known.Contains(id),
                    "Voice 里有个 json 里没有的配音：" + id + ".wav（改名打错了？）");
            }
        }

        [Test]
        public void 录了的配音都能在Resources里加载到()
        {
            string[] files = Directory.GetFiles(VoiceDir, "*.wav");
            Assert.Greater(files.Length, 10, "配音文件太少，路走歪了？");

            foreach (string file in files)
            {
                string id = Path.GetFileNameWithoutExtension(file);
                AudioClip clip = VoiceBank.Get(id);
                Assert.IsNotNull(clip, id + ".wav 加载不到（Unity 没导入？看看是不是被 .meta 丢了）");
                Assert.Greater(clip.length, 0.5f, id + ".wav 时长太短，八成是空录音");
            }
        }

        [Test]
        public void 核心几路配音一句都不许少()
        {
            // 第一夜引导的五句、三句道歉、四条手欠、三句嘲讽 —— 这些是玩家最先听到的
            string[] required =
            {
                "guide_1", "guide_2", "guide_3", "guide_4", "guide_5",
                "apology_1", "apology_2",
                "usage_1", "usage_2", "monitor_1", "monitor_2", "lightoff_1", "lightoff_2",
                "taunt_1", "taunt_2", "taunt_3",
            };

            foreach (string id in required)
            {
                Assert.IsNotNull(VoiceBank.Get(id), "核心配音丢了：" + id);
            }

            Assert.Greater(VoiceBank.Get("guide_1").length, 1f, "guide_1 时长不对劲");
        }

        [Test]
        public void 还没有的配音返回空而不是报错()
        {
            // 手机通话与更多台词还没录：缺了就是静音，不许冒异常、不许刷日志
            Assert.IsNull(VoiceBank.Get("没有这句"));
            Assert.IsNull(VoiceBank.Get(string.Empty));
            Assert.IsFalse(VoiceBank.Has("unknown_line_xyz"));
        }
    }
}
