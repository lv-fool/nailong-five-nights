using System.Collections.Generic;
using System.IO;
using NailongFNAF.Gameplay;
using UnityEditor;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 编辑**手机通话**里奶包说的台词 —— 菜单「奶龙 / 13. 编辑奶包通话台词」。
    ///
    /// 和「12. 编辑奶包台词」分开：那边是纯文本（引导 / 手欠 / 道歉），
    /// 这边每句还带一个**表情**（笑 / 撇嘴 / 坏笑 / 探身 / 垮脸），
    /// 因为手机屏上是**换网格**来表现表情的（AI 模型没有骨骼）。
    ///
    /// 数据落 <c>Assets/Resources/naibao_call_lines.json</c>。
    /// </summary>
    public class NaibaoCallLinesEditorWindow : EditorWindow
    {
        private const string JsonPath = "Assets/Resources/naibao_call_lines.json";

        /// <summary>编辑期间的可变副本：每句 = 文本 + 表情。</summary>
        private readonly Dictionary<string, List<NaibaoCallLines.Line>> _groups =
            new Dictionary<string, List<NaibaoCallLines.Line>>();

        private Vector2 _scroll;
        private bool _dirty;
        private string _status = string.Empty;

        [MenuItem("奶龙/13. 编辑奶包通话台词", false, 18)]
        public static void Open()
        {
            var window = GetWindow<NaibaoCallLinesEditorWindow>(false, "奶包通话台词", true);
            window.minSize = new Vector2(820f, 640f);
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
                if (EditorUtility.DisplayDialog("奶包通话台词",
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

            foreach (var key in NaibaoCallLines.Keys)
            {
                _groups[key] = loaded != null
                    ? loaded.LinesFor(key)
                    : new List<NaibaoCallLines.Line>(NaibaoCallLines.DefaultLines(key));
            }

            _status = loaded != null
                ? "已读取 " + JsonPath
                : "还没存过档，显示的是默认台词";

            Repaint();
        }

        private static NaibaoCallLines ReadJson()
        {
            if (!File.Exists(JsonPath))
            {
                return null;
            }

            try
            {
                NaibaoCallLines parsed;
                return NaibaoCallLines.TryParse(File.ReadAllText(JsonPath), out parsed) ? parsed : null;
            }
            catch
            {
                return null;
            }
        }

        private void SaveToDisk()
        {
            var data = new NaibaoCallLines();

            foreach (var key in NaibaoCallLines.Keys)
            {
                List<NaibaoCallLines.Line> lines;
                if (_groups.TryGetValue(key, out lines))
                {
                    data.SetGroup(key, lines);
                }
            }

            Directory.CreateDirectory("Assets/Resources");
            File.WriteAllText(JsonPath, data.ToJson(), new System.Text.UTF8Encoding(false));

            AssetDatabase.Refresh();

            NaibaoPhoneScript.InvalidateLinesCache();

            _dirty = false;
            _status = "已保存到 " + JsonPath;
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("手机通话里奶包说的台词（六组，每句带表情）", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "每组按顺序念完，念完后她挂断。\n"
                + "表情是**换整张脸**（AI 模型没有骨骼），所以每句要单独选一个。\n"
                + "空文本的行会被跳过，不会占掉一次通话。",
                MessageType.Info);

            EditorGUILayout.Space(8);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var key in NaibaoCallLines.Keys)
            {
                if (!_groups.ContainsKey(key))
                {
                    _groups[key] = new List<NaibaoCallLines.Line>();
                }

                List<NaibaoCallLines.Line> lines = _groups[key];
                bool changed = false;

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField(
                    NaibaoCallLines.TitleOf(key) + "   （" + CountNonEmpty(lines) + " 句）",
                    EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("恢复默认", GUILayout.Width(90f)))
                {
                    _groups[key] = new List<NaibaoCallLines.Line>(NaibaoCallLines.DefaultLines(key));
                    _dirty = true;
                    GUI.FocusControl(null);
                    changed = true;
                }

                if (GUILayout.Button("＋ 加一句", GUILayout.Width(90f)))
                {
                    lines.Add(new NaibaoCallLines.Line
                    {
                        Text = string.Empty,
                        Pose = NaibaoPose.Guide
                    });

                    _dirty = true;
                    GUI.FocusControl(null);
                    changed = true;
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(NaibaoCallLines.HintOf(key), EditorStyles.miniLabel);

                if (!changed)
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        NaibaoCallLines.Line line = lines[i];

                        if (line == null)
                        {
                            line = new NaibaoCallLines.Line { Pose = NaibaoPose.Guide };
                            lines[i] = line;
                        }

                        EditorGUILayout.BeginHorizontal();

                        GUILayout.Label((i + 1) + ".", GUILayout.Width(24f));

                        string current = line.Text ?? string.Empty;
                        string edited = EditorGUILayout.TextArea(current,
                            GUILayout.MinHeight(52f));

                        if (edited != current)
                        {
                            line.Text = edited;
                            _dirty = true;
                        }

                        // 表情下拉
                        int poseIndex = IndexOfPose(line.Pose);
                        int newIndex = EditorGUILayout.Popup(poseIndex, PoseLabels,
                            GUILayout.Width(140f));

                        if (newIndex != poseIndex)
                        {
                            line.Pose = NaibaoCallLines.AllPoses[newIndex];
                            _dirty = true;
                        }

                        if (GUILayout.Button("↑", GUILayout.Width(26f)) && i > 0)
                        {
                            NaibaoCallLines.Line tmp = lines[i - 1];
                            lines[i - 1] = lines[i];
                            lines[i] = tmp;
                            _dirty = true;
                            GUI.FocusControl(null);
                            changed = true;
                        }
                        else if (GUILayout.Button("↓", GUILayout.Width(26f)) && i < lines.Count - 1)
                        {
                            NaibaoCallLines.Line tmp = lines[i + 1];
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
                    EditorGUILayout.LabelField("（空组 —— 这一通她不说话）", EditorStyles.miniLabel);
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
                if (!_dirty || EditorUtility.DisplayDialog("奶包通话台词",
                        "有没保存的改动，确定丢弃？", "丢弃", "取消"))
                {
                    LoadFromDisk();
                }
            }

            if (GUILayout.Button("全部恢复默认", GUILayout.Height(32f)))
            {
                foreach (var key in NaibaoCallLines.Keys)
                {
                    _groups[key] = new List<NaibaoCallLines.Line>(
                        NaibaoCallLines.DefaultLines(key));
                }

                _dirty = true;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(_dirty ? "● 有未保存的改动" : _status, EditorStyles.miniLabel);
        }

        /// <summary>表情下拉框的显示文字（只算一次）。</summary>
        private static string[] _poseLabels;

        private static string[] PoseLabels
        {
            get
            {
                if (_poseLabels == null)
                {
                    var list = new List<string>();
                    foreach (var p in NaibaoCallLines.AllPoses)
                    {
                        list.Add(NaibaoCallLines.PoseName(p));
                    }

                    _poseLabels = list.ToArray();
                }

                return _poseLabels;
            }
        }

        /// <summary>表情在 AllPoses 里的下标（找不到给 0）。</summary>
        private static int IndexOfPose(NaibaoPose pose)
        {
            var all = NaibaoCallLines.AllPoses;

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == pose)
                {
                    return i;
                }
            }

            return 0;
        }

        private static int CountNonEmpty(List<NaibaoCallLines.Line> lines)
        {
            if (lines == null)
            {
                return 0;
            }

            int n = 0;
            foreach (var line in lines)
            {
                if (line != null && !string.IsNullOrEmpty(line.Text))
                {
                    n++;
                }
            }

            return n;
        }
    }
}
