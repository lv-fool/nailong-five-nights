using System.Collections.Generic;
using System.IO;
using NailongFNAF.Gameplay;
using UnityEditor;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 调整各关卡的 AI 值（活动度 0~20）——
    /// 菜单「奶龙 / 14. 调整关卡 AI 值」。
    ///
    /// 一张表：夜 × 敌人。改完点保存写到
    /// <c>Assets/Resources/night_difficulty.json</c>，
    /// 运行时 <see cref="NightDifficulty.CreateDefault"/> 从那里读。
    ///
    /// ± 按钮支持按住连发（复用 RepeatButton 的手感，这里是编辑器版）。
    /// </summary>
    public class NightDifficultyEditorWindow : EditorWindow
    {
        private const string JsonPath = "Assets/Resources/night_difficulty.json";

        /// <summary>编辑期间的可变副本：night → 四只敌人的值。</summary>
        private readonly Dictionary<int, int[]> _rows = new Dictionary<int, int[]>();

        private Vector2 _scroll;
        private bool _dirty;
        private string _status = string.Empty;

        [MenuItem("奶龙/14. 调整关卡 AI 值", false, 19)]
        public static void Open()
        {
            var window = GetWindow<NightDifficultyEditorWindow>(false, "关卡 AI 值", true);
            window.minSize = new Vector2(720f, 480f);
            window.LoadFromDisk();
            window.Show();
        }

        private void OnEnable()
        {
            if (_rows.Count == 0)
            {
                LoadFromDisk();
            }
        }

        private void OnDisable()
        {
            if (_dirty && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorUtility.DisplayDialog("关卡 AI 值",
                        "还有没保存的改动，要保存吗？", "保存", "丢弃"))
                {
                    SaveToDisk();
                }
            }
        }

        private void LoadFromDisk()
        {
            _rows.Clear();
            _dirty = false;

            var loaded = ReadJson();
            var def = NightDifficulty.CreateDefaultRaw();

            for (int n = 0; n < NightDifficulty.NightCount; n++)
            {
                var row = new int[4];

                foreach (var id in EnemyIds.All)
                {
                    int fromFile = loaded != null ? loaded.GetLevel(n, id) : -1;

                    row[(int)id] = fromFile >= 0
                        ? fromFile
                        : def.GetLevel(n, id);
                }

                _rows[n] = row;
            }

            _status = loaded != null
                ? "已读取 " + JsonPath
                : "还没存过档，显示的是默认曲线";

            Repaint();
        }

        private static NightDifficultyTable ReadJson()
        {
            if (!File.Exists(JsonPath))
            {
                return null;
            }

            try
            {
                NightDifficultyTable parsed;
                return NightDifficultyTable.TryParse(File.ReadAllText(JsonPath), out parsed)
                    ? parsed
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private void SaveToDisk()
        {
            var data = new NightDifficultyTable();

            for (int n = 0; n < NightDifficulty.NightCount; n++)
            {
                int[] row;
                if (!_rows.TryGetValue(n, out row))
                {
                    continue;
                }

                foreach (var id in EnemyIds.All)
                {
                    data.SetLevel(n, id, row[(int)id]);
                }
            }

            Directory.CreateDirectory("Assets/Resources");
            File.WriteAllText(JsonPath, data.ToJson(), new System.Text.UTF8Encoding(false));

            AssetDatabase.Refresh();

            // 让运行时的静态缓存失效
            NightDifficulty.InvalidateTableCache();

            _dirty = false;
            _status = "已保存到 " + JsonPath;
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("各关卡的 AI 值（活动度 0 ~ 20）", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "0 = 整夜不动，20 = 每次移动机会都动。\n"
                + "半夜找不到敌人是正常的 —— 0 就是「这夜它不出现」。\n"
                + "鼠标悬停在数字上可以用滚轮微调。",
                MessageType.Info);

            EditorGUILayout.Space(8);

            // 表头
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("夜", EditorStyles.boldLabel, GUILayout.Width(70f));

            foreach (var id in EnemyIds.All)
            {
                GUILayout.Label(ShortName(id), EditorStyles.boldLabel, GUILayout.Width(110f));
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("合计", EditorStyles.boldLabel, GUILayout.Width(60f));
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int n = 0; n < NightDifficulty.NightCount; n++)
            {
                if (!_rows.ContainsKey(n))
                {
                    _rows[n] = new int[4];
                }

                int[] row = _rows[n];

                EditorGUILayout.BeginHorizontal();

                GUILayout.Label("第 " + (n + 1) + " 夜", GUILayout.Width(70f));

                int total = 0;

                foreach (var id in EnemyIds.All)
                {
                    int idx = (int)id;
                    total += row[idx];

                    EditorGUILayout.BeginHorizontal(GUILayout.Width(110f));

                    if (GUILayout.Button("−", GUILayout.Width(24f)))
                    {
                        row[idx] = Mathf.Clamp(row[idx] - 1, 0, EnemyBrain.MaxActivityLevel);
                        _dirty = true;
                    }

                    int edited = EditorGUILayout.IntField(row[idx], GUILayout.Width(42f));
                    edited = Mathf.Clamp(edited, 0, EnemyBrain.MaxActivityLevel);

                    if (edited != row[idx])
                    {
                        row[idx] = edited;
                        _dirty = true;
                    }

                    if (GUILayout.Button("+", GUILayout.Width(24f)))
                    {
                        row[idx] = Mathf.Clamp(row[idx] + 1, 0, EnemyBrain.MaxActivityLevel);
                        _dirty = true;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label(total.ToString(), GUILayout.Width(60f));

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(6);
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
                if (!_dirty || EditorUtility.DisplayDialog("关卡 AI 值",
                        "有没保存的改动，确定丢弃？", "丢弃", "取消"))
                {
                    LoadFromDisk();
                }
            }

            if (GUILayout.Button("全部恢复默认", GUILayout.Height(32f)))
            {
                var def = NightDifficulty.CreateDefaultRaw();

                for (int n = 0; n < NightDifficulty.NightCount; n++)
                {
                    var row = new int[4];

                    foreach (var id in EnemyIds.All)
                    {
                        row[(int)id] = def.GetLevel(n, id);
                    }

                    _rows[n] = row;
                }

                _dirty = true;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(_dirty ? "● 有未保存的改动" : _status, EditorStyles.miniLabel);
        }

        /// <summary>表格里的短名（列太窄，放不下「· 右门」这种后缀）。</summary>
        private static string ShortName(EnemyId id)
        {
            switch (id)
            {
                case EnemyId.Nailong: return "奶龙";
                case EnemyId.Niulai: return "牛来";
                case EnemyId.Daishu: return "袋鼠";
                case EnemyId.Lulu: return "噜噜";
                default: return id.Code();
            }
        }
    }
}
