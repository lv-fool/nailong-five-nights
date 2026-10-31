using UnityEngine;
using UnityEditor;

namespace NailongFNAF.EditorTools
{
    /// <summary>
    /// 贝利亚跳杀预览窗口 —— 不用进 Play 就能看动画。
    ///
    /// 做法：一个自绘的预览区 + 三个滑杆（起始大小 / 结束大小 / 时长）。
    /// 拖动时间轴可以**逐帧定格**看任意时刻的形态，比反复进 Play 快得多。
    ///
    /// 菜单：奶龙 / 16. 预览贝利亚跳杀
    /// </summary>
    public class BelialLungePreviewWindow : EditorWindow
    {
        private Sprite _sprite;

        [SerializeField] private float _time = 0f;
        [SerializeField] private bool _playing;
        [SerializeField] private float _startScale = 0.18f;
        [SerializeField] private float _endScale = 1.65f;
        [SerializeField] private float _duration = 0.737f;

        /// <summary>和代码里一致 —— 读数改这里就够，不用去翻 PrototypeScene。</summary>
        private const float ShakeNear = 2f;
        private const float ShakeFar = 30f;

        private double _lastTick;

        [MenuItem("奶龙/16. 预览贝利亚跳杀", false, 30)]
        public static void Open()
        {
            var w = GetWindow<BelialLungePreviewWindow>("贝利亚跳杀预览");
            w.minSize = new Vector2(420f, 560f);
            w.TryLoadSprite();
        }

        private void OnEnable()
        {
            TryLoadSprite();
            EditorApplication.update += Tick;
            _lastTick = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
        }

        private void TryLoadSprite()
        {
            if (_sprite != null)
            {
                return;
            }

            _sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Resources/Belial/char_belial_lunge.png");

            if (_sprite == null)
            {
                // 退回按 Texture2D 载入（没配成 Sprite 时也能预览）
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/Resources/Belial/char_belial_lunge.png");

                if (tex != null)
                {
                    _sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                }
            }
        }

        private void Tick()
        {
            if (!_playing)
            {
                _lastTick = EditorApplication.timeSinceStartup;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _lastTick);
            _lastTick = now;

            _time += dt;

            if (_time >= _duration)
            {
                _time = _duration;
                _playing = false;
            }

            Repaint();
        }

        private void OnGUI()
        {
            if (_sprite == null)
            {
                EditorGUILayout.HelpBox(
                    "找不到 Assets/Resources/Belial/char_belial_lunge.png",
                    MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("贝利亚跳杀预览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "拖动时间轴可就定格看任意时刻。参数改完记得同步到 PrototypeScene。",
                EditorStyles.miniLabel);

            EditorGUILayout.Space(6f);

            // ── 控件 ──
            _playing = EditorGUILayout.Toggle("播放", _playing);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("从头播放"))
            {
                _time = 0f;
                _playing = true;
            }

            if (GUILayout.Button("定格在中段"))
            {
                _time = _duration * 0.55f;
                _playing = false;
            }

            if (GUILayout.Button("定格在结尾"))
            {
                _time = _duration * 0.95f;
                _playing = false;
            }

            EditorGUILayout.EndHorizontal();

            float t = _duration <= 0.001f ? 0f : _time / _duration;
            float newT = EditorGUILayout.Slider("时间轴 " + Mathf.RoundToInt(t * 100f) + "%", t, 0f, 1f);
            if (!Mathf.Approximately(newT, t))
            {
                _time = newT * _duration;
                _playing = false;
            }

            EditorGUILayout.Space(6f);

            _startScale = EditorGUILayout.Slider("起始大小", _startScale, 0.02f, 0.6f);
            _endScale = EditorGUILayout.Slider("结束大小", _endScale, 0.6f, 3f);
            _duration = EditorGUILayout.Slider("时长（秒）", _duration, 0.2f, 3f);

            EditorGUILayout.Space(8f);

            // ── 关键数据 ──
            float eased = t * t * t;
            float scale = Mathf.Lerp(_startScale, _endScale, eased);
            float screenPercent = scale * 1300f / 1080f * 100f;

            EditorGUILayout.LabelField(
                string.Format("当前缩放 {0:F3}    占屏高 {1:F0}%（设计 1080）", scale, screenPercent),
                EditorStyles.boldLabel);

            EditorGUILayout.Space(8f);

            DrawPreview(t, scale);

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "改完参数后，同步改 PrototypeScene 里的：\n" +
                "BelialLungeStartScale / BelialLungeEndScale\n" +
                "（时长是 JumpscareVideoSeconds，和视频跳杀共用）",
                MessageType.Info);
        }

        private void DrawPreview(float t, float scale)
        {
            float availW = position.width - 24f;
            float availH = position.height - 340f;

            if (availH < 120f)
            {
                availH = 120f;
            }

            var area = GUILayoutUtility.GetRect(availW, availH);

            // 背景：模拟屏幕（黑色）
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.13f));

            // 画一个 16:9 的"屏幕"框
            float tw = area.width;
            float th = tw * 9f / 16f;
            if (th > area.height)
            {
                th = area.height;
                tw = th * 16f / 9f;
            }

            var screen = new Rect(
                area.x + (area.width - tw) * 0.5f,
                area.y + (area.height - th) * 0.5f,
                tw, th);

            EditorGUI.DrawRect(screen, Color.black);

            // 图片按 scale 缩放后居中画在"屏幕"里
            // 基础高度按 1300/1080 折算，和游戏里一致
            float imgH = screen.height * (scale * 1300f / 1080f);
            float aspect = _sprite.rect.width / _sprite.rect.height;
            float imgW = imgH * aspect;

            // 抖动（和游戏里同一套公式，方便看真实观感）
            float shakeAmp = Mathf.Lerp(ShakeNear, ShakeFar, t);
            float sx = (Mathf.PerlinNoise(Time.realtimeSinceStartup * 18f, 0f) - 0.5f) * 2f * shakeAmp;
            float sy = (Mathf.PerlinNoise(0f, Time.realtimeSinceStartup * 18f) - 0.5f) * 2f * shakeAmp;

            var imgRect = new Rect(
                screen.center.x - imgW * 0.5f + sx,
                screen.center.y - imgH * 0.5f - sy,
                imgW, imgH);

            // 裁剪到屏幕框里（模拟游戏里超出画面的部分看不到）
            GUI.BeginClip(screen);
            var clipped = new Rect(
                imgRect.x - screen.x, imgRect.y - screen.y,
                imgRect.width, imgRect.height);

            var tex = _sprite.texture;
            if (tex != null)
            {
                GUI.DrawTexture(clipped, tex, ScaleMode.ScaleToFit, true);
            }

            GUI.EndClip();

            // 屏幕边框
            Handles.color = new Color(0.4f, 0.4f, 0.45f);
            Handles.DrawSolidRectangleWithOutline(screen, Color.clear, new Color(0.4f, 0.4f, 0.45f));
        }
    }
}
