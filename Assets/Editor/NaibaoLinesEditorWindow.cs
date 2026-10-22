using System.Collections.Generic;
using System.IO;
using NailongFNAF.Gameplay;
using UnityEditor;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 编辑奶包**所有**台词的窗口 —— 菜单「奶龙 / 12. 编辑奶包台词」。
    ///
    /// 六组：第 1 夜引导 / 偷偷加耗电 / 抢监控 / 关你的灯 / 纯嘲讽 / 道歉。
    /// 每组可加行、删行、逐行改、上下移动。改完点保存写到
    /// <c>Assets/Resources/naibao_lines.json</c>，并让运行时缓存失效。
    ///
    /// ⚠️ 手机通话那六组（带表情的）不在这里 —— 它们结构不同，
    /// 仍留在 NaibaoPhoneScript 里。
    /// </summary>
    public class NaibaoLinesEditorWindow : EditorWindow
    {
        /// <summary>放 Resources 下，运行时才能 Resources.Load 到。</summary>
        private const string JsonPath = "Assets/Resources/naibao_lines.json";

        private readonly Dictionary<string, List<string>> _groups =
            new Dictionary<string, List<string>>();

        private Vector2 _scroll;
        private bool _dirty;
        private string _status = string.Empty;

        [MenuItem("奶龙/12. 编辑奶包台词", false, 17)]
        public static void Open()
        {
            var window = GetWindow<NaibaoLinesEditorWindow>(false, "奶包台词", true);
            window.minSize = new Vector2(760f, 620f);
            window.LoadFromDisk();
            window.Show();
        }

        private void OnEnable()
        {
            if (_groups.Count == 0)
            {
                LoadFromDisk();
            }
        }

        private void OnDisable()
        {
            if (_dirty && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorUtility.DisplayDialog("奶包台词",
                        "还有没保存的改动，要保存吗？", "保存", "丢弃"))
                {
                    SaveToDisk();
                }
            }
        }

        private void LoadFromDisk()
        {
            _groups.Clear();
            _dirty = false;

            var loaded = ReadJson();

            foreach (var key in NaibaoLines.Keys)
            {
                _groups[key] = loaded != null
                    ? loaded.LinesFor(key)
                    : new List<string>(NaibaoLines.DefaultLines(key));
            }

            _status = loaded != null
                ? "已读取 " + JsonPath
                : "还没存过档，显示的是默认台词";

            Repaint();
        }

        private static NaibaoLines ReadJson()
        {
            if (!File.Exists(JsonPath))
            {
                return null;
            }

            try
            {
                NaibaoLines parsed;
                return NaibaoLines.TryParse(File.ReadAllText(JsonPath), out parsed) ? parsed : null;
            }
            catch
            {
                return null;
            }
        }

        private void SaveToDisk()
        {
            var data = new NaibaoLines();

            foreach (var key in NaibaoLines.Keys)
            {
                List<string> lines;
                if (_groups.TryGetValue(key, out lines))
                {
                    data.SetGroup(key, lines);
                }
            }

            Directory.CreateDirectory("Assets/Resources");
            File.WriteAllText(JsonPath, data.ToJson(), new System.Text.UTF8Encoding(false));

            AssetDatabase.Refresh();

            // 让运行时的静态缓存失效，免得这一局还读着旧台词
            NaibaoDirector.InvalidateLinesCache();

            _dirty = false;
            _status = "已保存到 " + JsonPath;
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("奶包说过的所有话（六组）", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "每组可加行 / 删行 / 逐行改 / 上下移动。\n"
                + "「引导」和「道歉」两组是**按顺序取**的，别乱删乱调 —— "
                + "引导五句对应 18/72/132/200/274 秒，道歉三条对应抢监控/关灯/偷加耗电。\n"
                + "其余四组每次出手随机抽一句。",
                MessageType.Info);

            EditorGUILayout.Space(8);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var key in NaibaoLines.Keys)
            {
                if (!_groups.ContainsKey(key))
                {
                    _groups[key] = new List<string>();
                }

                List<string> lines = _groups[key];
                bool changed = false;

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField(
                    NaibaoLines.TitleOf(key) + "   （" + CountNonEmpty(lines) + " 句）",
                    EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("恢复默认", GUILayout.Width(90f)))
                {
                    _groups[key] = new List<string>(NaibaoLines.DefaultLines(key));
                    _dirty = true;
                    GUI.FocusControl(null);
                    changed = true;
                }

                if (GUILayout.Button("＋ 加一句", GUILayout.Width(90f)))
                {
                    lines.Add(string.Empty);
                    _dirty = true;
                    GUI.FocusControl(null);
                    changed = true;
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(NaibaoLines.HintOf(key), EditorStyles.miniLabel);

                if (!changed)
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        EditorGUILayout.BeginHorizontal();

                        GUILayout.Label((i + 1) + ".", GUILayout.Width(24f));

                        string current = lines[i] ?? string.Empty;

                        var style = new GUIStyle(EditorStyles.textArea);
                        style.wordWrap = true;

                        string edited = EditorGUILayout.TextArea(current, style,
                            GUILayout.MinHeight(40f));

                        if (edited != current)
                        {
                            lines[i] = edited;
                            _dirty = true;
                        }

                        if (GUILayout.Button("↑", GUILayout.Width(26f)) && i > 0)
                        {
                            string tmp = lines[i - 1];
                            lines[i - 1] = lines[i];
                            lines[i] = tmp;
                            _dirty = true;
                            GUI.FocusControl(null);
                            changed = true;
                        }
                        else if (GUILayout.Button("↓", GUILayout.Width(26f)) && i < lines.Count - 1)
                        {
                            string tmp = lines[i + 1];
                            lines[i + 1] = lines[i];
                            lines[i] = tmp;
                            _dirty = true;
                            GUI.FocusControl(null);
                            changed = true;
                        }
                        else if (GUILayout.Button("✕", GUILayout.Width(26f)))
                        {
                            lines.RemoveAt(i);
                            _dirty = true;
                            GUI.FocusControl(null);
                            changed = true;
                        }

                        EditorGUILayout.EndHorizontal();

                        if (changed)
                        {
                            break;
                        }
                    }
                }

                if (lines.Count == 0)
                {
                    EditorGUILayout.LabelField("（空组 —— 这组她不出声）", EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(12);
                EditorGUILayout.LabelField(string.Empty, GUI.skin.horizontalSlider);
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = _dirty;

            if (GUILayout.Button("保存", GUILayout.Height(32f)))
            {
                SaveToDisk();
            }

            GUI.enabled = true;

            if (GUILayout.Button("重新读取", GUILayout.Height(32f)))
            {
                if (!_dirty || EditorUtility.DisplayDialog("奶包台词",
                        "有没保存的改动，确定丢弃？", "丢弃", "取消"))
                {
                    LoadFromDisk();
                }
            }

            if (GUILayout.Button("全部恢复默认", GUILayout.Height(32f)))
            {
                foreach (var key in NaibaoLines.Keys)
                {
                    _groups[key] = new List<string>(NaibaoLines.DefaultLines(key));
                }

                _dirty = true;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(_dirty ? "● 有未保存的改动" : _status, EditorStyles.miniLabel);
        }

        private static int CountNonEmpty(List<string> lines)
        {
            if (lines == null)
            {
                return 0;
            }

            int n = 0;
            foreach (var line in lines)
            {
                if (!string.IsNullOrEmpty(line))
                {
                    n++;
                }
            }

            return n;
        }
    }
}
