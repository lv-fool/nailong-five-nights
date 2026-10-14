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
        private const float MaxYaw = 90f;
        private const float Radius = 8f;

        /// <summary>水平视场 85°（照渲染脚本的 FOV_H）在 16:9 下折算出的垂直视场角。</summary>
        private const float BaseVFov = 54.49f;

        private Camera _cam;
        private GameObject _shell;
        private float _appliedVFov = -1f;

        private void Awake()
        {
            EnsureCamera();
            BuildPanorama();
            SetPan(0f);
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
                int a = r * (Cols + 1) + c;
                int b = a + 1;
                int d = a + Cols + 1;
                int e = d + 1;

                tris[n++] = a;
                tris[n++] = d;
                tris[n++] = b;
                tris[n++] = b;
                tris[n++] = d;
                tris[n++] = e;
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

            // 相机在圆柱**里面**，照到的是背面；关掉背面剔除就不用纠结三角面绕序。
            var material = new Material(Shader.Find("Unlit/Texture"))
            {
                mainTexture = texture
            };
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

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
        /// 否则超广角会看到全景条（±145°）之外的空。边界：满转 90° + 半视场
        /// 不能超过 145°，所以半视场最多 55°，取 52° 留一截余量。
        /// 竖向只剩 BaseVFov 一途，只会被压小 —— 全景上下各留到 35.75°，够用。
        /// </summary>
        private void ApplyFieldOfView()
        {
            float aspect = _cam.aspect > 0.01f ? _cam.aspect : 16f / 9f;

            const float MaxHalfHFov = 52f;                          // 90° 满转 + 52° ≤ 145° 全景边界
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
