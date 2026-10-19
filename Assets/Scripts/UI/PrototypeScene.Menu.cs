using NailongFNAF.Core;
using NailongFNAF.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 开始界面 + 关卡（夜晚）选择 + 进度解锁 —— <see cref="PrototypeScene"/> 的菜单部分。
    ///
    /// 流程（对齐 FNAF 1 的入口习惯）：
    ///   标题页 → 点「开始游戏」→ 关卡选择（第 1~5 夜 + 隐藏夜第 6 夜）
    ///   → 选一夜 → 进办公室开始那一夜。
    ///   撑到 6 点 → 结算面板「通关」→ 解锁下一夜（存档）→ 可继续下一夜或回标题。
    ///   被抓 / 断电被抓 → 结算面板「失败」→ 重试本夜或回标题。
    ///
    /// 解锁进度存在 PlayerPrefs（键 <see cref="UnlockKey"/>），默认只开第 1 夜。
    /// 菜单开着时 <c>_menuOpen</c> 为 true，主循环会冻住这一夜。
    /// </summary>
    public partial class PrototypeScene
    {
        /// <summary>夜晚总数：第 1~5 夜 + 隐藏夜（第 6 夜）。</summary>
        public const int NightCount = 6;

        /// <summary>解锁进度存档键：存「已解锁到第几夜」，0 起。</summary>
        public const string UnlockKey = "nailong.unlockedNight";

        /// <summary>无尽模式的纪录存档键：存「撑过的真实秒数」，越大越好。</summary>
        public const string EndlessBestKey = "nailong.endlessBest";

        /// <summary>无尽模式开局用的难度夜（0 起）—— 从第 3 夜那种强度开始，别太软。</summary>
        public const int EndlessStartNight = 2;

        /// <summary>无尽模式每撑过这么多小时，难度往上加一档。</summary>
        public const int EndlessHoursPerStage = 2;

        /// <summary>无尽模式当前难度档对应的夜（0 起）。</summary>
        private int _endlessStage = EndlessStartNight;

        /// <summary>菜单是否开着（开着就冻住这一夜）。</summary>
        private bool _menuOpen;

        /// <summary>本夜是否已经出过结算面板，避免每帧重复弹。</summary>
        private bool _endShown;

        /// <summary>被抓后先让跳杀 / 断电演出播多久，再弹结算面板（秒）。</summary>
        private const float JumpscareHoldSeconds = 2.6f;

        /// <summary>被抓后已经等了多久（秒），用来把结算面板延后到跳杀之后。</summary>
        private float _caughtHoldTimer;

        // 菜单三层视图
        private GameObject _menuRoot;
        private GameObject _titleView;
        private GameObject _levelView;
        private GameObject _endView;

        // 结算面板文案
        private Text _endTitleText;
        private Text _endBodyText;

        /// <summary>跳杀后那个敌人说的一句话（每只各有自己的台词）。</summary>
        private Text _endQuoteText;

        /// <summary>念那句台词的语音轨（素材可选，没有就只显示字幕）。</summary>
        private AudioSource _endQuoteAudio;
        private Button _endPrimaryButton;
        private Text _endPrimaryLabel;

        /// <summary>已解锁到第几夜（0 起）。</summary>
        private static int UnlockedNight
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(UnlockKey, 0), 0, NightCount - 1); }
        }

        /// <summary>夜晚显示名：第 1~5 夜，最后一夜叫「隐藏夜」。</summary>
        private static string NightName(int index)
        {
            return index >= NightCount - 1 ? "第 6 夜 · 隐藏夜" : "第 " + (index + 1) + " 夜";
        }

        // ---------------------------------------------------------------
        // 构建
        // ---------------------------------------------------------------

        private void BuildMenu()
        {
            _menuRoot = MakeFullScreenPanel("MenuRoot", new Color(0.02f, 0.02f, 0.03f, 1f), _canvas.transform);
            _menuRoot.transform.SetAsLastSibling();

            BuildTitleView();
            BuildLevelView();
            BuildEndView();

            OpenTitle();
        }

        private void BuildTitleView()
        {
            _titleView = MakeFullScreenPanel("TitleView", new Color(0f, 0f, 0f, 0f), _menuRoot.transform);

            MakeLabel(_titleView.transform, "奶龙的五夜后宫", 72, new Color(0.95f, 0.85f, 0.3f),
                new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f), new Vector2(0f, 0f), new Vector2(1100f, 120f));
            MakeLabel(_titleView.transform, "NAILONG · FIVE NIGHTS", 26, new Color(0.7f, 0.7f, 0.75f),
                new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), new Vector2(0f, 0f), new Vector2(1100f, 50f));

            // 分隔装饰线：放在副标题与按钮之间（0.55），别让它压到按钮
            MakeDivider(_titleView.transform, new Vector2(0.5f, 0.55f));

            // ⚠️ 按钮 84px 高 ≈ 画布 9%，间距必须留够，否则会和分隔线/彼此重叠
            // 三个按钮现在依次排开：开始游戏（选夜） / 无尽模式 / 退出
            MakeMenuButton(_titleView.transform, "开始游戏", Vector2.zero,
                new Vector2(0.5f, 0.47f), OnStartClicked);
            MakeMenuButton(_titleView.transform, "无尽模式", Vector2.zero,
                new Vector2(0.5f, 0.37f), OnEndlessClicked);
            MakeMenuButton(_titleView.transform, "退出", Vector2.zero,
                new Vector2(0.5f, 0.27f), OnQuitClicked);

            MakeLabel(_titleView.transform, "撑过 12 点到 6 点。鼠标看左右、升起监控、关门开灯、按手电筒。",
                20, new Color(0.55f, 0.55f, 0.6f),
                new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f), new Vector2(0f, 0f), new Vector2(1200f, 40f));
        }

        /// <summary>
        /// 分隔装饰线：一段居中的细横条 + 两端各一个菱形点（手绘风小点缀）。
        /// </summary>
        private void MakeDivider(Transform parent, Vector2 anchor)
        {
            var line = MakeFullScreenPanel("DividerLine", new Color(0.4f, 0.38f, 0.30f, 0.5f), parent);
            var rect = (RectTransform)line.transform;
            PlaceRect(rect, anchor, anchor, Vector2.zero, new Vector2(420f, 3f));
            line.GetComponent<Image>().raycastTarget = false;

            MakeDividerDot(parent, anchor, new Vector2(220f, 0f));
            MakeDividerDot(parent, anchor, new Vector2(-220f, 0f));
        }

        private void MakeDividerDot(Transform parent, Vector2 anchor, Vector2 offset)
        {
            var dot = MakeFullScreenPanel("DividerDot", new Color(0.7f, 0.62f, 0.25f, 0.8f), parent);
            var rect = (RectTransform)dot.transform;
            PlaceRect(rect, anchor, anchor, offset, new Vector2(7f, 7f));
            dot.GetComponent<Image>().raycastTarget = false;
        }

        private void BuildLevelView()
        {
            _levelView = MakeFullScreenPanel("LevelView", new Color(0f, 0f, 0f, 0f), _menuRoot.transform);

            MakeLabel(_levelView.transform, "选择夜晚", 52, new Color(0.95f, 0.85f, 0.3f),
                new Vector2(0.5f, 0.80f), new Vector2(0.5f, 0.80f), new Vector2(0f, 0f), new Vector2(900f, 90f));

            // 6 个夜晚按钮排两行，anchor 直接按画布比例给（按钮 84px ≈ 画布 9%，行距 0.14 才不叠）
            for (int i = 0; i < NightCount; i++)
            {
                int night = i;
                int col = i % 3;
                int row = i / 3;
                float x = (col - 1) * 0.16f;
                float y = 0.58f - row * 0.14f;

                bool unlocked = i <= UnlockedNight;
                // ⚠️ 别用 emoji（🔒）—— 中文字体/内置字体都没有 emoji 字形，显示成方块。
                string label = unlocked ? NightName(i) : NightName(i) + " · 未解锁";

                var btn = MakeMenuButton(_levelView.transform, label, Vector2.zero,
                    new Vector2(0.5f + x, y),
                    unlocked ? (UnityEngine.Events.UnityAction)(() => OnNightClicked(night)) : null);
                btn.interactable = unlocked;
            }

            MakeMenuButton(_levelView.transform, "返回", Vector2.zero,
                new Vector2(0.5f, 0.16f), OpenTitle);
        }

        private void BuildEndView()
        {
            _endView = MakeFullScreenPanel("EndView", new Color(0.02f, 0.02f, 0.03f, 0.96f), _menuRoot.transform);

            _endTitleText = MakeLabel(_endView.transform, "", 60, new Color(0.95f, 0.85f, 0.3f),
                new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f), new Vector2(0f, 0f), new Vector2(1100f, 100f));

            _endBodyText = MakeLabel(_endView.transform, "", 26, new Color(0.7f, 0.7f, 0.75f),
                new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.58f), new Vector2(0f, 0f), new Vector2(1100f, 80f));

            // 跳杀那只敌人说的台词 —— 斜体感靠颜色区分（Unity 内置字体没有真斜体）
            _endQuoteText = MakeLabel(_endView.transform, "", 30, new Color(0.85f, 0.78f, 0.55f),
                new Vector2(0.5f, 0.50f), new Vector2(0.5f, 0.50f), new Vector2(0f, 0f), new Vector2(1100f, 70f));
            _endQuoteText.fontStyle = FontStyle.Italic;

            if (_endQuoteText != null)
            {
                _endQuoteText.raycastTarget = false;
            }

            _endPrimaryButton = MakeMenuButton(_endView.transform, "继续", Vector2.zero,
                new Vector2(0.5f, 0.42f), OnEndPrimaryClicked);
            _endPrimaryLabel = _endPrimaryButton.GetComponentInChildren<Text>();

            MakeMenuButton(_endView.transform, "返回标题", Vector2.zero,
                new Vector2(0.5f, 0.30f), OpenTitle);

            // 台词语音轨：素材缺失时静默，不影响任何流程
            var voiceGo = new GameObject("EndQuoteVoice");
            voiceGo.transform.SetParent(_endView.transform, false);
            _endQuoteAudio = voiceGo.AddComponent<AudioSource>();
            _endQuoteAudio.playOnAwake = false;
            _endQuoteAudio.loop = false;
            _endQuoteAudio.spatialBlend = 0f;
            _endQuoteAudio.volume = 0.95f;
        }

        // ---------------------------------------------------------------
        // 交互
        // ---------------------------------------------------------------

        private void HandleMenu(GameManager gm)
        {
            if (_menuOpen)
            {
                return;
            }

            if (_endShown)
            {
                return;
            }

            if (gm.HasSurvived)
            {
                ShowWinEnd();
                return;
            }

            if (gm.HasBeenCaught)
            {
                // 先让跳杀 / 断电演出播完，再弹结算面板，别把画面盖没。
                _caughtHoldTimer += Time.deltaTime;
                float hold = _hasJumpscare ? JumpscareHoldSeconds : 0.6f;
                if (_caughtHoldTimer < hold)
                {
                    return;
                }

                ShowLoseEnd();
            }
        }

        private void OpenTitle()
        {
            _menuOpen = true;
            _menuRoot.SetActive(true);
            _titleView.SetActive(true);
            _levelView.SetActive(false);
            _endView.SetActive(false);

            // 暂停这一夜：按一下重开，让时钟/敌人停在原地，等玩家选好夜晚再真正开始
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Restart();
                ClearJumpscare();
            }

            _endShown = false;
            _caughtHoldTimer = 0f;
        }

        private void OnStartClicked()
        {
            _menuOpen = true;
            _titleView.SetActive(false);
            _levelView.SetActive(true);
            _endView.SetActive(false);

            // 刷新关卡按钮的锁态（可能刚通关解锁了下一夜）
            RefreshLevelButtons();
        }

        private void RefreshLevelButtons()
        {
            var buttons = _levelView.GetComponentsInChildren<Button>(true);
            int index = 0;
            for (int i = 0; i < NightCount; i++)
            {
                // 顺序里跳过末尾的「返回」按钮，只改夜晚按钮
                if (index >= buttons.Length)
                {
                    break;
                }

                var label = buttons[index].GetComponentInChildren<Text>();
                if (label != null && label.text.Contains("夜"))
                {
                    bool unlocked = i <= UnlockedNight;
                    buttons[index].interactable = unlocked;
                    label.text = unlocked ? NightName(i) : NightName(i) + " · 未解锁";
                    index++;
                }
            }
        }

        private void OnNightClicked(int night)
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            if (_enemyDirector != null)
            {
                _enemyDirector.SetNight(night);
            }

            // 走普通流程时把无尽模式关掉 —— 否则时钟还是「永远不到天亮」那一套
            gm.SetEndless(false);
            _endlessStage = EndlessStartNight;

            gm.Restart();
            ClearJumpscare();

            _menuRoot.SetActive(false);
            _menuOpen = false;
            _endShown = false;
            _caughtHoldTimer = 0f;
        }

        /// <summary>
        /// 标题页「无尽模式」：撑不到天亮，敌人一轮轮来，靠玩家自己的操作换电。
        /// 顺序很重要 —— <see cref="GameManager.SetEndless"/> 必须早于 <see cref="GameManager.Restart"/>，
        /// 因为 Restart 是照着 EndlessMode 去建时钟和换电账本的。
        /// </summary>
        private void OnEndlessClicked()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            gm.SetEndless(true);

            _endlessStage = EndlessStartNight;
            if (_enemyDirector != null)
            {
                _enemyDirector.SetNight(EndlessStartNight);
            }

            gm.Restart();
            ClearJumpscare();

            _menuRoot.SetActive(false);
            _menuOpen = false;
            _endShown = false;
            _caughtHoldTimer = 0f;
        }

        /// <summary>
        /// 无尽模式难度爬升：每撑过 <see cref="EndlessHoursPerStage"/> 个小时把敌人顶高一档。
        /// 只有档位真的变了才调 SetNight —— 它会把整局活动度重设，不能每帧调。
        /// </summary>
        private void UpdateEndlessRamp(GameManager gm)
        {
            if (gm == null || !gm.EndlessMode || _enemyDirector == null || gm.Clock == null)
            {
                return;
            }

            int stage = EndlessStartNight + gm.Clock.CurrentHour / EndlessHoursPerStage;
            if (stage > NightCount - 1)
            {
                stage = NightCount - 1;
            }

            if (stage == _endlessStage)
            {
                return;
            }

            _endlessStage = stage;
            _enemyDirector.SetNight(stage);
        }

        private void ShowWinEnd()
        {
            _endShown = true;
            _menuOpen = true;
            _menuRoot.SetActive(true);
            _menuRoot.transform.SetAsLastSibling(); // 置于跳杀层之上
            _titleView.SetActive(false);
            _levelView.SetActive(false);
            _endView.SetActive(true);

            int current = _enemyDirector != null ? _enemyDirector.NightIndex : 0;
            bool wasLast = current >= NightCount - 1;
            bool unlockedNext = false;

            if (!wasLast && current + 1 > UnlockedNight)
            {
                PlayerPrefs.SetInt(UnlockKey, current + 1);
                PlayerPrefs.Save();
                unlockedNext = true;
            }

            _endTitleText.text = wasLast ? "全部通关！" : NightName(current) + " 通关";
            _endBodyText.text = wasLast
                ? "你撑过了所有夜晚 —— 奶龙也拿你没办法。"
                : (unlockedNext ? "已解锁 " + NightName(current + 1) : "天亮了，你活下来了。");

            _endPrimaryLabel.text = wasLast ? "返回标题" : "下一夜";
            _endPrimaryButton.interactable = !wasLast;

            // 通关没有「谁说了句话」，清掉上一局残留的台词
            if (_endQuoteText != null)
            {
                _endQuoteText.text = string.Empty;
            }
        }

        private void ShowLoseEnd()
        {
            _endShown = true;
            _menuOpen = true;
            _menuRoot.SetActive(true);
            _menuRoot.transform.SetAsLastSibling(); // 置于跳杀层之上
            _titleView.SetActive(false);
            _levelView.SetActive(false);
            _endView.SetActive(true);

            var gm = GameManager.Instance;
            if (gm != null && gm.EndlessMode)
            {
                ShowEndlessLoseEnd(gm);
                return;
            }

            int current = _enemyDirector != null ? _enemyDirector.NightIndex : 0;

            _endTitleText.text = "你被抓住了";
            _endBodyText.text = NightName(current) + " 失败 —— 再来一次。";
            _endPrimaryLabel.text = "重试本夜";
            _endPrimaryButton.interactable = true;

            // 跳杀完，那只敌人说一句话
            _endQuoteText.text = QuoteFor(_jumpscareEnemy);
            PlayEndQuoteVoice(_jumpscareEnemy);
        }

        /// <summary>
        /// 无尽模式的结算：报「撑了多久 / 破没破纪录」，主按钮直接再来一局。
        /// 纪录按游戏内时长（小时）算，用真实秒数存档，换节奏调参也不会作废旧纪录。
        /// </summary>
        private void ShowEndlessLoseEnd(GameManager gm)
        {
            float secondsPerHour = gm.Clock != null ? gm.Clock.SecondsPerHour : NightClock.DefaultSecondsPerHour;
            int survived = gm.Clock != null ? Mathf.FloorToInt(gm.Clock.ElapsedSeconds) : 0;
            int best = EndlessBestSeconds;

            bool isRecord = survived > best;
            if (isRecord)
            {
                PlayerPrefs.SetInt(EndlessBestKey, survived);
                PlayerPrefs.Save();
                best = survived;
            }

            _endTitleText.text = isRecord ? "新纪录！" : "无尽模式结束";
            _endBodyText.text = "撑过 " + FormatSpan(survived, secondsPerHour)
                + (isRecord ? " · 这是你的最好成绩" : " · 最佳 " + FormatSpan(best, secondsPerHour));
            _endPrimaryLabel.text = "再来一局";
            _endPrimaryButton.interactable = true;

            // 无尽模式死法多半是被抓，台词照旧；没抓到就没台词
            _endQuoteText.text = QuoteFor(_jumpscareEnemy);
            PlayEndQuoteVoice(_jumpscareEnemy);
        }

        /// <summary>把「真实秒数」换算成游戏里的时长文案（每小时多少秒由时钟决定）。</summary>
        private static string FormatSpan(int seconds, float secondsPerHour)
        {
            if (secondsPerHour <= 0f)
            {
                secondsPerHour = NightClock.DefaultSecondsPerHour;
            }

            if (seconds < 0)
            {
                seconds = 0;
            }

            float totalHours = seconds / secondsPerHour;
            int hours = (int)totalHours;
            int minutes = (int)((totalHours - hours) * 60f);

            if (hours <= 0)
            {
                return minutes <= 0 ? "不到 1 分钟" : minutes + " 分钟";
            }

            return hours + " 小时 " + minutes + " 分";
        }

        /// <summary>无尽模式最好成绩（真实秒数），没打过就是 0。</summary>
        private static int EndlessBestSeconds
        {
            get
            {
                int value = PlayerPrefs.GetInt(EndlessBestKey, 0);
                return value > 0 ? value : 0;
            }
        }

        /// <summary>
        /// 播「被谁抓到就放谁的语音」。
        /// 素材放 <c>Assets/Resources/Audio/voice_&lt;代号&gt;</c>
        /// （如 <c>voice_nailong.mp3</c>），没有就**只显示字幕**，不报错、不影响流程。
        /// 所以做语音可以一条一条慢慢补，缺哪条就用哪条的文字版。
        /// </summary>
        private void PlayEndQuoteVoice(EnemyId id)
        {
            if (_endQuoteAudio == null)
            {
                return;
            }

            _endQuoteAudio.Stop();

            string path = "Audio/voice_" + id.Code();
            AudioClip clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                // 还没做语音 —— 静默跳过，字幕照常显示
                return;
            }

            _endQuoteAudio.clip = clip;
            _endQuoteAudio.Play();
        }

        /// <summary>
        /// 每只敌人被抓到后的台词。
        /// 调子跟美术一样是「可爱反差」—— 不骂人，用撒娇 / 无辜的语气说可怕的话。
        /// 改台词只动这张表。
        /// </summary>
        private static string QuoteFor(EnemyId id)
        {
            switch (id)
            {
                case EnemyId.Nailong:
                    return "「电量我收下啦～下次记得省着点用哦。」";

                case EnemyId.Niulai:
                    return "「门关得挺快嘛……可惜我比门快。」";

                case EnemyId.Daishu:
                    return "「你刚才没开灯吧？那你怎么知道门外是谁呢。」";

                case EnemyId.Lulu:
                    return "「手电筒呢？这次好像闪得不够亮呀。」";

                default:
                    return string.Empty;
            }
        }

        private void OnEndPrimaryClicked()
        {
            int current = _enemyDirector != null ? _enemyDirector.NightIndex : 0;
            bool wasLast = current >= NightCount - 1;

            if (_endQuoteAudio != null)
            {
                _endQuoteAudio.Stop();
            }

            // 无尽模式只有「死」这一种结局，主按钮就是再来一局
            var gm = GameManager.Instance;
            if (gm != null && gm.EndlessMode)
            {
                OnEndlessClicked();
                return;
            }

            if (_endTitleText.text == "你被抓住了")
            {
                OnNightClicked(current);
                return;
            }

            if (wasLast)
            {
                OpenTitle();
                return;
            }

            OnNightClicked(current + 1);
        }

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------
        // 小工具（菜单专用，和主文件里的辅助并存）
        // ---------------------------------------------------------------

        private GameObject MakeFullScreenPanel(string name, Color color, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            StretchToParent((RectTransform)go.transform);
            go.GetComponent<Image>().color = color;
            return go;
        }

        private Text MakeLabel(Transform parent, string content, int size, Color color,
            Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 dimensions)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            PlaceRect(rect, anchor, pivot, offset, dimensions);

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>
        /// 菜单按钮。锚点由 <paramref name="anchor"/> 定（画布比例），offset 是相对锚点的偏移。
        /// </summary>
        private Button MakeMenuButton(Transform parent, string label, Vector2 offset, Vector2 anchor,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            PlaceRect(rect, anchor, new Vector2(0.5f, 0.5f), offset, new Vector2(320f, 84f));

            var image = go.GetComponent<Image>();
            image.sprite = MakeRoundedSprite();
            image.color = new Color(0.22f, 0.22f, 0.28f, 0.98f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            // 悬停 / 按下反馈：缩放 + 变亮。用 colorMultiplier 让 ColorTint 生效。
            image.type = Image.Type.Simple;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.9f, 0.9f, 0.9f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.55f);
            button.colors = colors;

            var labelText = MakeLabel(go.transform, label, 28, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 84f));
            labelText.raycastTarget = false;

            return button;
        }

        /// <summary>
        /// 圆角按钮贴图 —— 四个角按圆角半径挖成透明。
        /// 用 9 宫格（border）拉伸，圆角不会糊。
        /// </summary>
        private static Sprite MakeRoundedSprite()
        {
            const int size = 64;
            const int radius = 14;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "menu_rounded";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            var cx = radius + 0.5f;
            var cy = radius + 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 四个角的圆角区域：离圆心 > radius 就透明
                    bool corner = (x < radius && y < radius) || (x >= size - radius && y < radius)
                        || (x < radius && y >= size - radius) || (x >= size - radius && y >= size - radius);

                    byte alpha = 255;
                    if (corner)
                    {
                        float dx = 0f, dy = 0f;
                        if (x < radius && y < radius) { dx = x - cx; dy = y - cy; }
                        else if (x >= size - radius && y < radius) { dx = x - (size - cx); dy = y - cy; }
                        else if (x < radius && y >= size - radius) { dx = x - cx; dy = y - (size - cy); }
                        else { dx = x - (size - cx); dy = y - (size - cy); }

                        if (dx * dx + dy * dy > radius * radius)
                        {
                            alpha = 0;
                        }
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            // border = 圆角半径，9 宫格拉伸时圆角不变形
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0u, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
    }
}
