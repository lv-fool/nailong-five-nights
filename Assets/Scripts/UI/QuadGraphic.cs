using UnityEngine;
using UnityEngine.UI;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 可以**任意四角顶点**的图片控件 —— 贴图上墙时用。
    ///
    /// 为什么不用 Image：Image 只能画正矩形（或简单旋转），
    /// 想让它「透视倾斜」（比如贴纸贴在斜着的墙上）做不到。
    /// 这里重写 <see cref="OnPopulateMesh"/> 直接按四个角的坐标生成两个三角形，
    /// 于是可以自由拖每一个角，做出任何四边形。
    ///
    /// 四个角按**顺时针**给：左下 → 左上 → 右上 → 右下。
    /// UV 固定 (0,0) (0,1) (1,1) (1,0)，和图片方向一致。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class QuadGraphic : MaskableGraphic
    {
        /// <summary>四个角相对自身中心的偏移（本地像素坐标）。</summary>
        private readonly Vector2[] _corners = new Vector2[4];

        /// <summary>
        /// 贴图。用 Texture 而不是 Sprite —— 这些 jpg 是按普通贴图导入的
        /// （没有 Sprite 元数据），套 Sprite 反而多一层。
        /// </summary>
        private Texture _texture;

        /// <summary>
        /// 覆盖 Graphic.mainTexture —— UGUI 渲染时读的是**这个属性**，
        /// 不是 material.mainTexture。
        ///
        /// 默认实现返回 s_WhiteTexture，所以只设 material.mainTexture 的话
        /// 画面依然是一片白（踩过）。
        /// </summary>
        public override Texture mainTexture
        {
            get { return _texture != null ? _texture : s_WhiteTexture; }
        }

        public Texture Texture
        {
            get { return _texture; }
            set
            {
                _texture = value;

                // ⚠️ 用**自建的材质**，别去改共享的默认材质 ——
                // 改了会把所有 QuadGraphic 的贴图一起换掉。
                //
                // 也不能用「material == defaultMaterial」判断是否已自建：
                // 没设过 material 时 material 就**返回** defaultMaterial，
                // 那个比较永远成立，于是每次设贴图都 new 一个材质（泄漏）。
                if (_ownMaterial == null)
                {
                    var shader = Shader.Find("UI/Default");

                    if (shader == null)
                    {
                        shader = Shader.Find("Unlit/Transparent");
                    }

                    if (shader == null)
                    {
                        Debug.LogWarning("[奶龙] QuadGraphic 找不到 UI shader");
                        return;
                    }

                    _ownMaterial = new Material(shader);
                    _ownMaterial.name = "QuadGraphic (instance)";
                }

                _ownMaterial.mainTexture = value;

                // ⚠️ 每次都重新赋一次 material —— UGUI 内部有材质缓存，
                // 只改 _ownMaterial 的内容（换贴图）不重新赋值的话，
                // 批次/材质指针还是老的，画面继续显示**上一张贴图**
                // （表现就是「所有机位都显示同一张图」）。
                material = _ownMaterial;

                SetMaterialDirty();
                SetVerticesDirty();
            }
        }

        /// <summary>自己 new 出来的材质（复用同一个实例，避免每次设贴图都泄漏）。</summary>
        private Material _ownMaterial;

        /// <summary>
        /// 设置四个角。顺序：左下、左上、右上、右下（顺时针）。
        /// 坐标是**相对控件中心**的本地像素。
        /// </summary>
        public void SetCorners(Vector2 bottomLeft, Vector2 topLeft,
            Vector2 topRight, Vector2 bottomRight)
        {
            _corners[0] = bottomLeft;
            _corners[1] = topLeft;
            _corners[2] = topRight;
            _corners[3] = bottomRight;

            RefreshBounds();
            SetVerticesDirty();
        }

        /// <summary>
        /// 按「中心 + 尺寸 + 倾斜」构造一个四角形状。
        ///
        /// <paramref name="skewX"/> / <paramref name="skewY"/> 是上下边的水平错位量，
        /// 用来做透视倾斜；0 就是普通矩形。
        /// </summary>
        public void SetCornersFromSize(float width, float height, float skewX, float skewY)
        {
            float hw = width * 0.5f;
            float hh = height * 0.5f;

            // 左下、左上、右上、右下
            SetCorners(
                new Vector2(-hw, -hh),
                new Vector2(-hw + skewX, hh),
                new Vector2(hw + skewX, hh),
                new Vector2(hw, -hh));
        }

        /// <summary>取回四个角（编辑器做手柄用）。</summary>
        public Vector2 GetCorner(int index)
        {
            return _corners[Mathf.Clamp(index, 0, 3)];
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            // UV 顺序和 _corners 一致：左下 (0,0)、左上 (0,1)、右上 (1,1)、右下 (1,0)
            var uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
            };

            var color32 = color;

            for (int i = 0; i < 4; i++)
            {
                vh.AddVert(_corners[i], color32, uv[i]);
            }

            // ⚠️ 绕序**顺时针**（从 +z 方向看）——
            // UGUI 默认不剔除，但反了在某些 shader 下会看不到。
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }

        /// <summary>
        /// 把 rectTransform 的尺寸同步成四角的包围盒。
        ///
        /// ⚠️ **必须做**：UGUI 用 rectTransform.rect 做剔除和射线检测边界。
        /// 我们的形状完全由顶点决定、rectTransform 本来是零尺寸，
        /// 于是 UGUI 认为整个控件空无一物，**整块剔除不画** ——
        /// 表现就是「贴图看不见 / 只剩一片白」。
        /// </summary>
        public void RefreshBounds()
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < 4; i++)
            {
                if (_corners[i].x < minX) { minX = _corners[i].x; }
                if (_corners[i].y < minY) { minY = _corners[i].y; }
                if (_corners[i].x > maxX) { maxX = _corners[i].x; }
                if (_corners[i].y > maxY) { maxY = _corners[i].y; }
            }

            if (rectTransform != null)
            {
                rectTransform.sizeDelta = new Vector2(maxX - minX, maxY - minY);
            }
        }

        /// <summary>
        /// 四角形状没有单一「尺寸」概念 —— 用外接框，
        /// 让 UGUI 的射线检测 / 布局至少有个合理边界。
        /// </summary>
        public override void SetMaterialDirty()
        {
            base.SetMaterialDirty();
        }
    }
}
