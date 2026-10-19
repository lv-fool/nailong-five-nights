using System.Collections.Generic;
using System.IO;
using NailongFNAF.Gameplay;
using NailongFNAF.UI;
using UnityEditor;
using UnityEngine;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 监控画面编辑器 —— 一个窗口里干三件事：
    ///
    ///   1. <b>遮挡区</b>：按家具轮廓画多边形，运行时据此裁剪敌人立绘。
    ///      选中一块后会出现**八个控制点**，像拖图片边框一样整体缩放；
    ///      拖顶点改单个拐角；拖内部整块平移。
    ///   2. <b>敌人点位</b>：四只敌人各一个点，拖到哪儿，它在监控里就站在哪儿
    ///      （按机位分别保存）。没摆过的机位仍走原来的自动布局。
    ///   3. <b>背景图</b>：拖图平移、拖角缩放。以后换成分块背景时，
    ///      靠这个把图对齐到画面。
    ///
    /// 数据落两个文件：
    ///   · Assets/Resources/occluders.json        —— 遮挡多边形
    ///   · Assets/Resources/monitor_layout.json   —— 底图变换 + 敌人点位
    ///
    /// 坐标一律**归一化 0..1、左下原点**（和 Unity UI 一致），运行时不用换算。
    /// </summary>
    public class OccluderEditorWindow : EditorWindow
    {
        // 放 Resources 下，运行时才能 Resources.Load 到
        private const string JsonPath = "Assets/Resources/occluders.json";
        private const string LayoutPath = "Assets/Resources/monitor_layout.json";
        private const string MonitorDir = "Assets/Art/Monitoring";

        /// <summary>画布逻辑尺寸 —— 归一化换算的基准，和 PrototypeScene 里一致。</summary>
        private const float DesignW = 1920f;
        private const float DesignH = 1080f;

        private enum Mode
        {
            Occluders,
            Enemies,
            Background
        }

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

        /// <summary>下标 = EnemyId 的整数值，顺序不能改。</summary>
        private static readonly string[] EnemyNames =
        {
            "牛来 Niulai", "袋鼠 Daishu", "噜噜 Lulu", "奶龙 Nailong"
        };

        private static readonly Color[] EnemyColors =
        {
            new Color(0.95f, 0.75f, 0.30f),   // 牛来
            new Color(0.40f, 0.85f, 0.45f),   // 袋鼠
            new Color(0.50f, 0.65f, 0.95f),   // 噜噜
            new Color(0.95f, 0.55f, 0.55f)    // 奶龙
        };

        private Mode _mode = Mode.Occluders;
        private int _room;
        private Texture2D _tex;

        // ---------------- 遮挡区 ----------------

        /// <summary>每个机位的多边形集合。每个多边形是一串归一化点。</summary>
        private Dictionary<int, List<List<Vector2>>> _shapes;

        /// <summary>
        /// 每块遮挡的形状类型。key = room * 1000 + 该房间里的第几块。
        ///
        /// ⚠️ 单独存一张表而不是塞进 _shapes —— _shapes 是
        /// List&lt;List&lt;Vector2&gt;&gt;，被绘制 / 拖动 / 删除等十几处引用，
        /// 改结构要动一大片。用旁挂表加一个"按房间+序号"的键，
        /// 改动面最小，老数据默认 0（多边形）天然兼容。
        /// </summary>
        private Dictionary<int, int> _shapeKinds = new Dictionary<int, int>();

        /// <summary>当前正在用的绘制工具。</summary>
        private Tool _tool = Tool.Select;

        /// <summary>矩形 / 椭圆拖拽中的起点（归一化）。</summary>
        private Vector2 _boxStart;
        private bool _boxDragging;

        private enum Tool
        {
            // -1：只选择/拖动，**点空白不加点**。
            // 数值故意和其他三个错开 —— 0/1/2 要和 __shapeKinds__ 里的 kind 对齐。
            Select = -1,

            Polygon = 0,
            Rect = 1,
            Ellipse = 2,
        }

        private List<Vector2> _current = new List<Vector2>();

        /// <summary>选中的**单个顶点**（-1 = 没选）。用来单独拖动 / 删除一个点。</summary>
        private int _selVertex = -1;

        /// <summary>当前选中的多边形（在本机位列表里的下标），-1 = 没选。</summary>
        private int _selShape = -1;

        // ---------------- 拖拽状态 ----------------

        private int _dragKind;           // 0 无 / 1 顶点 / 2 边框控制点 / 3 整块平移 / 4 敌人点 / 5 背景
        private int _dragIndex;          // 顶点下标 / 控制点下标 / 敌人 id
        private Vector2 _dragLast;       // 上一次鼠标（归一化，或 GUI 像素 —— 见各分支）
        private List<Vector2> _dragPoly; // 拖拽开始时的多边形快照
        private Rect _dragBox;           // 拖拽开始时的包围盒（归一化）

        // ---------------- 敌人点位 / 背景 ----------------

        private Dictionary<int, LayoutRoom> _layouts;

        /// <summary>敌人点位模式下当前选中的敌人（EnemyId 整数）。</summary>
        private int _selEnemy = 3;

        /// <summary>正在编辑哪一档点位：0 = 近，1 = 远。</summary>
        private int _tier = 0;

        /// <summary>
        /// 有没有未保存的改动。
        /// 加它是因为踩过：拖完点位忘点保存、直接关窗口，一晚上的调整全白费。
        /// 现在标题栏显示 *，关窗口 / 切机位时也会自动保存。
        /// </summary>
        private bool _dirty;

        private GUIStyle _tagStyle;

        [MenuItem("奶龙/6. 监控画面编辑器（背景 / 敌人点位 / 遮挡）", false, 15)]
        public static void Open()
        {
            var w = GetWindow<OccluderEditorWindow>("监控画面编辑器");
            w.minSize = new Vector2(960f, 720f);
            w.Load();
        }

        // ==================== 读取 ====================

        private void Load()
        {
            LoadShapes();
            LoadLayout();
            LoadTexture();
        }

        /// <summary>房间 + 块序号 → 唯一键（给 _shapeKinds 用）。</summary>
        private static int ShapeKey(int room, int index)
        {
            return room * 1000 + index;
        }

        private int KindOf(int room, int index)
        {
            int k;
            return _shapeKinds.TryGetValue(ShapeKey(room, index), out k) ? k : 0;
        }

        private void SetKind(int room, int index, int kind)
        {
            _shapeKinds[ShapeKey(room, index)] = kind;
        }
        private void LoadShapes()
        {
            _shapes = new Dictionary<int, List<List<Vector2>>>();

            if (!File.Exists(JsonPath))
            {
                return;
            }

            try
            {
                var doc = JsonUtility.FromJson<OccluderData>(File.ReadAllText(JsonPath));
                if (doc == null || doc.rooms == null)
                {
                    return;
                }

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
                                // 记下类型（缺省 0 = 多边形，老数据自动兼容）
                                _shapeKinds[ShapeKey(r.room, list.Count - 1)] = s.kind;
                            }
                        }
                    }
                    _shapes[r.room] = list;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[监控编辑器] 遮挡数据读取失败：" + e.Message);
            }
        }

        private void LoadLayout()
        {
            _layouts = new Dictionary<int, LayoutRoom>();

            if (!File.Exists(LayoutPath))
            {
                return;
            }

            try
            {
                var doc = JsonUtility.FromJson<LayoutDoc>(File.ReadAllText(LayoutPath));
                if (doc == null || doc.rooms == null)
                {
                    return;
                }

                foreach (var r in doc.rooms)
                {
                    if (r == null)
                    {
                        continue;
                    }

                    if (r.bgScale <= 0.01f)
                    {
                        r.bgScale = 1f;
                    }
                    _layouts[r.room] = r;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[监控编辑器] 布局读取失败：" + e.Message);
            }
        }

        private void LoadTexture()
        {
            string path = MonitorDir + "/" + RoomFiles[_room] + ".png";
            _tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>取某机位的布局，没有就新建一份默认值。</summary>
        private LayoutRoom Layout(int room)
        {
            LayoutRoom r;
            if (!_layouts.TryGetValue(room, out r) || r == null)
            {
                r = new LayoutRoom { room = room, bgScale = 1f };
                _layouts[room] = r;
            }
            return r;
        }

        // ==================== 保存 ====================

        private void SaveAll()
        {
            _dirty = false;
            SaveShapes();
            SaveLayout();
            AssetDatabase.Refresh();
        }

        private void SaveShapes()
        {
            var doc = new OccluderData();
            var rooms = new List<OccluderRoom>();

            foreach (var kv in _shapes)
            {
                var room = new OccluderRoom { room = kv.Key };
                var shapes = new List<OccluderShape>();

                // 序号要跟 _shapeKinds 的键一致 —— 所以先按原序配对再过滤
                for (int si = 0; si < kv.Value.Count; si++)
                {
                    var poly = kv.Value[si];
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

                    shapes.Add(new OccluderShape
                    {
                        kind = KindOf(kv.Key, si),
                        points = pts.ToArray(),
                    });
                }

                room.shapes = shapes.ToArray();
                rooms.Add(room);
            }

            doc.rooms = rooms.ToArray();

            // ⚠️ **先合并再写**：把文件里已有、但本窗口没加载到的机位保留下来。
            // 直接整体覆盖会**悄悄删掉别人的机位**
            // （踩过：用脚本重写这个文件，把用户画的几间房冲掉了，且无备份可恢复）。
            Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));

            if (File.Exists(JsonPath))
            {
                File.Copy(JsonPath, JsonPath + ".bak", true);   // 每次先备份，出事能找回

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
                    Debug.LogWarning("[监控编辑器] 合并已有遮挡数据失败：" + e.Message);
                }
            }

            File.WriteAllText(JsonPath, JsonUtility.ToJson(doc, true));
        }

        private void SaveLayout()
        {
            var doc = new LayoutDoc();
            var rooms = new List<LayoutRoom>();

            foreach (var kv in _layouts)
            {
                var r = kv.Value;
                if (r == null)
                {
                    continue;
                }

                // 空的默认值不写，免得文件里堆一堆没意义的机位
                bool hasPoints = r.points != null && r.points.Length > 0;
                bool hasBg = Mathf.Abs(r.bgX) > 0.01f
                             || Mathf.Abs(r.bgY) > 0.01f
                             || Mathf.Abs(r.bgScale - 1f) > 0.001f;

                if (!hasPoints && !hasBg)
                {
                    continue;
                }

                rooms.Add(r);
            }

            doc.rooms = rooms.ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath));

            if (File.Exists(LayoutPath))
            {
                File.Copy(LayoutPath, LayoutPath + ".bak", true);

                try
                {
                    var existing = JsonUtility.FromJson<LayoutDoc>(File.ReadAllText(LayoutPath));
                    if (existing != null && existing.rooms != null)
                    {
                        var have = new HashSet<int>();
                        foreach (var r in rooms)
                        {
                            have.Add(r.room);
                        }

                        var merged = new List<LayoutRoom>(rooms);
                        foreach (var r in existing.rooms)
                        {
                            if (r != null && !have.Contains(r.room))
                            {
                                merged.Add(r);
                            }
                        }
                        doc.rooms = merged.ToArray();
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[监控编辑器] 合并已有布局失败：" + e.Message);
                }
            }

            File.WriteAllText(LayoutPath, JsonUtility.ToJson(doc, true));

            Debug.Log("[监控编辑器] 布局已保存 -> " + LayoutPath);
        }

        // ==================== 界面 ====================

        private void OnGUI()
        {
            if (_shapes == null)
            {
                Load();
            }

            DrawToolbar();

            if (_tex == null)
            {
                EditorGUILayout.HelpBox("找不到监控图：" + MonitorDir + "/" + RoomFiles[_room] + ".png",
                    MessageType.Warning);
                return;
            }

            Rect rect = ImageRect();

            DrawBackgroundImage(rect);

            switch (_mode)
            {
                case Mode.Background:
                    DrawBackgroundHandles(rect);
                    break;
                case Mode.Enemies:
                    DrawEnemyPoints(rect);
                    break;
                default:
                    DrawShapes(rect);
                    DrawCurrent(rect);
                    DrawSelection(rect);
                    break;
            }

            HandleInput(rect);
            DrawFooter();
        }

        /// <summary>
        /// 关窗口 / 重编译时自动保存未存的改动。
        /// 踩过：拖完点位直接关窗口，一晚上的调整全没了。
        /// </summary>
        private void OnDisable()
        {
            if (_dirty)
            {
                SaveAll();
                Debug.Log("[监控编辑器] 关窗口时自动保存了未提交的改动");
            }
        }
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // 有未保存改动时显示橙色 *，一眼能看到
            if (_dirty)
            {
                var oldD = GUI.color;
                GUI.color = new Color(1f, 0.75f, 0.25f);
                GUILayout.Label("*", EditorStyles.toolbarButton, GUILayout.Width(16f));
                GUI.color = oldD;
            }

            int newRoom = EditorGUILayout.Popup(_room, RoomNames, EditorStyles.toolbarPopup,
                GUILayout.Width(170f));
            if (newRoom != _room)
            {
                // 切机位前先存 —— 否则上一个机位的调整会丢
                if (_dirty)
                {
                    SaveAll();
                }

                _room = newRoom;
                _current.Clear();
                _selShape = -1;
                _dragKind = 0;
                LoadTexture();
            }

            GUILayout.Space(10f);

            // 三个模式按钮
            if (GUILayout.Toggle(_mode == Mode.Occluders, "遮挡区",
                    EditorStyles.toolbarButton, GUILayout.Width(66f)))
            {
                SwitchMode(Mode.Occluders);
            }

            if (GUILayout.Toggle(_mode == Mode.Enemies, "敌人点位",
                    EditorStyles.toolbarButton, GUILayout.Width(72f)))
            {
                SwitchMode(Mode.Enemies);
            }

            if (GUILayout.Toggle(_mode == Mode.Background, "背景图",
                    EditorStyles.toolbarButton, GUILayout.Width(62f)))
            {
                SwitchMode(Mode.Background);
            }

            GUILayout.Space(10f);

            if (_mode == Mode.Occluders)
            {
                // 工具：选择 / 多边形 / 矩形 / 椭圆
                if (GUILayout.Toggle(_tool == Tool.Select, "选择",
                        EditorStyles.toolbarButton, GUILayout.Width(48f)))
                {
                    _tool = Tool.Select;
                }

                // 切走多边形时若有未闭合的点，给个提示 —— 但**不清空**，
                // 免得切个工具就把画了一半的多边形弄丢
                if (_current.Count > 0 && _tool != Tool.Polygon)
                {
                    var oldC = GUI.color;
                    GUI.color = new Color(1f, 0.8f, 0.3f);
                    GUILayout.Label("（有 " + _current.Count + " 个未闭合的点）",
                        EditorStyles.toolbarButton, GUILayout.Width(150f));
                    GUI.color = oldC;
                }

                if (GUILayout.Toggle(_tool == Tool.Polygon, "多边形",
                        EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    _tool = Tool.Polygon;
                }

                if (GUILayout.Toggle(_tool == Tool.Rect, "矩形",
                        EditorStyles.toolbarButton, GUILayout.Width(48f)))
                {
                    _tool = Tool.Rect;
                }

                if (GUILayout.Toggle(_tool == Tool.Ellipse, "椭圆",
                        EditorStyles.toolbarButton, GUILayout.Width(48f)))
                {
                    _tool = Tool.Ellipse;
                }

                GUILayout.Space(6f);

                if (_tool == Tool.Polygon)
                {
                    if (GUILayout.Button("闭合多边形", EditorStyles.toolbarButton, GUILayout.Width(84f)))
                    {
                        CloseCurrent();
                    }
                }

                if (GUILayout.Button("撤销上一点", EditorStyles.toolbarButton, GUILayout.Width(84f)))
                {
                    if (_current.Count > 0)
                    {
                        _current.RemoveAt(_current.Count - 1);
                    }
                }

                GUI.enabled = _selShape >= 0;
                string delLabel = _selVertex >= 0 ? "删除顶点" : "删除选中块";
                if (GUILayout.Button(delLabel, EditorStyles.toolbarButton, GUILayout.Width(84f)))
                {
                    DeleteSelected();
                }
                GUI.enabled = true;

                // 在**光标最近的那条边**上插一个顶点 —— 补形状用，
                // 没有它的话想加个拐角只能整块重画。
                //
                // ⚠️ 只在多边形模式下可用：
                // 矩形 / 椭圆的数据就是包围盒四角，插点会变成五边形，
                // 和"这是个矩形"的语义冲突（渲染仍按包围盒，插的点会被忽略）。
                GUI.enabled = _selShape >= 0
                    && _tool == Tool.Polygon
                    && KindOf(_room, _selShape) == (int)Tool.Polygon;
                if (GUILayout.Button("插入顶点", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                {
                    InsertVertexNearMouse();
                }
                GUI.enabled = true;
            }
            else if (_mode == Mode.Enemies)
            {
                // 先选调哪一档 —— 近 / 远是**两套独立点位**
                if (GUILayout.Toggle(_tier == 0, "近景点位",
                        EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    _tier = 0;
                }

                if (GUILayout.Toggle(_tier == 1, "远景点位",
                        EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    _tier = 1;
                }

                GUILayout.Space(8f);

                for (int i = 0; i < EnemyNames.Length; i++)
                {
                    var old = GUI.color;
                    GUI.color = _selEnemy == i ? EnemyColors[i] : Color.white;
                    if (GUILayout.Button(EnemyNames[i], EditorStyles.toolbarButton, GUILayout.Width(96f)))
                    {
                        _selEnemy = i;
                    }
                    GUI.color = old;
                }
            }
            else
            {
                if (GUILayout.Button("复位背景", EditorStyles.toolbarButton, GUILayout.Width(74f)))
                {
                    var l = Layout(_room);
                    l.bgX = 0f;
                    l.bgY = 0f;
                    l.bgScale = 1f;
                    Repaint();
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                SaveAll();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void SwitchMode(Mode m)
        {
            if (_mode == m)
            {
                return;
            }

            _mode = m;
            _current.Clear();
            _dragKind = 0;
            Repaint();
        }

        private Rect ImageRect()
        {
            const float top = 62f;
            const float bottom = 52f;

            float availW = position.width - 20f;
            float availH = position.height - top - bottom;
            float aspect = (float)_tex.width / _tex.height;

            float w = availW;
            float h = w / aspect;
            if (h > availH)
            {
                h = availH;
                w = h * aspect;
            }

            return new Rect((position.width - w) * 0.5f, top, w, h);
        }

        private void DrawBackgroundImage(Rect baseRect)
        {
            Rect r = BgRect(baseRect);
            GUI.DrawTexture(r, _tex, ScaleMode.StretchToFill);

            if (r != baseRect)
            {
                // 缩放后边缘用细线标出来，方便看清裁掉了多少
                Handles.color = new Color(0.4f, 0.8f, 1f, 0.6f);
                Handles.DrawAAPolyLine(1.5f,
                    new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
                    new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax),
                    new Vector2(r.xMin, r.yMin));
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(2f);

            string tip;
            switch (_mode)
            {
                case Mode.Occluders:
                    switch (_tool)
                    {
                        case Tool.Select:
                            tip = "选择模式：点中块 → 拖内部平移，拖顶点改拐角，拖八个控制点整体缩放（点空白不会加点）";
                            break;
                        case Tool.Rect:
                            tip = "矩形：按住左键拖出一个框，松手成块";
                            break;
                        case Tool.Ellipse:
                            tip = "椭圆：按住左键拖出一个框，松手成椭圆";
                            break;
                        default:
                            tip = "多边形：点击放点 · 双击 / 「闭合多边形」结束";
                            break;
                    }
                    break;
                case Mode.Enemies:
                    tip = "直接拖四只敌人的圆点摆位置（按机位分别保存）· 顶栏切换要摆哪只";
                    break;
                default:
                    tip = "拖图平移 · 拖四角缩放 · 「复位背景」回到铺满";
                    break;
            }

            EditorGUILayout.LabelField(tip + "   |   本机位：遮挡 " + CountShapes() + " 块",
                EditorStyles.miniLabel);
        }

        private int CountShapes()
        {
            List<List<Vector2>> list;
            return _shapes.TryGetValue(_room, out list) ? list.Count : 0;
        }

        // ==================== 坐标换算 ====================

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

        /// <summary>底图实际画出来的矩形（含平移缩放）。</summary>
        private Rect BgRect(Rect baseRect)
        {
            var l = Layout(_room);
            float s = l.bgScale <= 0.01f ? 1f : l.bgScale;

            float px = baseRect.width / DesignW;    // 设计像素 → 屏幕像素
            Vector2 center = baseRect.center + new Vector2(l.bgX * px, -l.bgY * px);

            float w = baseRect.width * s;
            float h = baseRect.height * s;
            return new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h);
        }

        // ==================== 画：遮挡 ====================

        private void DrawShapes(Rect r)
        {
            List<List<Vector2>> list;
            if (!_shapes.TryGetValue(_room, out list))
            {
                return;
            }

            for (int s = 0; s < list.Count; s++)
            {
                var poly = list[s];
                bool selected = s == _selShape;

                Handles.color = selected
                    ? new Color(1f, 0.85f, 0.2f, 1f)
                    : new Color(1f, 0.3f, 0.3f, 0.9f);

                int kind = KindOf(_room, s);

                if (kind == (int)Tool.Rect)
                {
                    // 矩形：直接画四条边（tag 是四个角）
                    Rect box = Bounds(poly);
                    Vector2 tl = ToGui(r, new Vector2(box.xMin, box.yMax));
                    Vector2 br = ToGui(r, new Vector2(box.xMax, box.yMin));
                    Handles.DrawAAPolyLine(selected ? 3.5f : 3f,
                        tl, new Vector2(br.x, tl.y), br, new Vector2(tl.x, br.y), tl);
                }
                else if (kind == (int)Tool.Ellipse)
                {
                    // 椭圆：按包围盒画一圈（64 段足够平滑）
                    Rect box = Bounds(poly);
                    Vector2 c = new Vector2((box.xMin + box.xMax) * 0.5f,
                                            (box.yMin + box.yMax) * 0.5f);
                    float rx = (box.xMax - box.xMin) * 0.5f;
                    float ry = (box.yMax - box.yMin) * 0.5f;

                    const int Segs = 64;
                    Vector2 prev = Vector2.zero;
                    for (int i = 0; i <= Segs; i++)
                    {
                        float ang = i / (float)Segs * Mathf.PI * 2f;
                        var p = new Vector2(c.x + Mathf.Cos(ang) * rx,
                                            c.y + Mathf.Sin(ang) * ry);
                        Vector2 g = ToGui(r, p);
                        if (i > 0)
                        {
                            Handles.DrawAAPolyLine(selected ? 3.5f : 3f, prev, g);
                        }
                        prev = g;
                    }
                }
                else
                {
                    for (int i = 0; i < poly.Count; i++)
                    {
                        Vector2 a = ToGui(r, poly[i]);
                        Vector2 b = ToGui(r, poly[(i + 1) % poly.Count]);
                        Handles.DrawAAPolyLine(selected ? 3.5f : 3f, a, b);
                    }
                }

                if (selected)
                {
                    for (int i = 0; i < poly.Count; i++)
                    {
                        // 被选中的那个顶点画成**黄色大点**，和其余白点区分开
                        bool picked = s == _selShape && i == _selVertex;
                        Handles.color = picked
                            ? new Color(1f, 0.9f, 0.15f, 1f)
                            : new Color(1f, 1f, 1f, 0.95f);
                        Handles.DrawSolidDisc(ToGui(r, poly[i]), Vector3.forward,
                            picked ? 7f : 4.5f);
                    }
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

        /// <summary>选中块的包围盒 + 八个控制点（像拖图片边框那样缩放）。</summary>
        private void DrawSelection(Rect r)
        {
            if (_selShape < 0)
            {
                return;
            }

            List<List<Vector2>> list;
            if (!_shapes.TryGetValue(_room, out list) || _selShape >= list.Count)
            {
                return;
            }

            Rect box = Bounds(list[_selShape]);

            Handles.color = new Color(0.35f, 0.85f, 1f, 0.85f);

            Vector2 tl = ToGui(r, new Vector2(box.xMin, box.yMax));
            Vector2 br = ToGui(r, new Vector2(box.xMax, box.yMin));

            Handles.DrawAAPolyLine(1.5f,
                tl, new Vector2(br.x, tl.y), br, new Vector2(tl.x, br.y), tl);

            for (int i = 0; i < 8; i++)
            {
                Vector2 g = ToGui(r, HandlePoint(box, i));
                var rr = new Rect(g.x - 5f, g.y - 5f, 10f, 10f);
                EditorGUI.DrawRect(rr, new Color(0.1f, 0.1f, 0.1f, 0.9f));
                EditorGUI.DrawRect(new Rect(rr.x + 1.5f, rr.y + 1.5f, 7f, 7f),
                    new Color(0.35f, 0.85f, 1f, 1f));
            }
        }

        /// <summary>八个控制点在归一化空间里的位置。0=左上，逆时针 → 7=左中。</summary>
        private static Vector2 HandlePoint(Rect b, int i)
        {
            float cx = b.center.x;
            float cy = b.center.y;

            switch (i)
            {
                case 0: return new Vector2(b.xMin, b.yMax);
                case 1: return new Vector2(cx, b.yMax);
                case 2: return new Vector2(b.xMax, b.yMax);
                case 3: return new Vector2(b.xMax, cy);
                case 4: return new Vector2(b.xMax, b.yMin);
                case 5: return new Vector2(cx, b.yMin);
                case 6: return new Vector2(b.xMin, b.yMin);
                default: return new Vector2(b.xMin, cy);
            }
        }

        private static Rect Bounds(List<Vector2> poly)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var p in poly)
            {
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        // ==================== 画：敌人点位 ====================

        /// <summary>敌人立绘（按代号索引），懒加载。</summary>
        private readonly Texture2D[] _enemyArt = new Texture2D[4];

        private static readonly string[] EnemyCodes =
        {
            "niulai", "daishu", "lulu", "nailong"
        };

        private Texture2D EnemyArt(int index)
        {
            if (index < 0 || index >= _enemyArt.Length)
            {
                return null;
            }

            if (_enemyArt[index] != null)
            {
                return _enemyArt[index];
            }

            string path = "Assets/Art/Characters/char_" + EnemyCodes[index] + "_idle.png";
            _enemyArt[index] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return _enemyArt[index];
        }

        /// <summary>
        /// 这只敌人在这个机位、这一档的**最终显示尺寸**（屏幕像素）。
        ///
        /// ⚠️ 必须和运行时 PrototypeScene 算得一模一样，否则你在编辑器里
        /// 摆得挺好，进游戏就胖一圈/瘦一圈。
        /// 运行时是依次相乘：统一定高 380 → 乘机位系数 → 乘近远系数 → 乘手动缩放。
        /// </summary>
        private Vector2 RuntimeSize(int enemy, Texture2D art)
        {
            const float Height = 380f;   // = PrototypeScene.MonitorCharacterHeight

            float aspect = art.height > 0 ? (float)art.width / art.height : 1f;
            float h = Height;
            float w = Height * aspect;

            float slotScale = PrototypeScene.MonitorSlotScale((EnemyId)enemy, _room);

            float tierScale = _tier == 0
                ? PrototypeScene.MonitorNearScale
                : PrototypeScene.MonitorFarScale;

            float manual = 1f;
            var l = Layout(_room);
            if (l.points != null)
            {
                foreach (var p in l.points)
                {
                    if (p != null && p.enemy == enemy && p.tier == _tier && p.scale > 0.01f)
                    {
                        manual = p.scale;
                        break;
                    }
                }
            }

            float total = slotScale * tierScale * manual;
            return new Vector2(w * total, h * total);
        }

        private void DrawEnemyPoints(Rect r)
        {
            for (int e = 0; e < EnemyNames.Length; e++)
            {
                Vector2 norm = PointNorm(_room, e);
                Vector2 g = ToGui(r, norm);

                Color c = EnemyColors[e];
                bool sel = e == _selEnemy;

                // 画**真实立绘** —— 只画圆点根本判断不了构图。
                Texture2D art = EnemyArt(e);
                if (art != null)
                {
                    Vector2 size = RuntimeSize(e, art);
                    float px = r.width / DesignW;   // 设计像素 → 窗口像素
                    float w = size.x * px;
                    float h = size.y * px;
                    var box = new Rect(g.x - w * 0.5f, g.y - h, w, h);

                    var old = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, sel ? 0.95f : 0.45f);
                    GUI.DrawTexture(box, art, ScaleMode.ScaleToFit, true);
                    GUI.color = old;

                    Handles.color = new Color(c.r, c.g, c.b, sel ? 1f : 0.6f);
                    Handles.DrawAAPolyLine(sel ? 2.5f : 1.5f,
                        new Vector2(box.xMin, box.yMin), new Vector2(box.xMax, box.yMin),
                        new Vector2(box.xMax, box.yMax), new Vector2(box.xMin, box.yMax),
                        new Vector2(box.xMin, box.yMin));
                }

                Handles.color = new Color(c.r, c.g, c.b, sel ? 1f : 0.7f);
                Handles.DrawSolidDisc(g, Vector3.forward, sel ? 7f : 5f);

                GUI.Label(new Rect(g.x + 10f, g.y - 8f, 140f, 18f),
                    EnemyNames[e], TagStyle(c));
            }
        }


        private GUIStyle TagStyle(Color c)
        {
            if (_tagStyle == null)
            {
                _tagStyle = new GUIStyle(EditorStyles.boldLabel);
                _tagStyle.fontSize = 11;
            }
            _tagStyle.normal.textColor = c;
            return _tagStyle;
        }

        // ==================== 画：背景控制点 ====================

        private void DrawBackgroundHandles(Rect baseRect)
        {
            Rect b = BgRect(baseRect);

            Handles.color = new Color(0.4f, 0.85f, 1f, 0.9f);

            Vector2[] corners =
            {
                new Vector2(b.xMin, b.yMin), new Vector2(b.xMax, b.yMin),
                new Vector2(b.xMax, b.yMax), new Vector2(b.xMin, b.yMax)
            };

            for (int i = 0; i < 4; i++)
            {
                Vector2 a = corners[i];
                Vector2 c = corners[(i + 1) % 4];
                Handles.DrawAAPolyLine(2f, a, c);

                EditorGUI.DrawRect(new Rect(a.x - 5f, a.y - 5f, 10f, 10f),
                    new Color(0.4f, 0.85f, 1f, 1f));
            }
        }

        // ==================== 输入 ====================

        private void HandleInput(Rect r)
        {
            var e = Event.current;
            if (e == null)
            {
                return;
            }

            // 拖拽中：不要求鼠标还在图里（拖出边界也要跟手）
            if (e.type == EventType.MouseDrag && _dragKind != 0)
            {
                DoDrag(r, e);
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseUp && _dragKind != 0)
            {
                // 矩形 / 椭圆：松手才落块
                if (_dragKind == 6 && _boxDragging)
                {
                    FinishBox(r, e);
                }

                _dragKind = 0;
                _boxDragging = false;
                e.Use();
                Repaint();
                return;
            }

            if (!r.Contains(e.mousePosition) && _mode != Mode.Background)
            {
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                OnMouseDown(r, e);
                e.Use();
                Repaint();
            }
        }

        private void OnMouseDown(Rect r, Event e)
        {
            switch (_mode)
            {
                case Mode.Occluders:
                    OnMouseDownOccluders(r, e);
                    break;
                case Mode.Enemies:
                    OnMouseDownEnemies(r, e);
                    break;
                case Mode.Background:
                    OnMouseDownBackground(r, e);
                    break;
            }
        }

        /// <summary>
        /// 选择模式下的点击：只做选中 / 开始拖动，**绝不新增顶点**。
        ///
        /// 优先级：控制点 → 已有顶点 → 块的内部 → 空白（清选中但不改数据）。
        /// </summary>
        private void OnMouseDownSelectOnly(Rect r, Event e)
        {
            Vector2 norm = ToNorm(r, e.mousePosition);

            // 1) 选中块的八个控制点
            if (_selShape >= 0)
            {
                List<List<Vector2>> list;
                if (_shapes.TryGetValue(_room, out list) && _selShape < list.Count)
                {
                    Rect box = Bounds(list[_selShape]);

                    for (int i = 0; i < 8; i++)
                    {
                        if ((ToGui(r, HandlePoint(box, i)) - e.mousePosition).sqrMagnitude <= 100f)
                        {
                            _dragKind = 2;
                            _dragIndex = i;
                            _dragBox = box;
                            _dragPoly = new List<Vector2>(list[_selShape]);
                            return;
                        }
                    }
                }
            }

            // 2) 任意块的顶点 / 内部
            List<List<Vector2>> shapes;
            if (_shapes.TryGetValue(_room, out shapes))
            {
                for (int s = 0; s < shapes.Count; s++)
                {
                    var poly = shapes[s];

                    for (int i = 0; i < poly.Count; i++)
                    {
                        if ((ToGui(r, poly[i]) - e.mousePosition).sqrMagnitude <= 100f)
                        {
                            _selShape = s;
                            _selVertex = i;
                            _dragKind = 1;
                            _dragIndex = i;
                            _dragPoly = new List<Vector2>(poly);
                            return;
                        }
                    }

                    if (PointInPoly(poly, norm))
                    {
                        _selShape = s;
                        _selVertex = -1;
                        _dragKind = 3;
                        _dragPoly = new List<Vector2>(poly);
                        _dragLast = norm;
                        return;
                    }
                }
            }

            // 3) 点空白：只清选中，什么都不加
            _selShape = -1;
            _selVertex = -1;
            _dragKind = 0;
        }
        private void OnMouseDownOccluders(Rect r, Event e)
        {
            // 选择模式：只做选中 / 拖动 / 顶点编辑。
            // **点空白不清选区、也不加点** —— 这正是它存在的意义：
            // 在多边形模式下，想点空白取消选中就会顺手多打一个点，
            // 结果画着画着多出一堆废点。
            if (_tool == Tool.Select)
            {
                OnMouseDownSelectOnly(r, e);
                return;
            }

            // 矩形 / 椭圆：按下开始拖，松开时成块
            if (_tool == Tool.Rect || _tool == Tool.Ellipse)
            {
                _boxStart = ToNorm(r, e.mousePosition);
                _boxDragging = true;
                _dragKind = 6;
                return;
            }

            if (e.clickCount >= 2)
            {
                CloseCurrent();
                return;
            }

            Vector2 norm = ToNorm(r, e.mousePosition);

            // 1) 先看选中块的八个控制点
            if (_selShape >= 0)
            {
                List<List<Vector2>> list;
                if (_shapes.TryGetValue(_room, out list) && _selShape < list.Count)
                {
                    Rect box = Bounds(list[_selShape]);

                    for (int i = 0; i < 8; i++)
                    {
                        if ((ToGui(r, HandlePoint(box, i)) - e.mousePosition).sqrMagnitude <= 100f)
                        {
                            _dragKind = 2;
                            _dragIndex = i;
                            _dragBox = box;
                            _dragPoly = new List<Vector2>(list[_selShape]);
                            return;
                        }
                    }
                }
            }

            // 2) 再看任意一块的顶点 / 内部（点中就切选中）
            List<List<Vector2>> shapes;
            if (_shapes.TryGetValue(_room, out shapes))
            {
                for (int s = 0; s < shapes.Count; s++)
                {
                    var poly = shapes[s];

                    for (int i = 0; i < poly.Count; i++)
                    {
                        if ((ToGui(r, poly[i]) - e.mousePosition).sqrMagnitude <= 100f)
                        {
                            _selShape = s;
                            _selVertex = i;      // 记住选中了哪个顶点（删除时要用）
                            _dragKind = 1;
                            _dragIndex = i;
                            _dragPoly = new List<Vector2>(poly);
                            return;
                        }
                    }

                    if (PointInPoly(poly, norm))
                    {
                        _selShape = s;
                        _selVertex = -1;     // 点的是块的内部，不是某个顶点
                        _dragKind = 3;
                        _dragPoly = new List<Vector2>(poly);
                        _dragLast = norm;
                        return;
                    }
                }
            }

            // 3) 都没点中 → 取消选择，继续放新点
            _selShape = -1;
            _selVertex = -1;
            _current.Add(norm);
        }

        private void OnMouseDownEnemies(Rect r, Event e)
        {
            for (int i = 0; i < EnemyNames.Length; i++)
            {
                Vector2 g = ToGui(r, PointNorm(_room, i));
                if ((g - e.mousePosition).sqrMagnitude <= 196f)   // 14px
                {
                    _selEnemy = i;
                    _dragKind = 4;
                    _dragIndex = i;
                    return;
                }
            }

            // 点空白：把当前选中的那只直接挪过去（省一次拖动）
            SetPoint(_room, _selEnemy, ToNorm(r, e.mousePosition));
        }

        private void OnMouseDownBackground(Rect r, Event e)
        {
            Rect b = BgRect(r);

            Vector2[] corners =
            {
                new Vector2(b.xMin, b.yMin), new Vector2(b.xMax, b.yMin),
                new Vector2(b.xMax, b.yMax), new Vector2(b.xMin, b.yMax)
            };

            for (int i = 0; i < 4; i++)
            {
                if ((corners[i] - e.mousePosition).sqrMagnitude <= 144f)
                {
                    _dragKind = 5;
                    _dragIndex = i;
                    return;
                }
            }

            if (b.Contains(e.mousePosition))
            {
                _dragKind = 5;
                _dragIndex = -1;          // -1 = 平移
                _dragLast = e.mousePosition;
            }
        }

        private void DoDrag(Rect r, Event e)
        {
            switch (_dragKind)
            {
                case 1:   // 顶点
                    DragVertex(r, e);
                    break;
                case 2:   // 边框控制点
                    DragHandle(r, e);
                    break;
                case 3:   // 整块平移
                    DragShape(r, e);
                    break;
                case 4:   // 敌人点
                    SetPoint(_room, _dragIndex, ToNorm(r, e.mousePosition));
                    break;
                case 5:   // 背景
                    DragBackground(r, e);
                    break;
                case 6:   // 矩形 / 椭圆拖拽中（松手时才成块）
                    break;
            }
        }

        private void DragVertex(Rect r, Event e)
        {
            List<List<Vector2>> list;
            if (!_shapes.TryGetValue(_room, out list) || _selShape < 0 || _selShape >= list.Count)
            {
                return;
            }

            if (_dragPoly == null || _dragIndex >= _dragPoly.Count)
            {
                return;
            }

            // 以按下时的快照为准，避免累加误差
            list[_selShape][_dragIndex] = ToNorm(r, e.mousePosition);
        }

        private void DragShape(Rect r, Event e)
        {
            List<List<Vector2>> list;
            if (!_shapes.TryGetValue(_room, out list) || _selShape < 0 || _selShape >= list.Count)
            {
                return;
            }

            if (_dragPoly == null)
            {
                return;
            }

            Vector2 now = ToNorm(r, e.mousePosition);
            Vector2 delta = now - _dragLast;
            _dragLast = now;

            var poly = list[_selShape];
            for (int i = 0; i < poly.Count; i++)
            {
                poly[i] = new Vector2(
                    Mathf.Clamp01(poly[i].x + delta.x),
                    Mathf.Clamp01(poly[i].y + delta.y));
            }
        }

        private void DragHandle(Rect r, Event e)
        {
            List<List<Vector2>> list;
            if (!_shapes.TryGetValue(_room, out list) || _selShape < 0 || _selShape >= list.Count)
            {
                return;
            }

            if (_dragPoly == null || _dragPoly.Count == 0)
            {
                return;
            }

            Vector2 norm = ToNorm(r, e.mousePosition);

            float minX = _dragBox.xMin, maxX = _dragBox.xMax;
            float minY = _dragBox.yMin, maxY = _dragBox.yMax;

            bool left = _dragIndex == 0 || _dragIndex == 6 || _dragIndex == 7;
            bool right = _dragIndex == 2 || _dragIndex == 3 || _dragIndex == 4;
            bool top = _dragIndex == 0 || _dragIndex == 1 || _dragIndex == 2;
            bool bottom = _dragIndex == 4 || _dragIndex == 5 || _dragIndex == 6;

            if (left)
            {
                minX = Mathf.Min(norm.x, maxX - 0.005f);
            }
            if (right)
            {
                maxX = Mathf.Max(norm.x, minX + 0.005f);
            }
            if (bottom)
            {
                minY = Mathf.Min(norm.y, maxY - 0.005f);
            }
            if (top)
            {
                maxY = Mathf.Max(norm.y, minY + 0.005f);
            }

            var newBox = Rect.MinMaxRect(minX, minY, maxX, maxY);

            float ow = Mathf.Max(_dragBox.width, 1e-4f);
            float oh = Mathf.Max(_dragBox.height, 1e-4f);

            var poly = list[_selShape];
            for (int i = 0; i < poly.Count && i < _dragPoly.Count; i++)
            {
                Vector2 src = _dragPoly[i];

                // u/v 的分母和分子都必须是「按下那一刻的基准框」（_dragBox），
                // 顶点坐标也必须来自「按下那一刻的快照」（_dragPoly）。
                // 曾经在这循环后面写了 _dragBox = newBox —— 于是每帧的基准框
                // 都变成上一帧的结果，缩放按帧率**指数级放大或收缩**，
                // 拖一下形状就飞了。这就是「缩放有问题」的根因。
                float u = (src.x - _dragBox.xMin) / ow;
                float v = (src.y - _dragBox.yMin) / oh;

                poly[i] = new Vector2(
                    Mathf.Clamp01(newBox.xMin + u * newBox.width),
                    Mathf.Clamp01(newBox.yMin + v * newBox.height));
            }
        }

        private void DragBackground(Rect r, Event e)
        {
            var l = Layout(_room);
            float px = r.width / DesignW;    // 设计像素 → 屏幕像素

            if (_dragIndex < 0)
            {
                // 平移
                Vector2 d = e.mousePosition - _dragLast;
                _dragLast = e.mousePosition;

                l.bgX += d.x / Mathf.Max(px, 1e-4f);
                l.bgY -= d.y / Mathf.Max(px, 1e-4f);   // GUI y 向下，设计 y 向上
            }
            else
            {
                // 拖角缩放：以画面中心为锚，按横向距离算比例
                Rect b = BgRect(r);
                float half = Mathf.Max(Mathf.Abs(e.mousePosition.x - b.center.x), 1f);
                float scale = (half * 2f) / Mathf.Max(r.width, 1f);
                l.bgScale = Mathf.Clamp(scale, 0.2f, 4f);
            }
        }

        // ==================== 敌人点位数据 ====================

        /// <summary>某只敌人某机位的归一化坐标。没摆过就用运行时的自动布局当默认值。</summary>
        private Vector2 PointNorm(int room, int enemy)
        {
            var l = Layout(room);
            if (l.points != null)
            {
                foreach (var p in l.points)
                {
                    if (p != null && p.enemy == enemy && p.tier == _tier)
                    {
                        return new Vector2(p.x, p.y);
                    }
                }
            }

            // 默认值 = 运行时自动摆的那个点，这样打开窗口看到的和游戏里一致
            Vector2 px = PrototypeScene.MonitorSlotPosition((EnemyId)enemy, room);
            return new Vector2(0.5f + px.x / DesignW, Mathf.Clamp01(px.y / DesignH));
        }

        private void SetPoint(int room, int enemy, Vector2 norm)
        {
            _dirty = true;
            var l = Layout(room);

            if (l.points == null)
            {
                l.points = new LayoutPoint[0];
            }

            foreach (var p in l.points)
            {
                if (p != null && p.enemy == enemy && p.tier == _tier)
                {
                    p.x = Mathf.Clamp01(norm.x);
                    p.y = Mathf.Clamp01(norm.y);
                    if (p.scale <= 0.01f)
                    {
                        p.scale = 1f;
                    }
                    return;
                }
            }

            var list = new List<LayoutPoint>(l.points);
            list.Add(new LayoutPoint
            {
                enemy = enemy,
                tier = _tier,
                x = Mathf.Clamp01(norm.x),
                y = Mathf.Clamp01(norm.y),
                scale = 1f
            });
            l.points = list.ToArray();
        }

        // ==================== 几何 ====================

        private static bool PointInPoly(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y)
                          / (poly[j].y - poly[i].y) + poly[i].x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// 矩形 / 椭圆拖完，落成一块。
        ///
        /// 存成**包围盒的四个角**（左下、右下、右上、左上）——
        /// 数据格式和多边形统一，运行时按 kind 决定填矩形还是填椭圆。
        /// </summary>
        private void FinishBox(Rect r, Event e)
        {
            Vector2 end = ToNorm(r, e.mousePosition);

            float minX = Mathf.Min(_boxStart.x, end.x);
            float maxX = Mathf.Max(_boxStart.x, end.x);
            float minY = Mathf.Min(_boxStart.y, end.y);
            float maxY = Mathf.Max(_boxStart.y, end.y);

            // 太小就是误点，不落块
            if (maxX - minX < 0.005f || maxY - minY < 0.005f)
            {
                return;
            }

            List<List<Vector2>> list;
            if (!_shapes.TryGetValue(_room, out list))
            {
                list = new List<List<Vector2>>();
                _shapes[_room] = list;
            }

            list.Add(new List<Vector2>
            {
                new Vector2(minX, minY),
                new Vector2(maxX, minY),
                new Vector2(maxX, maxY),
                new Vector2(minX, maxY),
            });

            SetKind(_room, list.Count - 1, (int)_tool);
            _selShape = list.Count - 1;
            _selVertex = -1;
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

        /// <summary>
        /// 删除。
        ///
        /// 分两级：**选中了某个顶点就只删那个顶点**（形状保留，少了那个点）；
        /// 没选顶点才删整块。
        /// 之前只能删整块 —— 因为压根没有"选中顶点"这个状态，
        /// 想微调一个点只能整块删掉重画。
        /// </summary>
        /// <summary>
        /// 在「离鼠标最近的那条边」上插入一个顶点。
        ///
        /// 用途：形状少了拐角时补点。没有它只能整块删掉重画。
        /// 新点插在最近边的中点（不是鼠标位置）——
        /// 插在中点不会破坏已有轮廓，之后再拖到鼠标处即可。
        /// </summary>
        private void InsertVertexNearMouse()
        {
            List<List<Vector2>> list;
            if (!_shapes.TryGetValue(_room, out list) || _selShape < 0 || _selShape >= list.Count)
            {
                return;
            }

            var poly = list[_selShape];
            if (poly.Count < 2)
            {
                return;
            }

            Vector2 mouse = Event.current != null
                ? Event.current.mousePosition
                : Vector2.zero;

            // 在归一化空间里找最近的边（用归一化距离，和屏幕无关）
            int bestEdge = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Count];

                // 鼠标位置 → 归一化（用 ImageRect 换算）
                Vector2 mn = ToNorm(ImageRect(), mouse);

                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 < 1e-9f ? 0f : Mathf.Clamp01(Vector2.Dot(mn - a, ab) / len2);
                Vector2 proj = a + ab * t;

                float d = (mn - proj).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestEdge = i;
                }
            }

            if (bestEdge < 0)
            {
                return;
            }

            Vector2 p1 = poly[bestEdge];
            Vector2 p2 = poly[(bestEdge + 1) % poly.Count];
            Vector2 mid = (p1 + p2) * 0.5f;

            poly.Insert(bestEdge + 1, mid);
            _selVertex = bestEdge + 1;    // 插完直接选中，方便接着拖
        }
        private void DeleteSelected()
        {
            List<List<Vector2>> list;
            if (!_shapes.TryGetValue(_room, out list) || _selShape < 0 || _selShape >= list.Count)
            {
                return;
            }

            var poly = list[_selShape];

            // 优先级 1：选中了顶点 → 只删这个顶点
            if (_selVertex >= 0 && _selVertex < poly.Count)
            {
                // 少于 3 个点就不成多边形了，这时才整块删
                if (poly.Count <= 3)
                {
                    list.RemoveAt(_selShape);
                    _selShape = -1;
                }
                else
                {
                    poly.RemoveAt(_selVertex);
                }

                _selVertex = -1;
                _dragKind = 0;
                Repaint();
                return;
            }

            // 优先级 2：没选顶点 → 删整块
            list.RemoveAt(_selShape);
            _selShape = -1;
            _selVertex = -1;
            _dragKind = 0;
            Repaint();
        }

        // ==================== 序列化结构（JsonUtility 要 public 字段）====================

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
            /// <summary>0=多边形 1=矩形 2=椭圆（缺省 0，兼容老数据）</summary>
            public int kind;

            /// <summary>x0, y0, x1, y1, ...（归一化，左下原点）</summary>
            public float[] points;
        }

        // 字段名必须和 PrototypeScene 里的 MonitorLayout* 完全一致

        [System.Serializable]
        private class LayoutDoc
        {
            public LayoutRoom[] rooms;
        }

        [System.Serializable]
        private class LayoutRoom
        {
            public int room;
            public float bgX;
            public float bgY;
            public float bgScale = 1f;
            public LayoutPoint[] points;
        }

        [System.Serializable]
        private class LayoutPoint
        {
            public int enemy;

            /// <summary>0 = 近档点位，1 = 远档点位（缺省 0，老数据当近档）</summary>
            public int tier;

            public float x = 0.5f;
            public float y = 0.06f;
            public float scale = 1f;
        }
    }
}
