using System.Text;
using NailongFNAF.Audio;
using NailongFNAF.Core;
using NailongFNAF.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 占位原型：用纯代码生成的色块跑通一整夜，验证玩法与敌人 AI。
    ///
    /// 这不是最终画面。等真素材按 docs/03-素材规格.md 的命名放进 Assets/Art 之后，
    /// 把这里的色块换成对应图片即可，GameManager / EnemyDirector 那一层不用动。
    ///
    /// 操作：
    ///   · 鼠标移到屏幕左右边缘 → 左右转头
    ///   · 底部按钮 → 开关左右门 / 灯，切换监控
    ///   · 监控开着时按 1 / 2 / 3 → 切换机位
    ///   · R 键 → 重开一夜
    ///   · [ / ] 键 → 上一夜 / 下一夜（调难度用）
    /// </summary>
    public class PrototypeScene : MonoBehaviour
    {
        /// <summary>鼠标进入屏幕两侧这个比例的范围内就转头。</summary>
        private const float EdgeZone = 0.08f;

        /// <summary>三个机位的显示名（大厅 / 左走廊 / 右走廊）。</summary>
        private static readonly string[] CameraNames =
        {
            "CAM 1A    HALL",
            "CAM 2A    LEFT HALL",
            "CAM 3A    RIGHT HALL"
        };

        [Header("素材（留空则用色块占位）")]
        [SerializeField] private Sprite _bgFront;
        [SerializeField] private Sprite _bgLeft;
        [SerializeField] private Sprite _bgRight;
        [SerializeField] private Sprite _doorLeftOpen;
        [SerializeField] private Sprite _doorLeftClosed;
        [SerializeField] private Sprite _doorRightOpen;
        [SerializeField] private Sprite _doorRightClosed;

        private Canvas _canvas;
        private Image _background;
        private Image _doorLeft;
        private Image _doorRight;
        private Image _monitorOverlay;
        private Text _monitorLabel;
        private Text _powerText;
        private Text _timeText;
        private Text _statusText;
        private Text _enemyText;
        private Text _hintText;
        private Image _jumpscareOverlay;
        private Text _jumpscareText;
        private Font _font;
        private Sprite _solidSprite;

        private EnemyDirector _enemyDirector;
        private AudioManager _audio;
        private bool _hasJumpscare;
        private EnemyId _jumpscareEnemy;
        private bool _wasPowerOut;
        private float _footstepTimer;

        private ViewSide _view = ViewSide.Front;

        private void Awake()
        {
            EnsureGameManager();
            EnsureEventSystem();
            EnsureEnemyDirector();
            EnsureAudioManager();

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
            {
                Debug.LogWarning("[奶龙] 没取到内置字体，HUD 上的文字不会显示（功能不受影响）。");
            }

            BuildUi();
        }

        private void OnDestroy()
        {
            if (_enemyDirector != null)
            {
                _enemyDirector.Attacked -= OnEnemyAttacked;
            }
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
                ClearJumpscare();
            }

            HandleNightKeys(gm);
            HandleCameraKeys(gm);

            UpdateView(gm);
            UpdateVisuals(gm);
            UpdateHud(gm);
            UpdateJumpscareShake();
            UpdateAudio(gm);
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

        private void EnsureEnemyDirector()
        {
            _enemyDirector = FindObjectOfType<EnemyDirector>();
            if (_enemyDirector == null)
            {
                var go = new GameObject("EnemyDirector");
                _enemyDirector = go.AddComponent<EnemyDirector>();
            }

            _enemyDirector.Attacked += OnEnemyAttacked;
        }

        private void EnsureAudioManager()
        {
            _audio = FindObjectOfType<AudioManager>();
            if (_audio == null)
            {
                var go = new GameObject("AudioManager");
                _audio = go.AddComponent<AudioManager>();
            }
        }

        // ---------------------------------------------------------------
        // 视角：鼠标靠边就转头
        // ---------------------------------------------------------------

        private void UpdateView(GameManager gm)
        {
            float width = Screen.width;
            float x = width > 0f ? Input.mousePosition.x / width : 0.5f;

            if (x <= EdgeZone)
            {
                _view = ViewSide.Left;
            }
            else if (x >= 1f - EdgeZone)
            {
                _view = ViewSide.Right;
            }
            else
            {
                _view = ViewSide.Front;
            }

            gm.SetView(_view);
        }

        private void HandleCameraKeys(GameManager gm)
        {
            if (!gm.MonitorUp)
            {
                return;
            }

            int before = gm.CurrentCameraIndex;

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                gm.SetCamera(0);
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                gm.SetCamera(1);
            }

            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                gm.SetCamera(2);
            }

            if (_audio != null && gm.CurrentCameraIndex != before)
            {
                _audio.PlayCameraSwitch();
            }
        }

        private void HandleNightKeys(GameManager gm)
        {
            if (_enemyDirector == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                _enemyDirector.SetNight(_enemyDirector.NightIndex - 1);
                gm.Restart();
                ClearJumpscare();
            }

            if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                _enemyDirector.SetNight(_enemyDirector.NightIndex + 1);
                gm.Restart();
                ClearJumpscare();
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
                _monitorLabel.text = CameraName(gm.CurrentCameraIndex);
                return;
            }

            _monitorOverlay.gameObject.SetActive(false);

            if (gm.HasPowerOut)
            {
                _background.sprite = _solidSprite;
                _background.color = Color.black;
                HideDoors();
                return;
            }

            ApplyBackground(gm);

            if (_hasJumpscare)
            {
                HideDoors();
                return;
            }

            SetDoorSprites(gm.LeftDoorClosed, gm.RightDoorClosed);
        }

        private void ApplyBackground(GameManager gm)
        {
            Sprite sprite = null;

            switch (_view)
            {
                case ViewSide.Left:
                    sprite = _bgLeft;
                    break;

                case ViewSide.Right:
                    sprite = _bgRight;
                    break;

                default:
                    sprite = _bgFront;
                    break;
            }

            if (sprite != null)
            {
                _background.sprite = sprite;
                _background.color = Color.white;
                return;
            }

            // 没素材就用色块兜底
            _background.sprite = _solidSprite;

            switch (_view)
            {
                case ViewSide.Left:
                    _background.color = gm.LeftDoorClosed
                        ? new Color(0.06f, 0.09f, 0.22f)
                        : new Color(0.18f, 0.30f, 0.58f);
                    break;

                case ViewSide.Right:
                    _background.color = gm.RightDoorClosed
                        ? new Color(0.05f, 0.16f, 0.09f)
                        : new Color(0.18f, 0.48f, 0.28f);
                    break;

                default:
                    _background.color = new Color(0.20f, 0.20f, 0.23f);
                    break;
            }
        }

        private void HideDoors()
        {
            _doorLeft.gameObject.SetActive(false);
            _doorRight.gameObject.SetActive(false);
        }

        /// <summary>门片：只在对应视角显示，素材缺失就隐藏（不会露出白块）。</summary>
        private void SetDoorSprites(bool leftClosed, bool rightClosed)
        {
            Sprite left = leftClosed ? _doorLeftClosed : _doorLeftOpen;
            Sprite right = rightClosed ? _doorRightClosed : _doorRightOpen;

            _doorLeft.gameObject.SetActive(left != null && _view == ViewSide.Left);
            _doorLeft.sprite = left;
            _doorLeft.color = Color.white;

            _doorRight.gameObject.SetActive(right != null && _view == ViewSide.Right);
            _doorRight.sprite = right;
            _doorRight.color = Color.white;
        }

        private static string CameraName(int index)
        {
            if (index < 0 || index >= CameraNames.Length)
            {
                return "CAM --";
            }

            return CameraNames[index];
        }

        // ---------------------------------------------------------------
        // 跳杀（占位：全屏红 + 大字抖动。等 mp4 就绪后换成 VideoPlayer）
        // ---------------------------------------------------------------

        private void OnEnemyAttacked(EnemyId id)
        {
            _hasJumpscare = true;
            _jumpscareEnemy = id;

            _jumpscareOverlay.gameObject.SetActive(true);
            _jumpscareText.text = "JUMPSCARE\n" + id.Code().ToUpperInvariant();
            _jumpscareText.rectTransform.anchoredPosition = Vector2.zero;
        }

        private void ClearJumpscare()
        {
            _hasJumpscare = false;
            _jumpscareOverlay.gameObject.SetActive(false);
        }

        private void UpdateJumpscareShake()
        {
            if (!_hasJumpscare)
            {
                return;
            }

            float t = Time.time;
            float x = Mathf.Sin(t * 42f) * 14f;
            float y = Mathf.Cos(t * 35f) * 10f;
            _jumpscareText.rectTransform.anchoredPosition = new Vector2(x, y);

            float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(t * 8f));
            _jumpscareOverlay.color = new Color(0.45f + 0.45f * pulse, 0.02f, 0.05f);
        }

        // ---------------------------------------------------------------
        // 音频：脚步与断电
        // ---------------------------------------------------------------

        private void UpdateAudio(GameManager gm)
        {
            if (_audio == null)
            {
                return;
            }

            if (gm.HasPowerOut)
            {
                if (!_wasPowerOut)
                {
                    _audio.PlayPowerOut();
                }

                _wasPowerOut = true;
                return;
            }

            _wasPowerOut = false;

            if (gm.HasBeenCaught || _enemyDirector == null || !AnyEnemyNearby())
            {
                _footstepTimer = 0f;
                return;
            }

            // 有敌人在走廊或更近的位置 —— 按不规则的间隔传出脚步
            _footstepTimer -= Time.deltaTime;
            if (_footstepTimer <= 0f)
            {
                _audio.PlayFootstep();
                _footstepTimer = Random.Range(0.7f, 1.5f);
            }
        }

        private bool AnyEnemyNearby()
        {
            foreach (var brain in _enemyDirector.Brains)
            {
                if (brain.Stage >= 1)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>由编辑器菜单「奶龙 → 5. 接入素材」调用，一次性注入背景与门片。</summary>
        public void SetArtwork(Sprite bgFront, Sprite bgLeft, Sprite bgRight,
            Sprite doorLeftOpen, Sprite doorLeftClosed,
            Sprite doorRightOpen, Sprite doorRightClosed)
        {
            _bgFront = bgFront;
            _bgLeft = bgLeft;
            _bgRight = bgRight;
            _doorLeftOpen = doorLeftOpen;
            _doorLeftClosed = doorLeftClosed;
            _doorRightOpen = doorRightOpen;
            _doorRightClosed = doorRightClosed;
        }

        // ---------------------------------------------------------------
        // HUD
        // ---------------------------------------------------------------

        private void UpdateHud(GameManager gm)
        {
            _powerText.text = "POWER " + Mathf.CeilToInt(gm.Power.Level) + "%";
            _timeText.text = FormatHour(gm.Clock.CurrentHour);
            _statusText.text = BuildStatus(gm);
            _enemyText.text = BuildEnemyStatus();
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

        private string BuildEnemyStatus()
        {
            if (_enemyDirector == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append("NIGHT ").Append(_enemyDirector.NightIndex + 1).Append("    ");

            foreach (var id in EnemyIds.All)
            {
                var brain = _enemyDirector.GetBrain(id);
                if (brain == null)
                {
                    continue;
                }

                sb.Append(id.Code().ToUpperInvariant())
                  .Append(" ")
                  .Append(DescribeBrain(brain))
                  .Append(" (")
                  .Append(brain.ActivityLevel)
                  .Append(")    ");
            }

            return sb.ToString();
        }

        private static string DescribeBrain(EnemyBrain brain)
        {
            var door = brain as DoorEnemyBrain;
            if (door != null)
            {
                switch (door.Stage)
                {
                    case 0:
                        return "AWAY";
                    case 1:
                        return "CORRIDOR";
                    default:
                        return "AT DOOR";
                }
            }

            var lulu = brain as LuluBrain;
            if (lulu != null)
            {
                switch (lulu.Stage)
                {
                    case 0:
                        return "AWAY";
                    case 1:
                        return "CORRIDOR";
                    default:
                        return "OUTSIDE " + lulu.Side.ToString().ToUpperInvariant();
                }
            }

            var nailong = brain as NailongBrain;
            if (nailong != null)
            {
                return "CAM " + (nailong.CameraIndex + 1);
            }

            return "-";
        }

        private string BuildHint(GameManager gm)
        {
            if (_hasJumpscare)
            {
                return "CAUGHT BY " + _jumpscareEnemy.Code().ToUpperInvariant() + "    |    R to restart";
            }

            if (gm.HasBeenCaught)
            {
                return "CAUGHT    |    R to restart";
            }

            if (gm.HasSurvived)
            {
                return "6 AM    YOU SURVIVED";
            }

            if (gm.HasPowerOut)
            {
                return "POWER OUT";
            }

            return "Mouse to edges to look around    |    1/2/3 to switch camera    |    R restart    |    [ ] change night";
        }

        // ---------------------------------------------------------------
        // 构建 UI（全部代码生成，不依赖任何素材文件）
        // ---------------------------------------------------------------

        private void BuildUi()
        {
            _canvas = GetOrCreateCanvas();
            _solidSprite = MakeSolidSprite();

            _background = MakeFullScreenImage("Background", new Color(0.20f, 0.20f, 0.23f));

            // 门片夹在背景与监控之间
            _doorLeft = MakeFullScreenImage("DoorLeft", Color.white);
            _doorLeft.gameObject.SetActive(false);

            _doorRight = MakeFullScreenImage("DoorRight", Color.white);
            _doorRight.gameObject.SetActive(false);

            // 监控层压在背景之上、按钮之下 —— 看监控时仍然能关门
            _monitorOverlay = MakeFullScreenImage("MonitorOverlay", new Color(0.04f, 0.05f, 0.06f));
            _monitorLabel = MakeText(_monitorOverlay.transform, "MonitorLabel",
                "CAM 1A", TextAnchor.MiddleCenter, 46, new Color(0.72f, 0.86f, 0.72f));
            StretchToParent(_monitorLabel.rectTransform);
            _monitorOverlay.gameObject.SetActive(false);

            BuildButtons();
            BuildHud();
            BuildJumpscareLayer();
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
            var gm = GameManager.Instance;
            bool before = gm.LeftDoorClosed;
            gm.ToggleLeftDoor();

            if (_audio != null && gm.LeftDoorClosed != before)
            {
                _audio.PlayDoor(gm.LeftDoorClosed);
            }
        }

        private void OnDoorRight()
        {
            var gm = GameManager.Instance;
            bool before = gm.RightDoorClosed;
            gm.ToggleRightDoor();

            if (_audio != null && gm.RightDoorClosed != before)
            {
                _audio.PlayDoor(gm.RightDoorClosed);
            }
        }

        private void OnLightLeft()
        {
            var gm = GameManager.Instance;
            bool before = gm.LeftLightOn;
            gm.ToggleLeftLight();

            if (_audio != null && gm.LeftLightOn != before)
            {
                _audio.PlayLight(gm.LeftLightOn);
            }
        }

        private void OnLightRight()
        {
            var gm = GameManager.Instance;
            bool before = gm.RightLightOn;
            gm.ToggleRightLight();

            if (_audio != null && gm.RightLightOn != before)
            {
                _audio.PlayLight(gm.RightLightOn);
            }
        }

        private void OnMonitor()
        {
            var gm = GameManager.Instance;
            bool before = gm.MonitorUp;
            gm.ToggleMonitor();

            if (_audio != null && gm.MonitorUp != before)
            {
                _audio.PlayCameraSwitch();
            }
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
            PlaceBottomLeft(_statusText.rectTransform, 40f, 150f, 1500f, 40f);

            _enemyText = MakeText(hud.transform, "EnemyText", string.Empty,
                TextAnchor.LowerLeft, 22, new Color(0.86f, 0.68f, 0.72f));
            PlaceBottomLeft(_enemyText.rectTransform, 40f, 196f, 1800f, 40f);

            _hintText = MakeText(hud.transform, "HintText", string.Empty,
                TextAnchor.LowerCenter, 26, new Color(0.86f, 0.86f, 0.62f));
            PlaceBottomCenter(_hintText.rectTransform, 0f, 12f, 1700f, 40f);
        }

        private void BuildJumpscareLayer()
        {
            _jumpscareOverlay = MakeFullScreenImage("JumpscareOverlay", new Color(0.6f, 0.02f, 0.05f));
            _jumpscareOverlay.transform.SetAsLastSibling();

            _jumpscareText = MakeText(_jumpscareOverlay.transform, "JumpscareText", string.Empty,
                TextAnchor.MiddleCenter, 120, Color.white);
            StretchToParent(_jumpscareText.rectTransform);

            _jumpscareOverlay.gameObject.SetActive(false);
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
            image.sprite = _solidSprite != null ? _solidSprite : MakeSolidSprite();
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
