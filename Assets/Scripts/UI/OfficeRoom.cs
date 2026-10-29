using NailongFNAF.Gameplay;
using UnityEngine;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 办公室的「真镜头转动」：全景条贴在一根水平朝里的圆柱内壁上，相机放在
    /// 圆柱中轴上绕竖直轴转。转头就是相机真的在转，而不是两张图在交叉淡化。
    ///
    /// 全景条由 <c>tools/render_pano.py</c> 生成（<c>Assets/Resources/office_pano.png</c>），
    /// 记的是「从固定眼点出发，每个方向看到什么」—— 列 = 偏航角，行 = 斜率。
    /// 圆柱上任意一点的方向，恰好就是相机到那点的射线方向，所以相机对准它
    /// 看到的正是当时那个方向该有的样子：近的桌角转过去就是比远处后墙划过
    /// 更多，透视跟真 3D 一字不差（不存在「圆柱没视差」的问题）。
    /// 转头是连续的 —— 相机每一帧都在转，不是一格格跳。
    ///
    /// <c>render_pano.py</c> 里我按针孔投影做了反向校验：把全景条重建成正前方
    /// 视角，跟直接渲的正前视图模糊后只差 2%，纵横亮度曲线相关度 1.0000。
    /// </summary>
    public class OfficeRoom : MonoBehaviour
    {
        // ⚠️ 这三个数必须和 tools/render_pano.py 的 YAW_MIN / YAW_MAX / SLOPE 一致，
        // 改一处必须改另一处，否则相机转到边缘会露出圆柱之外的空。
        private const float YawMin = -145f;
        private const float YawMax = 145f;
        private const float SlopeMax = 0.72f;

        private const float EyeHeight = 1.30f;
        private const float EyeZ = -1.20f;

        /// <summary>
        /// 满转角（度）。⚠️ **必须和 tools/gen_placeholder_art.py 的 `TURN_MAX_YAW_DEG` 一致** ——
        /// 门片是逐帧渲的，`ApplyDoors` 按 `|pan| × (帧数-1)` 取帧，末帧必须正好是
        /// 相机 yaw = 这个角度时渲的那张，否则门片会和全景里的门洞错开。
        ///
        /// 70° 是 2026-10-15 定的：门开在座位旁边（世界偏航角 ±77°~103°）之后，
        /// 转到七成门就进画面，摆动幅度也比原来的 90° 含蓄。
        /// </summary>
        private const float MaxYaw = 70f;

        private const float Radius = 8f;

        /// <summary>水平视场 85°（照渲染脚本的 FOV_H）在 16:9 下折算出的垂直视场角。</summary>
        private const float BaseVFov = 54.49f;

        private Camera _cam;
        private GameObject _shell;
        private float _appliedVFov = -1f;

        /// <summary>
        /// 房间相机。墙上按钮要按世界点投影到屏幕上，得借它来算（见
        /// <c>PrototypeScene.TryProjectToCanvas</c>）。
        /// </summary>
        public Camera Camera
        {
            get { return _cam; }
        }

        private void Awake()
        {
            EnsureCamera();
            BuildPanorama();
            BuildPosters();
            SetPan(0f);
        }

        /// <summary>
        /// 把墙上插画贴到圆柱内壁。
        ///
        /// 位置由 <see cref="PosterLayout"/> 给（角度 + 高度 + 宽度），
        /// 摆法和全景条一致：yaw 决定 (x,z)，高度直接给 y。
        /// 每张是个独立的四边形面片，比圆柱内壁**往里缩 2 厘米**避免 z-fighting。
        ///
        /// 贴图缺失的条目直接跳过 —— 少一张画不该让整个房间崩。
        /// </summary>
        private void BuildPosters()
        {
            var layout = PosterLayout.Load();

            if (layout == null || layout.Posters == null || layout.Posters.Count == 0)
            {
                return;
            }

            var root = new GameObject("OfficePosters");
            root.transform.SetParent(transform, false);

            var center = new Vector3(0f, EyeHeight, EyeZ);
            const float Inset = 0.02f;   // 往里缩一点，盖在全景条前面

            foreach (var poster in layout.Posters)
            {
                if (poster == null || string.IsNullOrEmpty(poster.Image))
                {
                    continue;
                }

                var texture = Resources.Load<Texture2D>(
                    PosterLayout.ImageFolder + poster.Image);

                if (texture == null)
                {
                    Debug.LogWarning("[奶龙] 缺插画 Resources/" +
                        PosterLayout.ImageFolder + poster.Image);
                    continue;
                }

                // 高按原图比例算 —— 不拉伸
                float aspect = texture.height / (float)texture.width;
                float w = Mathf.Max(0.05f, poster.Width);
                float h = w * aspect;

                float yawRad = poster.Yaw * Mathf.Deg2Rad;
                float r = Radius - Inset;

                var go = new GameObject("Poster_" + poster.Image);
                go.transform.SetParent(root.transform, false);

                // 面的中心点：贴在圆柱内壁上，朝向圆心
                Vector3 pos = new Vector3(
                    center.x + r * Mathf.Sin(yawRad),
                    poster.Height,
                    center.z + r * Mathf.Cos(yawRad));

                go.transform.position = pos;

                // 朝向圆心（圆柱中轴）—— 法线指向房间里
                go.transform.rotation = Quaternion.Euler(0f, poster.Yaw + 180f, 0f);

                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();

                mf.sharedMesh = MakeQuadMesh(w, h);

                // 和全景条用同一个 unlit shader（办公室没有实时光照，
                // Standard 会渲成全黑）
                var bankMaterial = Resources.Load<Material>("PanoMaterial");
                Shader shader = bankMaterial != null
                    ? bankMaterial.shader
                    : Shader.Find("NailongFNAF/UnlitTexture");

                if (shader == null)
                {
                    continue;
                }

                var mat = new Material(shader);
                mat.mainTexture = texture;
                mat.SetColor("_Color", new Color(1f, 1f, 1f,
                    Mathf.Clamp01(poster.Alpha <= 0f ? 1f : poster.Alpha)));

                mr.sharedMaterial = mat;
                mr.sortingOrder = 1;      // 画在全景条上面
            }
        }

        /// <summary>一个居中的矩形面片（法线 +z，绕序顺时针）。</summary>
        private static Mesh MakeQuadMesh(float w, float h)
        {
            var mesh = new Mesh();
            mesh.name = "PosterQuad";

            float hw = w * 0.5f;
            float hh = h * 0.5f;

            mesh.vertices = new[]
            {
                new Vector3(-hw, -hh, 0f),
                new Vector3(-hw,  hh, 0f),
                new Vector3( hw,  hh, 0f),
                new Vector3( hw, -hh, 0f),
            };

            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
            };

            // ⚠️ 顺时针（从 +z 方向看）—— 反了会被背面剔除，整张画消失
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void EnsureCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Office Camera");
                _cam = go.AddComponent<Camera>();
                go.tag = "MainCamera";
            }

            _cam.orthographic = false;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 60f;

            // 相机必须正好落在圆柱中轴上：位置偏一点就不是「转头」而是「平移」了。
            _cam.transform.position = new Vector3(0f, EyeHeight, EyeZ);
            _cam.transform.rotation = Quaternion.identity;
        }

        /// <summary>
        /// 造圆柱内壁。列对应偏航角、行对应斜率 p；高度 = 眼高 + 半径 × p，
        /// 这样「相机沿某个方向看过去」正好落在贴图对得上的那一格上。
        /// </summary>
        private void BuildPanorama()
        {
            _shell = new GameObject("OfficePanorama");
            _shell.transform.SetParent(transform, false);

            var meshFilter = _shell.AddComponent<MeshFilter>();
            _shell.AddComponent<MeshRenderer>();

            const int Cols = 192;
            const int Rows = 96;
            var center = new Vector3(0f, EyeHeight, EyeZ);

            var verts = new Vector3[(Cols + 1) * (Rows + 1)];
            var uvs = new Vector2[verts.Length];

            for (int r = 0; r <= Rows; r++)
            {
                // 行 0 是画面顶行 → p = +Slope，行 Rows 是底行 → p = -Slope。
                float slope = SlopeMax * (1f - 2f * (r / (float)Rows));
                float y = center.y + Radius * slope;
                float v = 1f - r / (float)Rows;

                for (int c = 0; c <= Cols; c++)
                {
                    float s = c / (float)Cols;
                    float yaw = (YawMin + (YawMax - YawMin) * s) * Mathf.Deg2Rad;

                    verts[r * (Cols + 1) + c] = new Vector3(
                        center.x + Radius * Mathf.Sin(yaw),
                        y,
                        center.z + Radius * Mathf.Cos(yaw));
                    uvs[r * (Cols + 1) + c] = new Vector2(s, v);
                }
            }

            var tris = new int[Cols * Rows * 6];
            int n = 0;
            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                int a = r * (Cols + 1) + c;     // 上·左
                int b = a + 1;                  // 上·右
                int d = a + Cols + 1;           // 下·左
                int e = d + 1;                  // 下·右

                // ⚠️ 绕序必须是**顺时针**（Unity 判正面的约定）—— 相机在圆柱**里面**，
                // 看到的永远是内壁，只有顺时针绕出来的正面才朝向相机。绕反了整面会被
                // 背面剔除掉，屏幕全黑。别想着用材质 `_Cull` 关剔除：内置
                // `Unlit/Texture` 根本没声明 `_Cull` 属性，设了也不生效，只能改绕序。
                tris[n++] = a;
                tris[n++] = b;
                tris[n++] = d;
                tris[n++] = b;
                tris[n++] = e;
                tris[n++] = d;
            }

            var mesh = new Mesh { name = "OfficePanoramaCylinder" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;

            var texture = Resources.Load<Texture2D>("office_pano");
            if (texture == null)
            {
                // 缺图就只留黑 —— 门、立绘、监控这些照旧工作，别让房间把界面弄崩
                Debug.LogWarning("[奶龙] 缺全景贴图 Assets/Resources/office_pano.png，" +
                                 "先跑一次工具：tools/render_pano.py");
                _shell.SetActive(false);
                return;
            }

            // 用 Resources 里的材质（PanoMaterial.mat）—— 材质引用的 shader 和贴图
            // 必然进包，绕开「内置 shader 被剔除 / Shader.Find 返回 null」的老问题。
            // 材质由工具脚本生成：Assets/Resources/PanoMaterial.mat（引用项目自带
            // NailongFNAF/UnlitTexture + office_pano.png）。
            var material = Resources.Load<Material>("PanoMaterial");

            if (material == null)
            {
                Debug.LogWarning("[奶龙] 缺材质 Assets/Resources/PanoMaterial.mat，" +
                                 "检查生成脚本（Editor 菜单）是否跑过");
                _shell.SetActive(false);
                return;
            }

            _shell.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// -1 满左、0 正前方、+1 满右。直接转相机，不做插值 —— 平滑由调用方的
        /// <c>Mathf.SmoothDamp</c> 负责，和原来镜头序列用的是同一套缓动。
        /// </summary>
        public void SetPan(float pan)
        {
            if (_cam == null)
            {
                return;
            }

            float yaw = Mathf.Clamp(pan, -1f, 1f) * MaxYaw;
            _cam.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            ApplyFieldOfView();
        }

        /// <summary>
        /// 视角照渲染时的 16:9 水平 85° 定。窗口拉宽了水平视场随之变大，得夹住，
        /// 否则超广角会看到全景条（±145°）之外的空。边界：满转 <see cref="MaxYaw"/> + 半视场
        /// 不能超过 145°，所以半视场最多 145 − 70 = 75°，取 70° 留一截余量。
        /// 竖向只剩 BaseVFov 一途，只会被压小 —— 全景上下各留到 35.75°，够用。
        /// </summary>
        private void ApplyFieldOfView()
        {
            float aspect = _cam.aspect > 0.01f ? _cam.aspect : 16f / 9f;

            const float MaxHalfHFov = 70f;                          // 70° 满转 + 70° ≤ 145° 全景边界
            float tanHalfVMax = Mathf.Tan(MaxHalfHFov * Mathf.Deg2Rad) / aspect;
            float maxVFov = 2f * Mathf.Atan(tanHalfVMax) * Mathf.Rad2Deg;
            float vfov = Mathf.Min(BaseVFov, maxVFov);

            if (Mathf.Abs(vfov - _appliedVFov) > 0.01f)
            {
                _cam.fieldOfView = vfov;
                _appliedVFov = vfov;
            }
        }
    }
}
