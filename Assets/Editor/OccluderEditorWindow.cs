using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 监控画面「遮挡区」编辑器 —— 手动画多边形，运行时据此裁剪敌人。
    ///
    /// 为什么需要它：程序自动算的遮挡遮罩（按 3D 盒子）不够准，
    /// 人工按真实家具轮廓画折线最可靠。
    ///
    /// 用法：
    ///   1. 菜单「奶龙 / 6. 编辑监控遮挡区」打开窗口
    ///   2. 顶部选机位
    ///   3. 在图上**点击**放置折线点（围出家具轮廓）
    ///   4. 双击 / 点「闭合多边形」结束当前这块
    ///   5. 一块房间可以画多块（比如三把椅子各画一块）
    ///   6. 点「保存」写入 Assets/Art/Monitoring/occluders.json
    ///
    /// 坐标：归一化 0..1，**左下角为原点**（和 Unity UI 一致），
    /// y 向上。这样运行时不用换算。
    /// </summary>
    public class OccluderEditorWindow : EditorWindow
    {
        // 放 Resources 下，运行时才能 Resources.Load 到
        private const string JsonPath = "Assets/Resources/occluders.json";
        private const string MonitorDir = "Assets/Art/Monitoring";

        private static readonly string[] RoomNames =
        {
            "CAM 1A 舞台", "CAM 1B 餐厅", "CAM 1C 海盗湾",
            "CAM 2A 西走廊", "CAM 2B 西转角", "CAM 3 储藏室",
            "CAM 4A 东走廊", "CAM 4B 东转角", "CAM 5 后台",
            "CAM 6 厨房（无画面）", "CAM 7 厕所"
        };

        private static readonly string[] RoomFiles =
        {
            "mon_1a", "mon_1b", "mon_1c", "mon_2a", "mon_2b", "mon_3",
            "mon_4a", "mon_4b", "mon_5", "mon_6", "mon_7"
        };

        /// <summary>每个机位的多边形集合。每个多边形是一串归一化点。</summary>
        private Dictionary<int, List<List<Vector2>>> _shapes;

        private int _room;
        private List<Vector2> _current = new List<Vector2>();
        private Texture2D _tex;
        private Vector2 _scroll;

        [MenuItem("奶龙/6. 编辑监控遮挡区", false, 15)]
        public static void Open()
        {
            var w = GetWindow<OccluderEditorWindow>("监控遮挡区");
            w.minSize = new Vector2(900f, 700f);
            w.Load();
        }

        private void Load()
        {
            _shapes = new Dictionary<int, List<List<Vector2>>>();

            if (File.Exists(JsonPath))
            {
                try
                {
                    var doc = JsonUtility.FromJson<OccluderData>(File.ReadAllText(JsonPath));
                    if (doc != null && doc.rooms != null)
                    {
                        foreach (var r in doc.rooms)
                        {
                            var list = new List<List<Vector2>>();
                            if (r.shapes != null)
                            {
                                foreach (var s in r.shapes)
                                {
                                    var poly = new List<Vector2>();
                                    if (s.points != null)
                                    {
                                        for (int i = 0; i + 1 < s.points.Length; i += 2)
                                        {
                                            poly.Add(new Vector2(s.points[i], s.points[i + 1]));
                                        }
                                    }
                                    if (poly.Count >= 3)
                                    {
                                        list.Add(poly);
                                    }
                                }
                            }
                            _shapes[r.room] = list;
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[遮挡区] 读取失败：" + e.Message);
                }
            }

            LoadTexture();
        }

        private void LoadTexture()
        {
            string path = MonitorDir + "/" + RoomFiles[_room] + ".png";
            _tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private void Save()
        {
            var doc = new OccluderData();
            var rooms = new List<OccluderRoom>();

            foreach (var kv in _shapes)
            {
                var room = new OccluderRoom { room = kv.Key };
                var shapes = new List<OccluderShape>();

                foreach (var poly in kv.Value)
                {
                    if (poly.Count < 3)
                    {
                        continue;
                    }

                    var pts = new List<float>();
                    foreach (var p in poly)
                    {
                        pts.Add(p.x);
                        pts.Add(p.y);
                    }
                    shapes.Add(new OccluderShape { points = pts.ToArray() });
                }

                room.shapes = shapes.ToArray();
                rooms.Add(room);
            }

            doc.rooms = rooms.ToArray();

            // ⚠️ **先合并再写**：把文件里已有、但本窗口没加载到的机位保留下来。
            // 直接整体覆盖会**悄悄删掉别人的机位**
            // （踩过：我用脚本重写这个文件，把用户画的几间房冲掉了，且无备份可恢复）。
            Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));

            if (File.Exists(JsonPath))
            {
                // 每次都先备份，出事能找回
                File.Copy(JsonPath, JsonPath + ".bak", true);

                try
                {
                    var existing = JsonUtility.FromJson<OccluderData>(File.ReadAllText(JsonPath));
                    if (existing != null && existing.rooms != null)
                    {
                        var have = new HashSet<int>();
                        foreach (var r in rooms)
                        {
                            have.Add(r.room);
                        }

                        var merged = new List<OccluderRoom>(rooms);
                        foreach (var r in existing.rooms)
                        {
                            if (!have.Contains(r.room))
                            {
                                merged.Add(r);   // 本窗口没动过的机位，原样保留
                            }
                        }
                        doc.rooms = merged.ToArray();
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[遮挡区] 合并已有数据失败（将只写当前窗口内容）：" + e.Message);
                }
            }

            File.WriteAllText(JsonPath, JsonUtility.ToJson(doc, true));
            AssetDatabase.Refresh();

            Debug.Log("[遮挡区] 已保存 " + doc.rooms.Length + " 个机位 -> " + JsonPath
                + "（旧文件已备份为 occluders.json.bak）");
        }

        private void OnGUI()
        {
            if (_shapes == null)
            {
                Load();
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            int newRoom = EditorGUILayout.Popup(_room, RoomNames, GUILayout.Width(220f));
            if (newRoom != _room)
            {
                _room = newRoom;
                _current.Clear();
                LoadTexture();
            }

            GUILayout.Space(12f);
            if (GUILayout.Button("闭合多边形", EditorStyles.toolbarButton, GUILayout.Width(90f)))
            {
                CloseCurrent();
            }

            if (GUILayout.Button("撤销上一点", EditorStyles.toolbarButton, GUILayout.Width(90f)))
            {
                if (_current.Count > 0)
                {
                    _current.RemoveAt(_current.Count - 1);
                }
            }

            if (GUILayout.Button("清空本机位", EditorStyles.toolbarButton, GUILayout.Width(90f)))
            {
                _shapes[_room] = new List<List<Vector2>>();
                _current.Clear();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                Save();
            }

            EditorGUILayout.EndHorizontal();

            if (_tex == null)
            {
                EditorGUILayout.HelpBox("找不到监控图：" + MonitorDir + "/" + RoomFiles[_room] + ".png",
                    MessageType.Warning);
                return;
            }

            // 画面区域
            float availW = position.width - 20f;
            float availH = position.height - 110f;
            float aspect = (float)_tex.width / _tex.height;

            float w = availW;
            float h = w / aspect;
            if (h > availH)
            {
                h = availH;
                w = h * aspect;
            }

            var rect = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
            rect.x = (position.width - w) * 0.5f;
            rect.y = 60f;

            GUI.DrawTexture(rect, _tex, ScaleMode.StretchToFill);

            DrawShapes(rect);
            DrawCurrent(rect);
            HandleInput(rect);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "点击放点 · 双击或点「闭合多边形」结束 · 已画 " + CountShapes() + " 块",
                EditorStyles.miniLabel);
        }

        private int CountShapes()
        {
            List<List<Vector2>> list;
            return _shapes.TryGetValue(_room, out list) ? list.Count : 0;
        }

        /// <summary>归一化坐标 → 窗口像素（y 要翻：数据左下原点，GUI 左上原点）。</summary>
        private static Vector2 ToGui(Rect r, Vector2 norm)
        {
            return new Vector2(r.x + norm.x * r.width, r.y + (1f - norm.y) * r.height);
        }

        private static Vector2 ToNorm(Rect r, Vector2 gui)
        {
            return new Vector2(
                Mathf.Clamp01((gui.x - r.x) / r.width),
                Mathf.Clamp01(1f - (gui.y - r.y) / r.height));
        }

        private void DrawShapes(Rect r)
        {
            List<List<Vector2>> list;
            if (!_shapes.TryGetValue(_room, out list))
            {
                return;
            }

            Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);

            foreach (var poly in list)
            {
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 a = ToGui(r, poly[i]);
                    Vector2 b = ToGui(r, poly[(i + 1) % poly.Count]);
                    Handles.DrawAAPolyLine(3f, a, b);
                }
            }
        }

        private void DrawCurrent(Rect r)
        {
            if (_current.Count == 0)
            {
                return;
            }

            Handles.color = new Color(0.3f, 1f, 0.5f, 1f);

            for (int i = 0; i < _current.Count; i++)
            {
                Vector2 a = ToGui(r, _current[i]);
                Handles.DrawSolidDisc(a, Vector3.forward, 4f);

                if (i > 0)
                {
                    Handles.DrawAAPolyLine(3f, ToGui(r, _current[i - 1]), a);
                }
            }

            // 预览「下一个点会连回起点」
            if (_current.Count >= 2)
            {
                Handles.color = new Color(0.3f, 1f, 0.5f, 0.35f);
                Handles.DrawAAPolyLine(2f, ToGui(r, _current[_current.Count - 1]),
                    ToGui(r, _current[0]));
            }
        }

        private void HandleInput(Rect r)
        {
            var e = Event.current;
            if (e == null || !r.Contains(e.mousePosition))
            {
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (e.clickCount >= 2)
                {
                    CloseCurrent();
                }
                else
                {
                    _current.Add(ToNorm(r, e.mousePosition));
                }
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseMove)
            {
                Repaint();
            }
        }

        private void CloseCurrent()
        {
            if (_current.Count < 3)
            {
                return;
            }

            List<List<Vector2>> list;
            if (!_shapes.TryGetValue(_room, out list))
            {
                list = new List<List<Vector2>>();
                _shapes[_room] = list;
            }

            list.Add(new List<Vector2>(_current));
            _current.Clear();
            Repaint();
        }

        // ---------------- 序列化结构（JsonUtility 要 public 字段）----------------

        [System.Serializable]
        private class OccluderData
        {
            public OccluderRoom[] rooms;
        }

        [System.Serializable]
        private class OccluderRoom
        {
            public int room;
            public OccluderShape[] shapes;
        }

        [System.Serializable]
        private class OccluderShape
        {
            /// <summary>x0, y0, x1, y1, ...（归一化，左下原点）</summary>
            public float[] points;
        }
    }
}
