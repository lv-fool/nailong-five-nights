using UnityEngine;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 桌沿上那部手机 —— 一个**钉在世界里**的 3D 物件，不跟着视角走。
    ///
    /// 玩家转头把它转到画面中间时它会亮起来（自发光 + 一点点呼吸），
    /// 这时点它才接通；转开就暗下去。这样它像屋里的东西，而不是贴在屏幕上的按钮。
    ///
    /// 模型：`Resources/NaibaoPhone/naibao_phone`（由 `奶包/naibao_relief_v2.glb`
    /// 经 Blender 减面到 6 万面后导出，见 `.setup/conv_phone.py`）。原始模型高 1 米，
    /// 这里按米缩到手机大小。
    /// </summary>
    public class NaibaoPhoneProp : MonoBehaviour
    {
        /// <summary>Resources 里的路径（不带扩展名）。</summary>
        public const string ResourcePath = "NaibaoPhone/naibao_phone";

        /// <summary>
        /// 摆在桌上哪儿：世界坐标，米。相机在 (0, 1.30, −1.20)，朝 +z。
        /// 想挪位置就改这里（冒烟脚本也靠它把手机摆到正前方）。
        /// </summary>
        public static Vector3 WorldPosition = new Vector3(0.24f, 1.02f, 0.00f);

        /// <summary>模型自身高 1 米，缩到 19 厘米。</summary>
        public const float Scale = 0.19f;

        /// <summary>手机贴图（从 GLB 的 baseColor 里导出来的那一张）。</summary>
        public const string TexturePath = "NaibaoPhone/naibao_phone_color";

        /// <summary>相机朝向和手机方向的夹角小于多少度算「转到它了」。</summary>
        public const float HighlightAngle = 15f;

        /// <summary>
        /// 模型摆正之后再绕竖轴翻多少度。
        /// FBX 没法告诉我们「哪一面才是屏幕」，长轴 / 薄轴能算出来，正反面只能靠看一眼定。
        /// </summary>
        public const float FlipYaw = 180f;

        /// <summary>亮起来的颜色。</summary>
        private static readonly Color HighlightColor = new Color(1f, 0.82f, 0.42f, 1f);

        private MeshRenderer[] _renderers;
        private Material _material;
        private float _glow;
        private float _pulse;
        private bool _highlighted;

        /// <summary>摆一部手机进来。缺模型时返回 null，调用方按「没有手机」处理。</summary>
        public static NaibaoPhoneProp Spawn(Transform parent)
        {
            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning("[奶龙] 缺手机模型 Resources/" + ResourcePath +
                                 ".fbx，先跑一次 .setup/conv_phone.py");
                return null;
            }

            var go = Instantiate(prefab);
            go.name = "NaibaoPhone";
            go.transform.SetParent(parent, false);
            go.transform.position = WorldPosition;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * Scale;

            var prop = go.AddComponent<NaibaoPhoneProp>();
            prop.Bind();
            return prop;
        }

        private void Bind()
        {
            AutoOrient();

            _renderers = GetComponentsInChildren<MeshRenderer>();

            // 模型自带的那份材质是靠 FBX 里的贴图路径找图的，导出时没内嵌，
            // 所以这里按「项目自己的 unlit 贴图 shader + 单独导出的贴图」重做一份：
            // 办公室没有实时光照，用 Standard 会渲染成黑的。
            var bankMaterial = Resources.Load<Material>("PanoMaterial");
            Shader shader = bankMaterial != null
                ? bankMaterial.shader
                : Shader.Find("NailongFNAF/UnlitTexture");

            var texture = Resources.Load<Texture2D>(TexturePath);

            if (shader == null)
            {
                Debug.LogWarning("[奶龙] 找不到 unlit shader，手机可能渲染成粉红");
            }
            else
            {
                _material = new Material(shader);
                _material.name = "NaibaoPhoneMaterial";

                if (texture != null)
                {
                    _material.mainTexture = texture;
                }
                else
                {
                    Debug.LogWarning("[奶龙] 缺手机贴图 Resources/" + TexturePath);
                }

                _material.SetColor("_Color", Color.white);

                for (int i = 0; i < _renderers.Length; i++)
                {
                    _renderers[i].sharedMaterial = _material;
                }
            }

        }

        /// <summary>
        /// 把模型摆成「立着、屏幕朝相机」。
        ///
        /// 模型从 GLB 经 Blender 转成 FBX 之后，三根轴跟 Unity 的 y-up 对不上：
        /// 手机的长轴落在某一根水平轴上、屏幕法线落在另一根上。硬写一个 yaw 很容易
        /// 换一台机器就歪掉，所以这里直接量：**最长的轴抬成 +Y（立起来）**，
        /// **最薄的轴转成 -Z（冲着相机）**，剩下的轴自然就是屏幕的横轴。
        /// </summary>
        private void AutoOrient()
        {
            var filter = GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return;
            }

            Vector3 size = filter.sharedMesh.bounds.size;

            int upAxis = 0;
            if (size.y > size[upAxis]) upAxis = 1;
            if (size.z > size[upAxis]) upAxis = 2;

            int thinAxis = 0;
            if (size.y < size[thinAxis]) thinAxis = 1;
            if (size.z < size[thinAxis]) thinAxis = 2;

            if (upAxis == thinAxis)
            {
                return;
            }

            // 模型自己的三根轴（世界方向）：filter 已经带着 FBX 导入时的旋转
            Quaternion modelRotation = filter.transform.rotation;
            Vector3 modelUp = modelRotation * AxisVector(upAxis);
            Vector3 modelThin = modelRotation * AxisVector(thinAxis);
            // 想要的三根轴：立起来、屏幕朝相机（−Z）、另一根做横轴
            Vector3 wantedUp = Vector3.up;
            Vector3 wantedThin = Vector3.back;

            Quaternion from = Quaternion.LookRotation(modelThin, modelUp);
            Quaternion to = Quaternion.LookRotation(wantedThin, wantedUp);

            transform.rotation = to * Quaternion.Inverse(from);
            transform.Rotate(Vector3.up, FlipYaw, Space.World);
        }

        private static Vector3 AxisVector(int axis)
        {
            return axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
        }

        /// <summary>有人在看它就亮一点；没人看就暗掉。</summary>
        public void SetHighlighted(bool highlighted)
        {
            _highlighted = highlighted;
        }

        /// <summary>
        /// 这一帧鼠标点是否落在这台手机上。
        ///
        /// 用**投影矩形**而不是射线：镜头是跟着鼠标转的，玩家几乎不可能把光标
        /// 稳稳停在一条几厘米宽的手机上（光标一动镜头就转，手机跟着跑）。
        /// 矩形给了一点边距，手感才是「指着它点一下」。
        /// </summary>
        public bool ContainsScreenPoint(Camera cam, Vector3 screenPoint)
        {
            if (cam == null || _renderers == null || _renderers.Length == 0)
            {
                return false;
            }

            Bounds bounds = _renderers[0].bounds;
            for (int i = 1; i < _renderers.Length; i++)
            {
                bounds.Encapsulate(_renderers[i].bounds);
            }

            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = bounds.center + new Vector3(
                    (c & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                    (c & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                    (c & 4) == 0 ? -bounds.extents.z : bounds.extents.z);

                Vector3 projected = cam.WorldToScreenPoint(corner);
                if (projected.z <= 0f)
                {
                    return false;
                }

                min = Vector2.Min(min, projected);
                max = Vector2.Max(max, projected);
            }

            const float margin = 28f;
            return screenPoint.x >= min.x - margin && screenPoint.x <= max.x + margin
                && screenPoint.y >= min.y - margin && screenPoint.y <= max.y + margin;
        }

        private void Update()
        {
            float target = _highlighted ? 1f : 0f;
            _glow = Mathf.MoveTowards(_glow, target, Time.unscaledDeltaTime * 4f);

            // 呼吸：亮着的时候轻微起伏，暗着的时候是纯白 —— 一眼能看出「现在能点」
            _pulse = _highlighted
                ? 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * 3.4f)
                : 0f;

            if (_material != null)
            {
                // 高亮 = 往暖黄里推一点，而不是简单加亮：
                // 加了亮度在这套黑房间里会像漏光，推色相才像「被盯上了」
                Color tint = Color.Lerp(Color.white, HighlightColor, _glow * _pulse * 0.55f);
                _material.SetColor("_Color", tint);
            }

            float scale = Scale * (1f + 0.02f * _glow * Mathf.Sin(Time.unscaledTime * 3.4f));
            transform.localScale = new Vector3(scale, scale, scale);
        }
    }
}
