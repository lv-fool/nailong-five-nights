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

        public Texture Texture
        {
            get { return _texture; }
            set
            {
                _texture = value;

                // ⚠️ Graphic.mainTexture 是**只读**的 —— 要换图得写到 material 上。
                // UGUI 默认会复用共享的 UI 材质，所以这里必须先 new 一份自己的，
                // 否则改一张图会把所有 QuadGraphic 的贴图一起改掉。
                if (material == null || material == defaultMaterial)
                {
                    material = new Material(Shader.Find("UI/Default"));
                }

                material.mainTexture = value;
                SetVerticesDirty();
            }
        }

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
        /// 四角形状没有单一「尺寸」概念 —— 用外接框，
        /// 让 UGUI 的射线检测 / 布局至少有个合理边界。
        /// </summary>
        public override void SetMaterialDirty()
        {
            base.SetMaterialDirty();
        }
    }
}
