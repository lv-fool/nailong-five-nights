using System.Collections.Generic;
using System.IO;
using NailongFNAF.Gameplay;
using UnityEditor;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 编辑跳杀后那句台词的窗口 —— 菜单「奶龙 / 11. 编辑跳杀台词」。
    ///
    /// 每只敌人一个输入框，改完点保存写到 <c>Assets/Resources/death_quotes.json</c>。
    /// 运行时 <see cref="NailongFNAF.UI.PrototypeScene"/> 从那里读，
    /// 文件不存在就退回 <see cref="DeathQuotes.DefaultFor"/> 的写死文案。
    ///
    /// 为什么做成窗口而不是直接改源码：台词是文案，改一个词不该逼人
    /// 在 .cs 里翻半天、还容易和别人的改动撞在一起。
    /// </summary>
    public class DeathQuoteEditorWindow : EditorWindow
    {
        /// <summary>放 Resources 下，运行时才能 Resources.Load 到。</summary>
        private const string JsonPath = "Assets/Resources/death_quotes.json";

        private readonly Dictionary<EnemyId, string> _texts = new Dictionary<EnemyId, string>();

        private Vector2 _scroll;
        private bool _dirty;
        private string _status = string.Empty;

        [MenuItem("奶龙/11. 编辑跳杀台词", false, 16)]
        public static void Open()
        {
            var window = GetWindow<DeathQuoteEditorWindow>(false, "跳杀台词", true);
            window.minSize = new Vector2(620f, 480f);
            window.LoadFromDisk();
            window.Show();
        }

        private void OnEnable()
        {
            if (_texts.Count == 0)
            {
                LoadFromDisk();
            }
        }

        private void OnDisable()
        {
            // 关窗口时如果还有没保存的改动，问一句 —— 免得白改
            if (_dirty && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorUtility.DisplayDialog("跳杀台词",
                        "还有没保存的改动，要保存吗？", "保存", "丢弃"))
                {
                    SaveToDisk();
                }
            }
        }

        private void LoadFromDisk()
        {
            _texts.Clear();
            _dirty = false;

            var loaded = ReadJson();

            foreach (var id in EnemyIds.All)
            {
                _texts[id] = loaded != null
                    ? loaded.For(id)
                    : DeathQuotes.DefaultFor(id);
            }

            _status = loaded != null ? "已读取 " + JsonPath : "还没存过档，显示的是默认台词";
            Repaint();
        }

        private static DeathQuotes ReadJson()
        {
            if (!File.Exists(JsonPath))
            {
                return null;
            }

            try
            {
                DeathQuotes parsed;
                return DeathQuotes.TryParse(File.ReadAllText(JsonPath), out parsed) ? parsed : null;
            }
            catch
            {
                return null;
            }
        }

        private void SaveToDisk()
        {
            var data = new DeathQuotes();
            data.ResetToDefaults();   // 先把四条都填上，再覆盖

            foreach (var id in EnemyIds.All)
            {
                string text;
                if (_texts.TryGetValue(id, out text))
                {
                    data.Set(id, text);
                }
            }

            Directory.CreateDirectory("Assets/Resources");
            File.WriteAllText(JsonPath, data.ToJson(), new System.Text.UTF8Encoding(false));

            AssetDatabase.Refresh();

            _dirty = false;
            _status = "已保存到 " + JsonPath;
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("跳杀后结算画面显示的那句话", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "调子建议保持「可爱反差」—— 不骂人，用撒娇 / 无辜的语气说可怕的话。\n"
                + "留空表示这一夜用默认文案。改完记得点保存。",
                MessageType.Info);

            EditorGUILayout.Space(8);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var id in EnemyIds.All)
            {
                EditorGUILayout.LabelField(LabelFor(id), EditorStyles.boldLabel);

                string current = _texts.ContainsKey(id) ? _texts[id] : string.Empty;

                var style = new GUIStyle(EditorStyles.textArea);
                style.wordWrap = true;

                string edited = EditorGUILayout.TextArea(current, style,
                    GUILayout.MinHeight(52f));

                if (edited != current)
                {
                    _texts[id] = edited;
                    _dirty = true;
                }

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("恢复默认", GUILayout.Width(90f)))
                {
                    _texts[id] = DeathQuotes.DefaultFor(id);
                    _dirty = true;
                    GUI.FocusControl(null);
                }

                GUILayout.Label("默认：" + DeathQuotes.DefaultFor(id),
                    EditorStyles.miniLabel);

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(10);
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
                if (!_dirty || EditorUtility.DisplayDialog("跳杀台词",
                        "有没保存的改动，确定丢弃？", "丢弃", "取消"))
                {
                    LoadFromDisk();
                }
            }

            if (GUILayout.Button("全部恢复默认", GUILayout.Height(32f)))
            {
                foreach (var id in EnemyIds.All)
                {
                    _texts[id] = DeathQuotes.DefaultFor(id);
                }

                _dirty = true;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(_dirty ? "● 有未保存的改动" : _status,
                EditorStyles.miniLabel);
        }

        private static string LabelFor(EnemyId id)
        {
            switch (id)
            {
                case EnemyId.Nailong:
                    return "奶龙（断电收电费 / 偷电）";

                case EnemyId.Niulai:
                    return "牛来（右门）";

                case EnemyId.Daishu:
                    return "袋鼠（左门）";

                case EnemyId.Lulu:
                    return "噜噜（正门）";

                default:
                    return id.Code();
            }
        }
    }
}
