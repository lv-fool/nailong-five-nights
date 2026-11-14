using NailongFNAF.Core;
using UnityEngine;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 方案 A：全屏氛围后处理（Bloom + 暗角 + 颗粒 + 色差 + 轻微调色）。
    ///
    /// 设计见 `docs/11-画面技术对照.md` 第五章。要点：
    ///   · 只作用在**相机画面**上；Screen Space Overlay 的 UGUI 不会被它影响，
    ///     所以 HUD / 监控层保持清晰（监控自己那套 CRT 不受干扰）。
    ///   · **不碰 Camera.targetTexture** —— 改了会让 WorldToScreenPoint 变成 RT 坐标，
    ///     墙按钮 / 门口立绘全算错（踩过，详见 MEMORY）。
    ///   · 默认画质是 Low（1）：<see cref="ScreenPostPolicy.BloomAllowed"/> 返回 false，
    ///     这时**完全不跑 Bloom 的降采样/模糊 pass**，只留暗角 + 颗粒 + 色差，
    ///     1080p 下开销几乎可以忽略。想看到 Bloom 要把画质调到 Medium 以上。
    ///   · 监控升起时总强度减半（<see cref="ScreenPostPolicy.EffectiveIntensity"/>），
    ///     免得氛围层把监控画面抢戏。
    ///
    /// 素材走 `Resources.Load<Material>("ScreenPostMaterial")`（和 PanoMaterial 同一个
    /// 套路，避免发行版把没人引用的 shader 剔除）。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ScreenPostFx : MonoBehaviour
    {
        private const string MaterialResourcePath = "ScreenPostMaterial";

        [Header("总开关 / 强度")]
        [Range(0f, 1f)]
        [Tooltip("0 = 完全关闭；1 = 全强度。监控升起时自动减半。")]
        [SerializeField] private float _intensity = 0.85f;

        [Header("Bloom（仅 Medium 以上画质生效）")]
        [Range(0f, 2f)]
        [SerializeField] private float _bloomIntensity = 0.35f;

        [Range(0f, 2f)]
        [SerializeField] private float _bloomThreshold = 0.65f;

        [Header("镜头")]
        [Range(0f, 1f)]
        [SerializeField] private float _vignette = 0.28f;

        [Range(0f, 0.2f)]
        [SerializeField] private float _grain = 0.015f;

        [Range(0f, 0.01f)]
        [SerializeField] private float _chroma = 0.0008f;

        [Range(0.5f, 1.5f)]
        [SerializeField] private float _contrast = 1.04f;

        [Range(0f, 2f)]
        [SerializeField] private float _saturation = 1.05f;

        private Material _material;
        private Camera _camera;

        /// <summary>最近一次实际生效的强度（调试 / 截图验证用）。</summary>
        public float LastIntensity { get; private set; }

        /// <summary>最近一次是否真的跑了 Bloom（调试 / 截图验证用）。</summary>
        public bool LastBloomRan { get; private set; }

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            Material source = Resources.Load<Material>(MaterialResourcePath);

            if (source == null)
            {
                Debug.LogWarning("[奶龙] 缺 Assets/Resources/ScreenPostMaterial.mat，"
                    + "全屏氛围后处理不生效（画面照常，只是没有 Bloom/暗角）。");
                enabled = false;
                return;
            }

            // 克隆一份：运行时 SetFloat 不污染 Resources 里的共享材质
            _material = new Material(source);
            _material.hideFlags = HideFlags.DontSave;
        }

        private void OnDisable()
        {
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            var gm = GameManager.Instance;
            bool monitorUp = gm != null && gm.MonitorUp;
            float intensity = ScreenPostPolicy.EffectiveIntensity(_intensity, monitorUp);
            bool bloom = ScreenPostPolicy.BloomAllowed(QualitySettings.GetQualityLevel());

            LastIntensity = intensity;
            LastBloomRan = bloom && intensity > 0.001f;

            if (intensity <= 0.001f)
            {
                Graphics.Blit(source, destination);
                return;
            }

            // ── Bloom：半分辨率做提取 + 两次可分离模糊 ──
            RenderTexture bloomTex = null;
            RenderTexture bloomTmp = null;

            if (bloom)
            {
                int w = Mathf.Max(1, source.width / 2);
                int h = Mathf.Max(1, source.height / 2);

                RenderTexture rtA = RenderTexture.GetTemporary(w, h, 0, source.format);
                RenderTexture rtB = RenderTexture.GetTemporary(w, h, 0, source.format);

                _material.SetFloat("_Threshold", _bloomThreshold);

                // 0: 亮度提取
                Graphics.Blit(source, rtA, _material, 0);

                // 1: 横向模糊
                _material.SetVector("_BlurDir", new Vector4(1f / w, 0f, 0f, 0f));
                Graphics.Blit(rtA, rtB, _material, 1);

                // 1: 纵向模糊
                _material.SetVector("_BlurDir", new Vector4(0f, 1f / h, 0f, 0f));
                Graphics.Blit(rtB, rtA, _material, 1);

                bloomTex = rtA;
                bloomTmp = rtB;
                _material.SetTexture("_BloomTex", bloomTex);
            }
            else
            {
                // 不跑 Bloom 时绑黑图、强度 0 —— 保证 shader 采样到的是确定值
                _material.SetTexture("_BloomTex", Texture2D.blackTexture);
            }

            _material.SetFloat("_BloomIntensity", bloom ? _bloomIntensity : 0f);
            _material.SetFloat("_Vignette", _vignette);
            _material.SetFloat("_Grain", _grain);
            _material.SetFloat("_Chroma", _chroma);
            _material.SetFloat("_Contrast", _contrast);
            _material.SetFloat("_Saturation", _saturation);
            _material.SetFloat("_Intensity", intensity);
            _material.SetFloat("_TimeSeed", Time.time * 10f);

            Graphics.Blit(source, destination, _material, 2);

            if (bloomTex != null)
            {
                RenderTexture.ReleaseTemporary(bloomTex);
            }

            if (bloomTmp != null)
            {
                RenderTexture.ReleaseTemporary(bloomTmp);
            }
        }
    }
}