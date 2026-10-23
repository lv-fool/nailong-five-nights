using System.Collections.Generic;
using System.IO;
using NailongFNAF.Gameplay;
using UnityEditor;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 编辑跳杀后台词的窗口 —— 菜单「奶龙 / 11. 编辑跳杀台词」。
    ///
    /// 每只敌人一个**台词池**：可以加行、删行、逐行改、上下移动。
    /// 每次被抓从池里随机抽一句（运行时避开上一句，见 PrototypeScene.QuoteFor）。
    ///
    /// 改完点保存写到 <c>Assets/Resources/death_quotes.json</c>；
    /// 运行时的 PrototypeScene 从那里读，文件不存在就退回
    /// <see cref="DeathQuotes.DefaultPool"/> 的写死文案。
    /// </summary>
    public class DeathQuoteEditorWindow : EditorWindow
    {
        /// <summary>放 Resources 下，运行时才能 Resources.Load 到。</summary>
        private const string JsonPath = "Assets/Resources/death_quotes.json";

        /// <summary>每只敌人一个可变列表（编辑期间用）。</summary>
        private readonly Dictionary<EnemyId, List<string>> _pools =
            new Dictionary<EnemyId, List<string>>();

        private Vector2 _scroll;
        private bool _dirty;
        private string _status = string.Empty;

        [MenuItem("奶龙/11. 编辑跳杀台词", false, 16)]
        public static void Open()
        {
            var window = GetWindow<DeathQuoteEditorWindow>(false, "跳杀台词", true);
            window.minSize = new Vector2(680f, 560f);
            window.LoadFromDisk();
            window.Show();
        }

        private void OnEnable()
        {
            if (_pools.Count == 0)
            {
                LoadFromDisk();
            }
        }

        private void OnDisable()
        {
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
            _pools.Clear();
            _dirty = false;

            var loaded = ReadJson();

            foreach (var id in EnemyIds.All)
            {
                _pools[id] = loaded != null
                    ? loaded.PoolFor(id)
                    : new List<string>(DeathQuotes.DefaultPool(id));
            }

            _status = loaded != null
                ? "已读取 " + JsonPath
                : "还没存过档，显示的是默认台词";

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

            foreach (var id in EnemyIds.All)
            {
                List<string> lines;
                if (_pools.TryGetValue(id, out lines))
                {
                    data.SetPool(id, lines);
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

            EditorGUILayout.LabelField("跳杀后结算画面显示的那句话（每只一个随机池）", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "每次被抓从池里**随机抽一句**，并避开这一只上一句说过的话。\n"
                + "空行会被忽略；只有一句时只能重复。\n"
                + "调子建议保持「可爱反差」—— 不骂人，用撒娇 / 无辜的语气说可怕的话。\n\n"
                + "语音：每句旁边写着它该用的文件名（voice_<代号>_<序号>），"
                + "丢进 Assets/Resources/Audio/ 即可。也可以只放一条 "
                + "voice_<代号> 当这一只的通用录音。\n"
                + "格式：mp3 / wav / ogg，**或者 mp4 也行** ——"
                + "微信、QQ 导出的录音常是「mp4 容器装 AAC、扩展名却写 .mp3」，"
                + "那种会被 Unity 导成 VideoClip，程序走另一条路只取它的音轨。",
                MessageType.Info);

            EditorGUILayout.Space(8);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var id in EnemyIds.All)
            {
                if (!_pools.ContainsKey(id))
                {
                    _pools[id] = new List<string>();
                }

                List<string> pool = _pools[id];

                bool changed = false;

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField(
                    LabelFor(id) + "   （" + CountNonEmpty(pool) + " 句）",
                    EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("恢复默认", GUILayout.Width(90f)))
                {
                    _pools[id] = new List<string>(DeathQuotes.DefaultPool(id));
                    _dirty = true;
                    GUI.FocusControl(null);
                    changed = true;
                }

                if (GUILayout.Button("＋ 加一句", GUILayout.Width(90f)))
                {
                    pool.Add(string.Empty);
                    _dirty = true;
                    GUI.FocusControl(null);
                    changed = true;
                }

                EditorGUILayout.EndHorizontal();

                if (!changed)
                {
                    for (int i = 0; i < pool.Count; i++)
                    {
                        EditorGUILayout.BeginHorizontal();

                        // 序号 + **这句对应的语音文件名** ——
                        // 从这个窗口直接能看出该把录音命名成什么，
                        // 不用去翻代码里 PlayEndQuoteVoice 的规则。
                        GUILayout.Label((i + 1) + ".", GUILayout.Width(24f));

                        var nameStyle = new GUIStyle(EditorStyles.miniLabel);
                        nameStyle.normal.textColor = new Color(0.55f, 0.75f, 0.95f);
                        GUILayout.Label("voice_" + id.Code() + "_" + i, nameStyle,
                            GUILayout.Width(140f));

                        string current = pool[i] ?? string.Empty;

                        var style = new GUIStyle(EditorStyles.textField);
                        style.wordWrap = true;

                        string edited = EditorGUILayout.TextField(current, style);

                        if (edited != current)
                        {
                            pool[i] = edited;
                            _dirty = true;
                        }

                        if (GUILayout.Button("↑", GUILayout.Width(26f)) && i > 0)
                        {
                            string tmp = pool[i - 1];
                            pool[i - 1] = pool[i];
                            pool[i] = tmp;
                            _dirty = true;
                            GUI.FocusControl(null);
                            changed = true;
                        }
                        else if (GUILayout.Button("↓", GUILayout.Width(26f)) && i < pool.Count - 1)
                        {
                            string tmp = pool[i + 1];
                            pool[i + 1] = pool[i];
                            pool[i] = tmp;
                            _dirty = true;
                            GUI.FocusControl(null);
                            changed = true;
                        }
                        else if (GUILayout.Button("✕", GUILayout.Width(26f)))
                        {
                            pool.RemoveAt(i);
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

                if (pool.Count == 0)
                {
                    EditorGUILayout.LabelField("（空池 —— 这只被抓时不出声）", EditorStyles.miniLabel);
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
                    _pools[id] = new List<string>(DeathQuotes.DefaultPool(id));
                }

                _dirty = true;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(_dirty ? "● 有未保存的改动" : _status, EditorStyles.miniLabel);
        }

        /// <summary>池子里非空行数。</summary>
        private static int CountNonEmpty(List<string> pool)
        {
            if (pool == null)
            {
                return 0;
            }

            int n = 0;
            foreach (var line in pool)
            {
                if (!string.IsNullOrEmpty(line))
                {
                    n++;
                }
            }

            return n;
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
