using System.Collections.Generic;
using NailongFNAF.Gameplay;
using UnityEngine;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 桌沿上那部手机 —— 一个**钉在世界里**的 3D 物件，不跟着视角走。
    ///
    /// 玩家转头把它转到画面中间时它会亮起来（自发光 + 一点点呼吸），
    /// 这时点它才接通；转开就暗下去。这样它像屋里的东西，而不是贴在屏幕上的按钮。
    ///
    /// 屏上的人有**四张脸**：平常一张，通话时按台词换（笑 / 撇嘴 / 坏笑）。
    /// 它们是四份独立的网格（AI 生成的模型没有骨骼和表情通道，换脸只能换网格），
    /// 每份都经 Blender 减面到一万五千面，见 `.setup/conv_phone_faces.py`。
    /// 原始模型高 1 米，这里按米缩到手机大小。
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

        /// <summary>相机朝向和手机方向的夹角小于多少度算「转到它了」。</summary>
        public const float HighlightAngle = 15f;

        /// <summary>
        /// 模型摆正之后再绕竖轴翻多少度。
        /// FBX 没法告诉我们「哪一面才是屏幕」，长轴 / 薄轴能算出来，正反面只能靠看一眼定。
        /// </summary>
        public const float FlipYaw = 180f;

        /// <summary>亮起来的颜色。</summary>
        private static readonly Color HighlightColor = new Color(1f, 0.82f, 0.42f, 1f);

        /// <summary>一张脸：网格 + 贴图在同一个 Resources 前缀下。</summary>
        private struct Face
        {
            public string Name;
            public GameObject Go;
            public MeshRenderer[] Renderers;
            public Material Material;
        }

        private readonly List<Face> _faces = new List<Face>();
        private int _active = -1;
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

            var go = new GameObject("NaibaoPhone");
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
            // 四张脸各是一个子物体，同一时刻只开一张。
            // 缺哪一张就跳过 —— 差分是锦上添花，不该让手机整台消失。
            AddFace(ResourcePath, ResourcePath + "_color");
            AddFace(ResourcePath + "_happy", ResourcePath + "_happy");
            AddFace(ResourcePath + "_pout", ResourcePath + "_pout");
            AddFace(ResourcePath + "_smirk", ResourcePath + "_smirk");

            if (_faces.Count == 0)
            {
                Debug.LogWarning("[奶龙] 手机的四张脸一张都没加载出来");
                return;
            }

            ShowIdle();
        }

        private void AddFace(string modelPath, string texturePath)
        {
            var prefab = Resources.Load<GameObject>(modelPath);
            if (prefab == null)
            {
                return;
            }

            var go = Instantiate(prefab);
            go.name = "Face_" + System.IO.Path.GetFileName(modelPath);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            AlignToCamera(go.transform);

            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
            {
                Destroy(go);
                return;
            }

            // FBX 里那份材质靠贴图路径找图，导出时没内嵌；这里按项目自己的
            // unlit 贴图 shader 重做 —— 办公室没有实时光照，Standard 会渲染成黑的。
            var bankMaterial = Resources.Load<Material>("PanoMaterial");
            Shader shader = bankMaterial != null
                ? bankMaterial.shader
                : Shader.Find("NailongFNAF/UnlitTexture");

            Material material = null;
            if (shader == null)
            {
                Debug.LogWarning("[奶龙] 找不到 unlit shader，手机可能渲染成粉红");
            }
            else
            {
                material = new Material(shader);
                material.name = "NaibaoPhone" + System.IO.Path.GetFileName(texturePath);

                var texture = Resources.Load<Texture2D>(texturePath);
                if (texture != null)
                {
                    material.mainTexture = texture;
                }

                material.SetColor("_Color", Color.white);
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].sharedMaterial = material;
                }
            }

            go.SetActive(false);

            _faces.Add(new Face
            {
                Name = modelPath,
                Go = go,
                Renderers = renderers,
                Material = material,
            });
        }

        /// <summary>
        /// 换一张脸。同一张就不动 —— 每帧调也不会重建。
        /// <paramref name="force"/> 给初始化用：第一次要连激活一起做。
        /// </summary>
        public void SetFace(NaibaoPose pose)
        {
            SetFaceIndex(IndexOf(pose));
        }

        /// <summary>平常那张脸（没在通话时就是它）。</summary>
        public void ShowIdle()
        {
            SetFaceIndex(0);
        }

        private void SetFaceIndex(int index)
        {
            if (_faces.Count == 0)
            {
                return;
            }

            if (index < 0 || index >= _faces.Count)
            {
                index = 0;
            }

            if (index == _active)
            {
                return;
            }

            _active = index;
            for (int i = 0; i < _faces.Count; i++)
            {
                _faces[i].Go.SetActive(i == index);
            }
        }

        /// <summary>
        /// 哪张脸对应哪种姿势。
        ///
        /// 现在只有三张「有表情」的网格（笑 / 撇嘴 / 坏笑），五种植装里有两种共用：
        /// 得意和探身都用坏笑，不耐烦和垮脸都用撇嘴。
        /// </summary>
        private int IndexOf(NaibaoPose pose)
        {
            string want;
            switch (pose)
            {
                case NaibaoPose.Huffy:
                case NaibaoPose.Bored:
                    want = ResourcePath + "_pout";
                    break;
                case NaibaoPose.Smug:
                case NaibaoPose.Peek:
                    want = ResourcePath + "_smirk";
                    break;
                default:
                    want = ResourcePath + "_happy";
                    break;
            }

            for (int i = 0; i < _faces.Count; i++)
            {
                if (_faces[i].Name == want)
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>
        /// 把模型摆成「立着、屏幕朝相机」。
        ///
        /// 模型从 GLB 经 Blender 转成 FBX 之后，三根轴跟 Unity 的 y-up 对不上：
        /// 手机的长轴落在某一根水平轴上、屏幕法线落在另一根上。硬写一个 yaw 很容易
        /// 换一台机器就歪掉，所以这里直接量：**最长的轴抬成 +Y（立起来）**，
        /// **最薄的轴转成 -Z（冲着相机）**，剩下的轴自然就是屏幕的横轴。
        /// </summary>
        private static void AlignToCamera(Transform child)
        {
            var filter = child.GetComponentInChildren<MeshFilter>();
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

            Quaternion modelRotation = filter.transform.rotation;
            Vector3 modelUp = modelRotation * AxisVector(upAxis);
            Vector3 modelThin = modelRotation * AxisVector(thinAxis);

            Quaternion from = Quaternion.LookRotation(modelThin, modelUp);
            Quaternion to = Quaternion.LookRotation(Vector3.back, Vector3.up);

            child.rotation = to * Quaternion.Inverse(from);
            child.Rotate(Vector3.up, FlipYaw, Space.World);
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
            if (cam == null || _active < 0 || _active >= _faces.Count)
            {
                return false;
            }

            MeshRenderer[] renderers = _faces[_active].Renderers;
            if (renderers.Length == 0)
            {
                return false;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
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

            // 高亮 = 往暖黄里推一点，而不是简单加亮：
            // 加了亮度在这套黑房间里会像漏光，推色相才像「被盯上了」
            Color tint = Color.Lerp(Color.white, HighlightColor, _glow * _pulse * 0.55f);

            for (int i = 0; i < _faces.Count; i++)
            {
                if (_faces[i].Material != null)
                {
                    _faces[i].Material.SetColor("_Color", tint);
                }
            }

            float scale = Scale * (1f + 0.02f * _glow * Mathf.Sin(Time.unscaledTime * 3.4f));
            transform.localScale = new Vector3(scale, scale, scale);
        }
    }
}
