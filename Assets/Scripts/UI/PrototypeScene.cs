using NailongFNAF.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 占位原型：用纯代码生成的色块跑通一整夜，验证玩法流程。
    ///
    /// 这不是最终画面。等真素材按 docs/03-素材规格.md 的命名放进 Assets/Art 之后，
    /// 把这里的色块换成对应图片即可，GameManager 那一层不用动。
    ///
    /// 操作：
    ///   · 鼠标移到屏幕左右边缘 → 左右转头
    ///   · 底部按钮 → 开关左右门 / 灯，切换监控
    ///   · R 键 → 重开一夜
    /// </summary>
    public class PrototypeScene : MonoBehaviour
    {
        /// <summary>鼠标进入屏幕两侧这个比例的范围内就转头。</summary>
        private const float EdgeZone = 0.08f;

        private enum View
        {
            Front,
            Left,
            Right
        }

        private Canvas _canvas;
        private Image _background;
        private Image _monitorOverlay;
        private Text _monitorLabel;
        private Text _powerText;
        private Text _timeText;
        private Text _statusText;
        private Text _hintText;
        private Font _font;

        private View _view = View.Front;

        private void Awake()
        {
            EnsureGameManager();
            EnsureEventSystem();

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
            {
                Debug.LogWarning("[奶龙] 没取到内置字体，HUD 上的文字不会显示（功能不受影响）。");
            }

            BuildUi();
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                gm.Restart();
            }

            UpdateView();
            UpdateVisuals(gm);
            UpdateHud(gm);
        }

        private static void EnsureGameManager()
        {
            if (GameManager.Instance == null)
            {
                new GameObject("GameManager").AddComponent<GameManager>();
            }
        }

        /// <summary>没有事件系统的话按钮点不动，补一个。</summary>
        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // ---------------------------------------------------------------
        // 视角：鼠标靠边就转头
        // ---------------------------------------------------------------

        private void UpdateView()
        {
            float width = Screen.width;
            float x = width > 0f ? Input.mousePosition.x / width : 0.5f;

            if (x <= EdgeZone)
            {
                _view = View.Left;
            }
            else if (x >= 1f - EdgeZone)
            {
                _view = View.Right;
            }
            else
            {
                _view = View.Front;
            }
        }

        // ---------------------------------------------------------------
        // 画面
        // ---------------------------------------------------------------

        private void UpdateVisuals(GameManager gm)
        {
            if (gm.MonitorUp)
            {
                _monitorOverlay.gameObject.SetActive(true);
                _monitorLabel.text = "CAM 1A    signal ok";
                return;
            }

            _monitorOverlay.gameObject.SetActive(false);

            if (gm.HasPowerOut)
            {
                _background.color = Color.black;
                return;
            }

            switch (_view)
            {
                case View.Left:
                    // 关门时画面压暗，一眼能看出门是关着的
                    _background.color = gm.LeftDoorClosed
                        ? new Color(0.06f, 0.09f, 0.22f)
                        : new Color(0.18f, 0.30f, 0.58f);
                    break;

                case View.Right:
                    _background.color = gm.RightDoorClosed
                        ? new Color(0.05f, 0.16f, 0.09f)
                        : new Color(0.18f, 0.48f, 0.28f);
                    break;

                default:
                    _background.color = new Color(0.20f, 0.20f, 0.23f);
                    break;
            }
        }

        // ---------------------------------------------------------------
        // HUD
        // ---------------------------------------------------------------

        private void UpdateHud(GameManager gm)
        {
            _powerText.text = "POWER " + Mathf.CeilToInt(gm.Power.Level) + "%";
            _timeText.text = FormatHour(gm.Clock.CurrentHour);
            _statusText.text = BuildStatus(gm);
            _hintText.text = BuildHint(gm);
        }

        private static string FormatHour(int hour)
        {
            return hour == 0 ? "12 AM" : hour + " AM";
        }

        private static string BuildStatus(GameManager gm)
        {
            return "DOOR L " + (gm.LeftDoorClosed ? "CLOSED" : "OPEN")
                 + "    DOOR R " + (gm.RightDoorClosed ? "CLOSED" : "OPEN")
                 + "    LIGHT L " + (gm.LeftLightOn ? "ON" : "OFF")
                 + "    LIGHT R " + (gm.RightLightOn ? "ON" : "OFF");
        }

        private static string BuildHint(GameManager gm)
        {
            if (gm.HasSurvived)
            {
                return "6 AM    YOU SURVIVED";
            }

            if (gm.HasPowerOut)
            {
                return "POWER OUT";
            }

            return "Move the mouse to the screen edges to look around    |    R to restart";
        }

        // ---------------------------------------------------------------
        // 构建 UI（全部代码生成，不依赖任何素材文件）
        // ---------------------------------------------------------------

        private void BuildUi()
        {
            _canvas = GetOrCreateCanvas();

            _background = MakeFullScreenImage("Background", new Color(0.20f, 0.20f, 0.23f));

            // 监控层压在背景之上、按钮之下 —— 看监控时仍然能关门
            _monitorOverlay = MakeFullScreenImage("MonitorOverlay", new Color(0.04f, 0.05f, 0.06f));
            _monitorLabel = MakeText(_monitorOverlay.transform, "MonitorLabel",
                "CAM 1A", TextAnchor.MiddleCenter, 46, new Color(0.72f, 0.86f, 0.72f));
            StretchToParent(_monitorLabel.rectTransform);
            _monitorOverlay.gameObject.SetActive(false);

            BuildButtons();
            BuildHud();
        }

        private Canvas GetOrCreateCanvas()
        {
            var existing = FindObjectOfType<Canvas>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("PrototypeCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        private void BuildButtons()
        {
            const float width = 170f;
            const float height = 64f;
            const float gap = 20f;
            const float y = 60f;

            float total = width * 5f + gap * 4f;
            float startX = -total * 0.5f + width * 0.5f;

            MakeButton("BtnDoorLeft", "DOOR L", startX + (width + gap) * 0f, y, width, height, OnDoorLeft);
            MakeButton("BtnLightLeft", "LIGHT L", startX + (width + gap) * 1f, y, width, height, OnLightLeft);
            MakeButton("BtnMonitor", "CAM", startX + (width + gap) * 2f, y, width, height, OnMonitor);
            MakeButton("BtnLightRight", "LIGHT R", startX + (width + gap) * 3f, y, width, height, OnLightRight);
            MakeButton("BtnDoorRight", "DOOR R", startX + (width + gap) * 4f, y, width, height, OnDoorRight);
        }

        private void OnDoorLeft()
        {
            GameManager.Instance.ToggleLeftDoor();
        }

        private void OnDoorRight()
        {
            GameManager.Instance.ToggleRightDoor();
        }

        private void OnLightLeft()
        {
            GameManager.Instance.ToggleLeftLight();
        }

        private void OnLightRight()
        {
            GameManager.Instance.ToggleRightLight();
        }

        private void OnMonitor()
        {
            GameManager.Instance.ToggleMonitor();
        }

        private void BuildHud()
        {
            var hud = new GameObject("Hud", typeof(RectTransform));
            hud.transform.SetParent(_canvas.transform, false);
            StretchToParent(hud.GetComponent<RectTransform>());

            _powerText = MakeText(hud.transform, "PowerText", "POWER 100%",
                TextAnchor.UpperRight, 34, new Color(0.92f, 0.92f, 0.92f));
            PlaceTopRight(_powerText.rectTransform, -40f, -30f, 400f, 50f);

            _timeText = MakeText(hud.transform, "TimeText", "12 AM",
                TextAnchor.UpperLeft, 34, new Color(0.92f, 0.92f, 0.92f));
            PlaceTopLeft(_timeText.rectTransform, 40f, -30f, 300f, 50f);

            _statusText = MakeText(hud.transform, "StatusText", string.Empty,
                TextAnchor.LowerLeft, 24, new Color(0.75f, 0.75f, 0.82f));
            PlaceBottomLeft(_statusText.rectTransform, 40f, 150f, 1200f, 40f);

            _hintText = MakeText(hud.transform, "HintText", string.Empty,
                TextAnchor.LowerCenter, 26, new Color(0.86f, 0.86f, 0.62f));
            PlaceBottomCenter(_hintText.rectTransform, 0f, 12f, 1500f, 40f);
        }

        // ---------------------------------------------------------------
        // 小工具
        // ---------------------------------------------------------------

        private Image MakeFullScreenImage(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_canvas.transform, false);

            StretchToParent(go.GetComponent<RectTransform>());

            var image = go.GetComponent<Image>();
            image.sprite = MakeSolidSprite();
            image.color = color;
            image.raycastTarget = false;

            return image;
        }

        private Button MakeButton(string name, string label, float x, float y,
            float width, float height, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);

            var image = go.GetComponent<Image>();
            image.sprite = MakeSolidSprite();
            image.color = new Color(0.28f, 0.28f, 0.34f, 0.95f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = MakeText(go.transform, "Label", label,
                TextAnchor.MiddleCenter, 24, Color.white);
            StretchToParent(text.rectTransform);

            return button;
        }

        private Text MakeText(Transform parent, string name, string content,
            TextAnchor anchor, int fontSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.text = content;
            text.alignment = anchor;
            text.fontSize = fontSize;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        /// <summary>生成一张 4×4 的纯白贴图，配合 Image.color 就能当任意色块用。</summary>
        private static Sprite MakeSolidSprite()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            texture.name = "prototype_solid";

            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void PlaceTopRight(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void PlaceBottomLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void PlaceBottomCenter(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
