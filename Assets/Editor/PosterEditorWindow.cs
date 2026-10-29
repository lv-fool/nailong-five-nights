using System.Collections.Generic;
using System.IO;
using NailongFNAF.Gameplay;
using UnityEditor;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 编辑墙上的插画 —— 菜单「奶龙 / 15. 编辑墙上插画」。
    ///
    /// 每张画用一个**角度 + 高度 + 宽度**定位，贴在办公室那根圆柱内壁上。
    /// 角度 ±70 之外转不到（那是相机限位），所以界面上会提示。
    ///
    /// 数据落 <c>Assets/Resources/poster_layout.json</c>，
    /// 图片放 <c>Assets/Resources/Posters/</c>（文件名不带扩展名写进 json）。
    /// </summary>
    public class PosterEditorWindow : EditorWindow
    {
        private const string JsonPath = "Assets/Resources/poster_layout.json";
        private const string ImageDir = "Assets/Resources/Posters";

        /// <summary>相机能转到的最大角度 —— 超过这个值画就永远看不见。</summary>
        private const float VisibleYaw = 70f;

        private PosterLayout _layout;

        private Vector2 _scroll;
        private bool _dirty;
        private string _status = string.Empty;

        /// <summary>Resources/Posters 下的所有贴图名（下拉框用）。</summary>
        private string[] _availableImages = new string[0];

        [MenuItem("奶龙/15. 编辑墙上插画", false, 20)]
        public static void Open()
        {
            var window = GetWindow<PosterEditorWindow>(false, "墙上插画", true);
            window.minSize = new Vector2(760f, 520f);
            window.LoadFromDisk();
            window.Show();
        }

        private void OnEnable()
        {
            if (_layout == null)
            {
                LoadFromDisk();
            }
        }

        private void OnDisable()
        {
            if (_dirty && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorUtility.DisplayDialog("墙上插画",
                        "还有没保存的改动，要保存吗？", "保存", "丢弃"))
                {
                    SaveToDisk();
                }
            }
        }

        private void LoadFromDisk()
        {
            _dirty = false;

            _layout = ReadJson();

            if (_layout == null)
            {
                _layout = new PosterLayout();
                _layout.ResetToDefault();
                _status = "还没存过档，显示的是默认摆放";
            }
            else
            {
                _status = "已读取 " + JsonPath;
            }

            RefreshImageList();
            Repaint();
        }

        /// <summary>扫一遍 Resources/Posters，做下拉框的选项。</summary>
        private void RefreshImageList()
        {
            var names = new List<string>();

            if (Directory.Exists(ImageDir))
            {
                foreach (var path in Directory.GetFiles(ImageDir))
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();

                    if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                    {
                        names.Add(Path.GetFileNameWithoutExtension(path));
                    }
                }
            }

            names.Sort();
            _availableImages = names.ToArray();
        }

        private static PosterLayout ReadJson()
        {
            if (!File.Exists(JsonPath))
            {
                return null;
            }

            try
            {
                PosterLayout parsed;
                return PosterLayout.TryParse(File.ReadAllText(JsonPath), out parsed)
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
            Directory.CreateDirectory("Assets/Resources");
            File.WriteAllText(JsonPath, _layout.ToJson(), new System.Text.UTF8Encoding(false));

            AssetDatabase.Refresh();

            // 让运行时缓存失效（下次进 Play 才会读到新值，这里只是保险）
            PosterLayout.InvalidateCache();

            _dirty = false;
            _status = "已保存到 " + JsonPath + " —— 进 Play 生效";
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("墙上的插画", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "角度 0 = 正前方，负 = 左，正 = 右。相机限位 ±" + VisibleYaw + "°，\n"
                + "超出这一范围的画**永远转不到**，界面上会标红。\n"
                + "高度是离地米数（人眼 1.30），宽度是画面宽度（高度按原图比例自动算）。\n"
                + "图片放 Assets/Resources/Posters/，改完点保存，**进 Play 生效**。",
                MessageType.Info);

            EditorGUILayout.Space(8);

            // 表头
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("贴图", EditorStyles.boldLabel, GUILayout.Width(180f));
            GUILayout.Label("角度", EditorStyles.boldLabel, GUILayout.Width(90f));
            GUILayout.Label("高度", EditorStyles.boldLabel, GUILayout.Width(90f));
            GUILayout.Label("宽度", EditorStyles.boldLabel, GUILayout.Width(90f));
            GUILayout.Label("透明度", EditorStyles.boldLabel, GUILayout.Width(90f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (_layout.Posters == null)
            {
                _layout.Posters = new List<PosterLayout.Poster>();
            }

            for (int i = 0; i < _layout.Posters.Count; i++)
            {
                var p = _layout.Posters[i];

                if (p == null)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();

                // 贴图下拉（包含「当前值不在列表里」的情况）
                int currentIdx = System.Array.IndexOf(_availableImages, p.Image);

                string[] options = _availableImages.Length > 0
                    ? _availableImages
                    : new[] { p.Image };

                int showIdx = currentIdx >= 0 ? currentIdx : 0;

                int picked = EditorGUILayout.Popup(showIdx, options, GUILayout.Width(180f));

                if (picked != showIdx && _availableImages.Length > 0)
                {
                    p.Image = _availableImages[picked];
                    _dirty = true;
                }

                // 角度
                float yaw = EditorGUILayout.FloatField(p.Yaw, GUILayout.Width(90f));

                if (!Mathf.Approximately(yaw, p.Yaw))
                {
                    p.Yaw = yaw;
                    _dirty = true;
                }

                // 超范围标红
                if (Mathf.Abs(p.Yaw) > VisibleYaw)
                {
                    var style = new GUIStyle(EditorStyles.miniLabel);
                    style.normal.textColor = new Color(1f, 0.4f, 0.35f);
                    GUILayout.Label("← 转不到", style, GUILayout.Width(60f));
                }
                else
                {
                    GUILayout.Label(string.Empty, GUILayout.Width(60f));
                }

                // 高度
                float h = EditorGUILayout.FloatField(p.Height, GUILayout.Width(90f));

                if (!Mathf.Approximately(h, p.Height))
                {
                    p.Height = h;
                    _dirty = true;
                }

                // 宽度
                float w = EditorGUILayout.FloatField(p.Width, GUILayout.Width(90f));

                if (!Mathf.Approximately(w, p.Width))
                {
                    p.Width = w;
                    _dirty = true;
                }

                // 透明度
                float a = EditorGUILayout.Slider(p.Alpha, 0f, 1f, GUILayout.Width(90f));

                if (!Mathf.Approximately(a, p.Alpha))
                {
                    p.Alpha = a;
                    _dirty = true;
                }

                if (GUILayout.Button("✕", GUILayout.Width(26f)))
                {
                    _layout.Posters.RemoveAt(i);
                    _dirty = true;
                    GUI.FocusControl(null);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("＋ 加一张", GUILayout.Height(32f)))
            {
                string first = _availableImages.Length > 0 ? _availableImages[0] : "poster_01";

                _layout.Posters.Add(new PosterLayout.Poster
                {
                    Image = first,
                    Yaw = 0f,
                    Height = 1.6f,
                    Width = 0.6f,
                    Alpha = 1f
                });

                _dirty = true;
                GUI.FocusControl(null);
            }

            GUI.enabled = _dirty;

            if (GUILayout.Button("保存", GUILayout.Height(32f)))
            {
                SaveToDisk();
            }

            GUI.enabled = true;

            if (GUILayout.Button("重新读取", GUILayout.Height(32f)))
            {
                if (!_dirty || EditorUtility.DisplayDialog("墙上插画",
                        "有没保存的改动，确定丢弃？", "丢弃", "取消"))
                {
                    LoadFromDisk();
                }
            }

            if (GUILayout.Button("恢复默认摆放", GUILayout.Height(32f)))
            {
                _layout.ResetToDefault();
                _dirty = true;
                GUI.FocusControl(null);
            }

            if (GUILayout.Button("刷新图片列表", GUILayout.Height(32f)))
            {
                RefreshImageList();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(
                _dirty ? "● 有未保存的改动" : _status,
                EditorStyles.miniLabel);
        }
    }
}
