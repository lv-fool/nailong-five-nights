using System.Collections.Generic;
using NailongFNAF.Core;
using NailongFNAF.Gameplay;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 开始界面 + 关卡（夜晚）选择 + 进度解锁 —— <see cref="PrototypeScene"/> 的菜单部分。
    ///
    /// 流程（对齐 FNAF 1 的入口习惯）：
    ///   标题页 → 点「开始游戏」→ 关卡选择（第 1~5 夜）
    ///   → 选一夜 → 进办公室开始那一夜。
    ///   撑到 6 点 → 结算面板「通关」→ 解锁下一夜（存档）→ 可继续下一夜或回标题。
    ///   被抓 / 断电被抓 → 结算面板「失败」→ 重试本夜或回标题。
    ///
    /// 解锁进度存在 PlayerPrefs（键 <see cref="UnlockKey"/>），默认只开第 1 夜。
    /// 菜单开着时 <c>_menuOpen</c> 为 true，主循环会冻住这一夜。
    /// </summary>
    public partial class PrototypeScene
    {
        /// <summary>
        /// 夜晚总数：**第 1~5 夜**。
        ///
        /// 隐藏夜（原第 6 夜）暂时移除（用户 2026-10-22 定）——
        /// 内容还没做完，留着只会让人点进去看到半成品。
        /// 要恢复就把这里改回 6、<see cref="NightName"/> 的隐藏夜分支一起加回来。
        /// </summary>
        public const int NightCount = 5;

        /// <summary>解锁进度存档键：存「已解锁到第几夜」，0 起。</summary>
        public const string UnlockKey = "nailong.unlockedNight";

        /// <summary>永夜模式的纪录存档键：存「撑过的真实秒数」，越大越好。</summary>
        public const string EndlessBestKey = "nailong.endlessBest";

        /// <summary>永夜模式历史最高分。</summary>
        public const string EndlessBestScoreKey = "nailong.endlessBestScore";

        /// <summary>永夜模式的进度存档键：一行文本，格式见 <see cref="EternalNightSave"/>。</summary>
        public const string EternalSaveKey = "nailong.eternalSave";

        /// <summary>永夜模式开局用的难度夜（0 起）—— 与 <see cref="EndlessDifficulty.StartNight"/> 同源。</summary>
        public const int EndlessStartNight = EndlessDifficulty.StartNight;

        /// <summary>永夜模式当前难度档对应的夜（0 起）。</summary>
        private int _endlessStage = EndlessStartNight;

        /// <summary>永夜模式当前的加压系数（0 = 没加压）。</summary>
        private float _endlessOverdrive;

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
        private GameObject _pauseView;
        private GameObject _customView;
        private GameObject _achvView;

        // 自定义夜界面：每只敌人一行的活动度数字（下标 = (int)EnemyId）
        private readonly Text[] _customLevelTexts = new Text[EnemyIds.All.Length];
        private CustomNightConfig _customConfig;

        // 成就界面：计数 + 每行的名称 / 描述（顺序照 AchievementCatalog.All）
        private readonly Text[] _nightFragmentTexts = new Text[NightCount];
        private Text _achvFragmentText;
        private Text _achvCountText;
        private readonly Text[] _achvLineTexts = new Text[AchievementCatalog.All.Length];

        // 结算面板文案
        private Text _endTitleText;
        private Text _endBodyText;

        /// <summary>跳杀后那个敌人说的一句话（每只各有自己的台词）。</summary>
        private Text _endQuoteText;

        /// <summary>念那句台词的语音轨（素材可选，没有就只显示字幕）。</summary>
        private AudioSource _endQuoteAudio;

        /// <summary>
        /// 放 mp4 语音用的（只取音轨，不渲染画面）。
        /// 见 <see cref="BuildEndView"/> 里那段注释：微信/QQ 导出的「.mp3」
        /// 常常其实是 mp4 容器，Unity 只会把它导成 VideoClip。
        /// </summary>
        private VideoPlayer _endQuoteVideo;
        private Button _endPrimaryButton;
        private Text _endPrimaryLabel;

        /// <summary>死亡面板上的奶包道歉（只在「这局是她的锅」时出现）。</summary>
        private CanvasGroup _endNaibaoGroup;
        private Image _endNaibaoPortrait;
        private Text _endNaibaoText;

        // 暂停面板
        private Text _pauseInfoText;
        private Text _pauseSavedText;
        private bool _paused;
        private float _savedToastTimer;

        /// <summary>标题页「永夜模式 / 继续永夜」那颗按钮的文字，有存档时要换名字。</summary>
        private Text _endlessLabel;

        /// <summary>标题页「自定义夜」按钮：通关隐藏夜才解锁（照 FNAF 1 的第 7 夜）。</summary>
        private Button _customButton;
        private Text _customLabel;

        /// <summary>自定义夜解锁存档键：通关隐藏夜时写 1。</summary>
        public const string CustomUnlockedKey = "nailong.customUnlocked";

        /// <summary>已解锁到第几夜（0 起）。</summary>
        private static int UnlockedNight
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(UnlockKey, 0), 0, NightCount - 1); }
        }

        /// <summary>
        /// 夜晚显示名：第 1~5 夜。
        ///
        /// 隐藏夜暂时移除，所以不再有「第 6 夜 · 隐藏夜」这个特例 ——
        /// 现在最后一个就是「第 5 夜」。
        /// </summary>
        private static string NightName(int index)
        {
            return "第 " + (index + 1) + " 夜";
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
            BuildCustomView();
            BuildAchievementsView();
            BuildEndView();
            BuildPauseView();

            OpenTitle();
        }

        private void BuildTitleView()
        {
            _titleView = MakeFullScreenPanel("TitleView", new Color(0f, 0f, 0f, 0f), _menuRoot.transform);

            MakeLabel(_titleView.transform, "奶龙的五夜后宫", 72, new Color(0.95f, 0.85f, 0.3f),
                new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.82f), new Vector2(0f, 0f), new Vector2(1100f, 120f));
            MakeLabel(_titleView.transform, "NAILONG · FIVE NIGHTS", 26, new Color(0.7f, 0.7f, 0.75f),
                new Vector2(0.5f, 0.73f), new Vector2(0.5f, 0.73f), new Vector2(0f, 0f), new Vector2(1100f, 50f));

            // 分隔装饰线：放在副标题与按钮之间，别让它压到按钮
            MakeDivider(_titleView.transform, new Vector2(0.5f, 0.66f));

            // ⚠️ 按钮 84px 高 ≈ 画布 9.3%，**间距必须 ≥ 0.10**，否则会叠在一起（这里踩过）
            // 五个按钮依次排开：故事模式 / 自定义夜 / 永夜模式 / 成就 / 退出
            MakeMenuButton(_titleView.transform, "故事模式", Vector2.zero,
                new Vector2(0.5f, 0.57f), OnStartClicked);
            Button customButton = MakeMenuButton(_titleView.transform, "自定义夜", Vector2.zero,
                new Vector2(0.5f, 0.47f), OnCustomClicked);
            _customLabel = customButton.GetComponentInChildren<Text>();
            _customButton = customButton;
            Button endlessButton = MakeMenuButton(_titleView.transform, "永夜模式", Vector2.zero,
                new Vector2(0.5f, 0.37f), OnEndlessClicked);
            _endlessLabel = endlessButton.GetComponentInChildren<Text>();
            MakeMenuButton(_titleView.transform, "成就", Vector2.zero,
                new Vector2(0.5f, 0.27f), OnAchievementsClicked);
            MakeMenuButton(_titleView.transform, "退出", Vector2.zero,
                new Vector2(0.5f, 0.17f), OnQuitClicked);

            MakeLabel(_titleView.transform, "撑过 12 点到 6 点。鼠标看左右、升起监控、关门开灯、按手电筒。",
                20, new Color(0.55f, 0.55f, 0.6f),
                new Vector2(0.5f, 0.05f), new Vector2(0.5f, 0.05f), new Vector2(0f, 0f), new Vector2(1200f, 40f));
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
                new Vector2(0.5f, 0.88f), new Vector2(0.5f, 0.88f), new Vector2(0f, 0f), new Vector2(900f, 90f));

            MakeLabel(_levelView.transform, "通关前一晚，点亮下一夜。", 26,
                new Color(0.84f, 0.85f, 0.89f),
                new Vector2(0.5f, 0.79f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 44f));

            // 6 个夜晚按钮排两行。⚠️ 按钮 320px 宽 ≈ 画布 0.20，列距必须 ≥ 0.21 才不互相压边；
            // 锁着的按钮**不加「未解锁」后缀** —— 字一长就撑破按钮、和隔壁撞在一起，
            // 靠 interactable = false 的灰色已经够区分了。
            for (int i = 0; i < NightCount; i++)
            {
                int night = i;
                int col = i % 3;
                int row = i / 3;
                float x = (col - 1) * 0.27f;
                float y = 0.65f - row * 0.29f;
                MakeMenuCard(_levelView.transform, "NightCard" + i,
                    new Vector2(0.5f + x, y - 0.065f), new Vector2(470f, 270f));

                bool unlocked = i <= UnlockedNight;
                string label = NightName(i);

                var btn = MakeMenuButton(_levelView.transform, label, Vector2.zero,
                    new Vector2(0.5f + x, y),
                    unlocked ? (UnityEngine.Events.UnityAction)(() => OnNightClicked(night)) : null);
                btn.interactable = unlocked;

                // 独立于开局按钮：已解锁的夜晚可在局外慢慢读，不打断防守。
                Text fragment = MakeLabel(_levelView.transform, unlocked ? StoryFragments.Night(i) : "……", 28,
                    new Color(0.91f, 0.90f, 0.85f), new Vector2(0.5f + x, y - 0.115f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(414f, 132f));
                fragment.horizontalOverflow = HorizontalWrapMode.Wrap;
                fragment.verticalOverflow = VerticalWrapMode.Truncate;
                fragment.lineSpacing = 1.15f;
                fragment.alignment = TextAnchor.UpperLeft;
                _nightFragmentTexts[i] = fragment;
                fragment.name = "NightFragment" + i;
                fragment.raycastTarget = false;
            }

            MakeMenuButton(_levelView.transform, "返回", Vector2.zero,
                new Vector2(0.5f, 0.09f), OpenTitle);
        }

        /// <summary>
        /// 自定义夜界面：四个敌人各一行，± 按钮调活动度（0 ~ 20），
        /// 调完点「开始」。设置会存档（<see cref="CustomNightConfig"/>），下次进来还是上次的数。
        /// 照 FNAF 1 的第 7 夜：通关隐藏夜才解锁（按钮在标题页就锁着）。
        /// </summary>
        /// <summary>
        /// 自定义夜：照 FNAF 1 原版那屏布局 —— **四列横排**。
        ///
        ///   名字（Freddy / Bonnie / Chica / Foxy）
        ///   立绘方框
        ///   A.I. Level
        ///   ⟨ 20 ⟩
        ///
        /// 底下一条难度图例，右下一个 READY。
        /// 早先是四行竖排，改横排是为了照原版、也为了让立绘有地方放。
        /// </summary>
        private void BuildCustomView()
        {
            _customView = MakeFullScreenPanel("CustomView", new Color(0f, 0f, 0f, 0f), _menuRoot.transform);

            MakeLabel(_customView.transform, "自定义夜", 50, new Color(0.95f, 0.95f, 0.95f),
                new Vector2(0.5f, 0.90f), new Vector2(0.5f, 0.90f), new Vector2(0f, 0f), new Vector2(900f, 80f));

            // 四列均分：中心在 0.5 左右对称分布
            const float colSpread = 0.34f;   // 最左/最右列距中心多远
            float[] colX = new float[EnemyIds.All.Length];

            for (int i = 0; i < EnemyIds.All.Length; i++)
            {
                float t = EnemyIds.All.Length > 1
                    ? (i / (float)(EnemyIds.All.Length - 1) - 0.5f) * 2f
                    : 0f;

                colX[i] = 0.5f + t * colSpread;
            }

            for (int i = 0; i < EnemyIds.All.Length; i++)
            {
                EnemyId id = EnemyIds.All[i];
                float x = colX[i];

                // 名字
                MakeLabel(_customView.transform, CustomEnemyName(id), 30, Color.white,
                    new Vector2(x, 0.80f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 50f));

                // 立绘方框
                MakeCustomPortrait(id, new Vector2(x, 0.63f));

                // A.I. Level
                MakeLabel(_customView.transform, "A.I. Level", 22, new Color(0.85f, 0.85f, 0.88f),
                    new Vector2(x, 0.44f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 36f));

                // ⟨ 20 ⟩
                MakeStepperButton(_customView.transform, "−",
                    new Vector2(x - 0.055f, 0.36f), () => OnCustomStep(id, -1));
                _customLevelTexts[(int)id] = MakeLabel(_customView.transform, "0", 40, Color.white,
                    new Vector2(x, 0.36f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 60f));
                MakeStepperButton(_customView.transform, "+",
                    new Vector2(x + 0.055f, 0.36f), () => OnCustomStep(id, 1));
            }

            // 底部难度图例（照原版那句 (0-2)easy (3-6)med …）
            MakeLabel(_customView.transform, CustomDifficultyLegend(), 20,
                new Color(0.78f, 0.78f, 0.82f),
                new Vector2(0.36f, 0.14f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 40f));

            // 右下 READY（= 开始）
            MakeMenuButton(_customView.transform, "开始", Vector2.zero,
                new Vector2(0.86f, 0.14f), OnCustomStartClicked);

            // 左上返回
            MakeMenuButton(_customView.transform, "返回", Vector2.zero,
                new Vector2(0.10f, 0.92f), OpenTitle);

            _customView.SetActive(false);
        }

        /// <summary>四档难度图例，照原版那行字。</summary>
        private static string CustomDifficultyLegend()
        {
            return "(0-2) 简单     (3-6) 中等     (7-12) 困难     (13-20) 极限";
        }

        /// <summary>
        /// 自定义夜那一列顶上的名字。
        /// 只写名字（不带「走哪边」）—— 横排没地方塞后缀，塞了就撑破列宽。
        /// </summary>
        private static string CustomEnemyName(EnemyId id)
        {
            switch (id)
            {
                case EnemyId.Niulai:
                    return "牛来";
                case EnemyId.Daishu:
                    return "袋鼠";
                case EnemyId.Lulu:
                    return "噜噜";
                case EnemyId.Nailong:
                    return "奶龙";
                default:
                    return id.Code();
            }
        }

        /// <summary>
        /// 自定义夜每列的立绘方框。
        ///
        /// 用 Stare（正对镜头那张）—— 它最能代表「这只东西正看着你」，
        /// 也刚好是原版那屏的构图。素材缺失时退回 Idle，再缺就是一块暗色占位。
        /// </summary>
        private void MakeCustomPortrait(EnemyId id, Vector2 anchor)
        {
            const float w = 168f;
            const float h = 188f;

            var go = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_customView.transform, false);

            var rect = (RectTransform)go.transform;
            PlaceRect(rect, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));

            var image = go.GetComponent<Image>();
            image.sprite = MakeRoundedSprite();
            image.color = new Color(0.10f, 0.10f, 0.13f, 1f);
            image.type = Image.Type.Simple;
            image.raycastTarget = false;

            var art = FindCharacter(id);
            Sprite sprite = art != null ? art.Get(CharacterPose.Stare) : null;

            if (sprite == null)
            {
                return;   // 没素材就留一块暗框，别让界面缺一格看起来像坏了
            }

            // 立绘叠在方框里，按高度装满、居中 —— 原版那些头像也是撑满的
            var artGo = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            artGo.transform.SetParent(go.transform, false);

            var artRect = (RectTransform)artGo.transform;
            artRect.anchorMin = new Vector2(0.5f, 0.5f);
            artRect.anchorMax = new Vector2(0.5f, 0.5f);
            artRect.pivot = new Vector2(0.5f, 0.5f);
            artRect.anchoredPosition = Vector2.zero;

            var artImage = artGo.GetComponent<Image>();
            artImage.sprite = sprite;
            artImage.preserveAspect = true;
            artImage.raycastTarget = false;
            FitHeight(artRect, sprite, h);
        }

        /// <summary>自定义夜每行的小标题：名字 + 走哪边。</summary>
        private static string CustomEnemyLabel(EnemyId id)
        {
            switch (id)
            {
                case EnemyId.Niulai:
                    return "牛来 · 右门";
                case EnemyId.Daishu:
                    return "袋鼠 · 左门";
                case EnemyId.Lulu:
                    return "噜噜 · 正门";
                case EnemyId.Nailong:
                    return "奶龙 · 偷电";
                default:
                    return id.Code();
            }
        }

        /// <summary>
        /// 自定义夜的 ± 按钮（比菜单按钮小，64×64）。**支持按住连发**。
        ///
        /// ⚠️ 刻意**不用** Button.onClick：连发要靠 <see cref="RepeatButton"/>，
        /// 而它按下时自己会走一格。两个都挂上就会「按一次加两格」，
        /// 所以这里只保留 Button 做视觉（hover / 按下变色），不接 onClick。
        /// </summary>
        private Button MakeStepperButton(Transform parent, string label, Vector2 anchor,
            System.Action onRepeat)
        {
            var go = new GameObject("Stepper", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            PlaceRect(rect, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f));

            var image = go.GetComponent<Image>();
            image.sprite = MakeRoundedSprite();
            image.color = new Color(0.22f, 0.22f, 0.28f, 0.98f);
            image.type = Image.Type.Simple;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.9f, 0.9f, 0.9f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.55f);
            button.colors = colors;

            // 单击 + 长按连发都交给它
            var repeat = go.AddComponent<RepeatButton>();
            repeat.OnRepeat = onRepeat;

            var labelText = MakeLabel(go.transform, label, 36, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f));
            labelText.raycastTarget = false;

            return button;
        }

        /// <summary>
        /// 成就列表界面：两列，一条一行「★ 名字 · 条件」。
        /// 19 条（9 普通 + 10 彩蛋）单列排不下，所以按 10 条一列分两栏；
        /// 彩蛋未解锁时名字和条件都是 ???。
        /// </summary>
        private void BuildAchievementsView()
        {
            _achvView = MakeFullScreenPanel("AchievementsView", new Color(0f, 0f, 0f, 0f), _menuRoot.transform);

            MakeLabel(_achvView.transform, "成就", 52, new Color(0.95f, 0.85f, 0.3f),
                new Vector2(0.5f, 0.86f), new Vector2(0.5f, 0.86f), new Vector2(0f, 0f), new Vector2(900f, 80f));
            _achvCountText = MakeLabel(_achvView.transform, "", 28, new Color(0.85f, 0.87f, 0.92f),
                new Vector2(0.5f, 0.80f), new Vector2(0.5f, 0.80f), new Vector2(0f, 0f), new Vector2(900f, 40f));

            // 固定阅读窗口，条目在内部滚动；底部旁白与返回不受滚动影响。
            var scrollGo = new GameObject("AchievementScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(_achvView.transform, false);
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.anchorMin = new Vector2(0.10f, 0.23f);
            scrollRect.anchorMax = new Vector2(0.90f, 0.76f);
            scrollRect.offsetMin = scrollRect.offsetMax = Vector2.zero;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewport = (RectTransform)viewportGo.transform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.055f, 1f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            const float rowHeight = 142f;
            content.sizeDelta = new Vector2(0f, AchievementCatalog.All.Length * rowHeight + 12f);
            content.anchoredPosition = Vector2.zero;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 55f;
            scroll.inertia = true;

            viewport.offsetMax = new Vector2(-28f, 0f);
            Image track = MakeMenuCard(scrollGo.transform, "Scrollbar", new Vector2(1f, 0.5f), Vector2.zero);
            var trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(1f, 0f);
            trackRect.anchorMax = Vector2.one;
            trackRect.pivot = new Vector2(1f, 0.5f);
            trackRect.sizeDelta = new Vector2(18f, 0f);
            trackRect.anchoredPosition = Vector2.zero;
            Image handle = MakeMenuCard(track.transform, "Handle", new Vector2(0.5f, 0.5f), Vector2.zero);
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
            handle.color = new Color(0.85f, 0.71f, 0.37f, 1f);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;

            for (int i = 0; i < AchievementCatalog.All.Length; i++)
            {
                Image card = MakeMenuCard(content, "AchievementCard" + i,
                    new Vector2(0.5f, 1f), new Vector2(0f, 130f));
                var cardRect = (RectTransform)card.transform;
                cardRect.anchorMin = new Vector2(0f, 1f);
                cardRect.anchorMax = Vector2.one;
                cardRect.sizeDelta = new Vector2(-24f, 130f);
                cardRect.anchoredPosition = new Vector2(0f, -12f - i * rowHeight - 65f);
                Text line = MakeLabel(card.transform, "", 38, Color.white,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                line.rectTransform.anchorMin = Vector2.zero;
                line.rectTransform.anchorMax = Vector2.one;
                line.rectTransform.offsetMin = new Vector2(32f, 12f);
                line.rectTransform.offsetMax = new Vector2(-32f, -12f);
                line.horizontalOverflow = HorizontalWrapMode.Wrap;
                line.verticalOverflow = VerticalWrapMode.Truncate;
                line.alignment = TextAnchor.MiddleLeft;
                line.supportRichText = true;
                line.raycastTarget = false;
                int achievementIndex = i;
                var target = card.gameObject.AddComponent<Button>();
                target.targetGraphic = card;
                SetMenuButtonColors(target);
                target.onClick.AddListener(() => ShowAchievementFragment(achievementIndex));
                _achvLineTexts[i] = line;
            }

            MakeMenuButton(_achvView.transform, "返回", Vector2.zero,
                new Vector2(0.5f, 0.045f), OpenTitle);

            Image fragmentCard = MakeMenuCard(_achvView.transform, "AchievementMemory",
                new Vector2(0.5f, 0.12f), new Vector2(1450f, 92f));
            _achvFragmentText = MakeLabel(fragmentCard.transform, "点一下已解锁的成就。", 28,
                new Color(1f, 0.90f, 0.66f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1380f, 88f));
            _achvFragmentText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _achvFragmentText.raycastTarget = false;
            _achvView.SetActive(false);
        }

        private void ShowAchievementFragment(int index)
        {
            if (index < 0 || index >= AchievementCatalog.All.Length) return;
            AchievementCatalog.Def def = AchievementCatalog.All[index];
            _achvFragmentText.text = !AchievementStore.IsUnlocked(def.Id)
                ? "这一页还没有留下字。"
                : (string.IsNullOrEmpty(def.Flavor) ? "这一页暂时没有别的字。" : def.Flavor);
        }

        /// <summary>成就界面每次打开都刷新一遍（可能在别的局里刚解锁过）。</summary>
        private void RefreshAchievementsView()
        {
            _achvFragmentText.text = "点一下已解锁的成就。";
            _achvCountText.text = "已解锁 " + AchievementStore.UnlockedCount()
                + " / " + AchievementCatalog.All.Length;

            for (int i = 0; i < AchievementCatalog.All.Length; i++)
            {
                AchievementCatalog.Def def = AchievementCatalog.All[i];
                bool unlocked = AchievementStore.IsUnlocked(def.Id);
                bool masked = AchievementCatalog.ShouldMask(def);

                string title = masked ? "???" : def.Title;
                string desc = masked ? "???" : def.Description;

                _achvLineTexts[i].text = (unlocked ? "★ " : "☆ ") + "<b>" + title + "</b>"
                    + "\n<size=30>" + desc + "</size>";
                _achvLineTexts[i].color = unlocked
                    ? new Color(1f, 0.88f, 0.53f)
                    : (masked ? new Color(0.67f, 0.71f, 0.79f) : new Color(0.88f, 0.91f, 0.96f));
            }
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

            // 台词语音轨：素材缺失时静默，不影响任何流程。
            //
            // ⚠️ **挂在 canvas 上，别挂在 _endView 下** ——
            // _endView 平时是未激活的，而未激活的 GameObject 上
            // AudioSource 和 VideoPlayer 都不会播放。挂 canvas 上则随调随响，
            // 「离开结算页就停」由 StopEndQuoteVoice() 负责。
            var voiceGo = new GameObject("EndQuoteVoice");
            voiceGo.transform.SetParent(_canvas.transform, false);
            _endQuoteAudio = voiceGo.AddComponent<AudioSource>();
            _endQuoteAudio.playOnAwake = false;
            _endQuoteAudio.loop = false;
            _endQuoteAudio.spatialBlend = 0f;
            _endQuoteAudio.volume = 0.95f;

            // 有些语音是 **mp4 容器装的 AAC**（微信 / QQ 录音导出来常常是这种，
            // 扩展名却写着 .mp3）—— Unity 的音频导入器不认 mp4，只会把它当
            // VideoClip 导入。所以这里再挂一个 VideoPlayer，**只用它的音轨**：
            // 不渲染画面（APIOnly），声音输出到上面那个 AudioSource，
            // 这样两种素材（真音频 / mp4）都能放。
            _endQuoteVideo = voiceGo.AddComponent<VideoPlayer>();
            _endQuoteVideo.playOnAwake = false;
            _endQuoteVideo.isLooping = false;
            _endQuoteVideo.source = VideoSource.VideoClip;
            _endQuoteVideo.renderMode = VideoRenderMode.APIOnly;
            _endQuoteVideo.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _endQuoteVideo.controlledAudioTrackCount = 1;
            _endQuoteVideo.SetTargetAudioSource(0, _endQuoteAudio);

            BuildEndNaibaoApology();
        }

        /// <summary>
        /// 死亡面板上的奶包：如果这局是她的锅，她在左下角站着道歉。
        ///
        /// 为什么不做成游戏里那张卡：死亡面板是一层几乎不透明的全屏黑，
        /// 场景里的卡会被盖住 —— 只能画在面板自己身上。
        /// </summary>
        private void BuildEndNaibaoApology()
        {
            var go = new GameObject("EndNaibao", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_endView.transform, false);
            StretchToParent(go.GetComponent<RectTransform>());

            _endNaibaoGroup = go.GetComponent<CanvasGroup>();
            _endNaibaoGroup.alpha = 0f;
            _endNaibaoGroup.blocksRaycasts = false;

            var portraitGo = new GameObject("EndNaibaoPortrait",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            portraitGo.transform.SetParent(go.transform, false);
            PlaceRect(portraitGo.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(150f, 220f), new Vector2(160f, 344f));

            _endNaibaoPortrait = portraitGo.GetComponent<Image>();
            _endNaibaoPortrait.preserveAspect = true;
            _endNaibaoPortrait.raycastTarget = false;

            // 说话框：贴底一条，别压到上面三个按钮
            var box = new GameObject("EndNaibaoLine",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            box.transform.SetParent(go.transform, false);
            PlaceRect(box.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(470f, 112f), new Vector2(760f, 152f));

            var boxImage = box.GetComponent<Image>();
            boxImage.sprite = _buttonSprite;
            boxImage.color = new Color(0.06f, 0.07f, 0.09f, 0.9f);
            boxImage.raycastTarget = false;

            Text nameLabel = MakeText(box.transform, "EndNaibaoName", "奶包",
                TextAnchor.UpperLeft, 22, new Color(1f, 0.84f, 0.42f));
            PlaceRect(nameLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -10f), new Vector2(140f, 28f));

            _endNaibaoText = MakeText(box.transform, "EndNaibaoText", string.Empty,
                TextAnchor.UpperLeft, 24, new Color(0.94f, 0.94f, 0.94f));
            _endNaibaoText.horizontalOverflow = HorizontalWrapMode.Wrap;
            PlaceRect(_endNaibaoText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -42f), new Vector2(724f, 104f));
        }

        /// <summary>死亡面板要不要摆出奶包的道歉（她没背锅就收起来）。</summary>
        private void ShowEndNaibaoApology(GameManager gm)
        {
            if (_endNaibaoGroup == null)
            {
                return;
            }

            string line = gm != null ? gm.NaibaoApologyLine : null;
            if (string.IsNullOrEmpty(line))
            {
                _endNaibaoGroup.alpha = 0f;
                return;
            }

            if (_endNaibaoText != null)
            {
                _endNaibaoText.text = line;
            }

            if (_endNaibaoPortrait != null)
            {
                Sprite sprite = NaibaoSprite(NaibaoPose.Huffy);
                _endNaibaoPortrait.sprite = sprite;
                _endNaibaoPortrait.enabled = sprite != null;
            }

            _endNaibaoGroup.alpha = 1f;
        }

        /// <summary>
        /// 永夜模式的暂停面板：Esc 打开，时间 / 电量 / 敌人全冻住（靠 timeScale = 0）。
        /// 三个出路 —— 继续、存一档再走、直接放弃；存档留在 PlayerPrefs 里，
        /// 下次进标题页那颗按钮会变成「继续永夜」。
        /// </summary>
        private void BuildPauseView()
        {
            _pauseView = MakeFullScreenPanel("PauseView", new Color(0.02f, 0.02f, 0.03f, 0.92f), _menuRoot.transform);

            MakeLabel(_pauseView.transform, "暂停", 60, new Color(0.95f, 0.85f, 0.3f),
                new Vector2(0.5f, 0.76f), new Vector2(0.5f, 0.76f), new Vector2(0f, 0f), new Vector2(900f, 90f));

            _pauseInfoText = MakeLabel(_pauseView.transform, "", 26, new Color(0.75f, 0.75f, 0.8f),
                new Vector2(0.5f, 0.66f), new Vector2(0.5f, 0.66f), new Vector2(0f, 0f), new Vector2(1100f, 50f));

            _pauseSavedText = MakeLabel(_pauseView.transform, "", 24, new Color(0.6f, 0.85f, 0.6f),
                new Vector2(0.5f, 0.59f), new Vector2(0.5f, 0.59f), new Vector2(0f, 0f), new Vector2(1100f, 40f));

            MakeMenuButton(_pauseView.transform, "继续游戏", Vector2.zero,
                new Vector2(0.5f, 0.48f), ResumeFromPause);
            MakeMenuButton(_pauseView.transform, "保存进度", Vector2.zero,
                new Vector2(0.5f, 0.38f), OnPauseSave);
            MakeMenuButton(_pauseView.transform, "保存并退出", Vector2.zero,
                new Vector2(0.5f, 0.28f), OnPauseSaveAndExit);
            MakeMenuButton(_pauseView.transform, "放弃本局", Vector2.zero,
                new Vector2(0.5f, 0.18f), OnPauseAbandon);

            _pauseView.SetActive(false);
        }

        /// <summary>
        /// 每帧调一次。返回 true 表示这一帧已经被暂停逻辑接管（外面别再跑游戏更新）。
        /// 只有永夜模式能暂停 —— 故事模式按 Esc 什么都不做，免得把时钟停住刷时间。
        /// </summary>
        private bool HandlePause(GameManager gm)
        {
            if (_paused)
            {
                // 暂停时 timeScale = 0，计时都得用 unscaled
                if (_savedToastTimer > 0f)
                {
                    _savedToastTimer -= Time.unscaledDeltaTime;
                    if (_savedToastTimer <= 0f && _pauseSavedText != null)
                    {
                        _pauseSavedText.text = string.Empty;
                    }
                }

                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    ResumeFromPause();
                }

                return true;
            }

            if (gm.EndlessMode && !gm.HasPowerOut && !gm.HasBeenCaught &&
                Input.GetKeyDown(KeyCode.Escape))
            {
                OpenPause();
                return true;
            }

            return false;
        }

        /// <summary>打开暂停面板：冻住时间，写一行「撑了多久 / 危险度」。</summary>
        private void OpenPause()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.EndlessMode || gm.IsPaused)
            {
                return;
            }

            _paused = true;
            gm.SetPaused(true);

            if (_menuRoot != null)
            {
                _menuRoot.SetActive(true);
                _titleView.SetActive(false);
                _levelView.SetActive(false);
                _endView.SetActive(false);
                _customView.SetActive(false);
                _achvView.SetActive(false);
                _pauseView.SetActive(true);
            }

            if (_pauseInfoText != null && gm.Clock != null && gm.Recharge != null)
            {
                float hours = gm.Clock.ElapsedSeconds / Mathf.Max(1f, gm.Clock.SecondsPerHour);
                int danger = EndlessDifficulty.TierFor(hours, gm.Recharge.Noise) + 1;
                _pauseInfoText.text = "永夜 · 已撑 " + gm.Clock.CurrentHour + " 小时 · 危险 " + danger + "/" + NightCount;
            }

            if (_pauseSavedText != null)
            {
                _pauseSavedText.text = string.Empty;
            }

            _savedToastTimer = 0f;
        }

        /// <summary>继续游戏：面板收起、解冻。</summary>
        private void ResumeFromPause()
        {
            _paused = false;
            _savedToastTimer = 0f;

            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.SetPaused(false);
            }

            if (_pauseView != null)
            {
                _pauseView.SetActive(false);
            }

            if (_menuRoot != null)
            {
                _menuRoot.SetActive(false);
            }
        }

        /// <summary>存一档，面板不关（还能继续打），只弹一句「进度已保存」。</summary>
        private void OnPauseSave()
        {
            if (SaveEternalNight() && _pauseSavedText != null)
            {
                _pauseSavedText.text = "进度已保存";
                _savedToastTimer = 2.5f;
            }
        }

        /// <summary>存档后退回标题 —— 标题页那颗按钮会变成「继续永夜」。</summary>
        private void OnPauseSaveAndExit()
        {
            SaveEternalNight();
            ResumeFromPause();
            OpenTitle();
        }

        /// <summary>放弃本局：删掉存档，回标题，下次点永夜就是从零开始。</summary>
        private void OnPauseAbandon()
        {
            ClearEternalNightSave();
            ResumeFromPause();
            OpenTitle();
        }

        // ---------------------------------------------------------------
        // 永夜模式的存档（PlayerPrefs 一行文本，见 EternalNightSave）
        // ---------------------------------------------------------------

        /// <summary>把当前进度写进 PlayerPrefs。成功返回 true。</summary>
        private static bool SaveEternalNight()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                return false;
            }

            var save = gm.CaptureSave();
            if (save == null)
            {
                return false;
            }

            PlayerPrefs.SetString(EternalSaveKey, save.ToStorage());
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>读存档；没有或者格式不对都返回 false。</summary>
        private static bool TryLoadEternalNight(out EternalNightSave save)
        {
            string raw = PlayerPrefs.GetString(EternalSaveKey, string.Empty);
            return EternalNightSave.TryParse(raw, out save);
        }

        private static void ClearEternalNightSave()
        {
            PlayerPrefs.DeleteKey(EternalSaveKey);
            PlayerPrefs.Save();
        }

        /// <summary>标题页按钮文案：有存档就叫「继续永夜」，顺带报一下撑了多久。</summary>
        private void RefreshTitleButtons()
        {
            // 自定义夜：通关隐藏夜才解锁，没解锁就灰着并把条件写在按钮上
            if (_customButton != null && _customLabel != null)
            {
                bool customUnlocked = CustomNightUnlocked;
                _customButton.interactable = customUnlocked;
                _customLabel.text = customUnlocked ? "自定义夜" : "自定义夜 · 未解锁";
            }

            if (_endlessLabel == null)
            {
                return;
            }

            EternalNightSave save;
            if (!TryLoadEternalNight(out save))
            {
                _endlessLabel.text = "永夜模式";
                return;
            }

            _endlessLabel.text = save.Hour >= 1
                ? "继续永夜（" + save.Hour + " 小时）"
                : "继续永夜";
        }

        /// <summary>按「已撑多久 + 动静」把敌人顶到对应难度（读档后立刻生效）。</summary>
        private void ApplyEternalDifficulty(GameManager gm)
        {
            if (_enemyDirector == null || gm == null || gm.Clock == null)
            {
                return;
            }

            float hours = gm.Clock.ElapsedSeconds / Mathf.Max(1f, gm.Clock.SecondsPerHour);
            float noise = gm.Recharge != null ? gm.Recharge.Noise : 0f;

            _endlessStage = EndlessDifficulty.TierFor(hours, noise);
            _endlessOverdrive = EndlessDifficulty.OverdriveFor(hours, noise);
            _enemyDirector.SetNight(_endlessStage);
            _enemyDirector.SetOverdrive(_endlessOverdrive);
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
                PlayDawnThenWin();
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
            if (_customView != null)
            {
                _customView.SetActive(false);
            }

            if (_achvView != null)
            {
                _achvView.SetActive(false);
            }

            if (_pauseView != null)
            {
                _pauseView.SetActive(false);
            }

            _paused = false;
            _savedToastTimer = 0f;
            RefreshTitleButtons();

            // 暂停这一夜：按一下重开，让时钟/敌人停在原地，等玩家选好夜晚再真正开始
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Restart();
                ClearJumpscare();
            }

            _endShown = false;
            _caughtHoldTimer = 0f;
            ResetDawn();
        }

        private void OnStartClicked()
        {
            _menuOpen = true;
            _titleView.SetActive(false);
            _levelView.SetActive(true);
            _endView.SetActive(false);
            _customView.SetActive(false);
            _achvView.SetActive(false);

            // 刷新关卡按钮的锁态（可能刚通关解锁了下一夜）
            RefreshLevelButtons();
        }

        /// <summary>标题页「自定义夜」：打开调节界面，读上次的设置。</summary>
        private void OnCustomClicked()
        {
            if (!CustomNightUnlocked)
            {
                return;
            }

            _menuOpen = true;
            _menuRoot.SetActive(true);
            _menuRoot.transform.SetAsLastSibling();
            _titleView.SetActive(false);
            _levelView.SetActive(false);
            _endView.SetActive(false);
            _achvView.SetActive(false);
            _customView.SetActive(true);

            _customConfig = CustomNightConfig.Load();
            RefreshCustomLabels();
        }

        /// <summary>标题页「成就」。</summary>
        private void OnAchievementsClicked()
        {
            _menuOpen = true;
            _menuRoot.SetActive(true);
            _menuRoot.transform.SetAsLastSibling();
            _titleView.SetActive(false);
            _levelView.SetActive(false);
            _endView.SetActive(false);
            _customView.SetActive(false);
            _achvView.SetActive(true);

            RefreshAchievementsView();
        }

        /// <summary>自定义夜某只敌人 ±1（CustomNightConfig 会夹在 0 ~ 20）。</summary>
        private void OnCustomStep(EnemyId id, int delta)
        {
            if (_customConfig == null)
            {
                _customConfig = CustomNightConfig.Load();
            }

            _customConfig.SetLevel(id, _customConfig.GetLevel(id) + delta);
            RefreshCustomLabels();
        }

        /// <summary>自定义夜「开始」：先存档，再按设置开局。</summary>
        private void OnCustomStartClicked()
        {
            if (_customConfig != null)
            {
                _customConfig.Save();
            }

            StartCustomNight();
        }

        private void RefreshCustomLabels()
        {
            if (_customConfig == null)
            {
                return;
            }

            foreach (EnemyId id in EnemyIds.All)
            {
                if (_customLevelTexts[(int)id] != null)
                {
                    _customLevelTexts[(int)id].text = _customConfig.GetLevel(id).ToString();
                }
            }
        }

        /// <summary>自定义夜是否已解锁（通关隐藏夜）。</summary>
        private static bool CustomNightUnlocked
        {
            get { return PlayerPrefs.GetInt(CustomUnlockedKey, 0) == 1; }
        }

        private void RefreshLevelButtons()
        {
            for (int i = 0; i < NightCount; i++)
            {
                if (_nightFragmentTexts[i] != null)
                    _nightFragmentTexts[i].text = i <= UnlockedNight ? StoryFragments.Night(i) : "……";
            }

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
                    label.text = NightName(i);
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
                // 回普通夜：关掉自定义夜的覆盖
                _enemyDirector.ClearCustomLevels();
                _enemyDirector.SetNight(night);
            }

            // 走普通流程时把永夜模式关掉 —— 否则时钟还是「永远不到天亮」那一套
            gm.SetEndless(false);
            _endlessStage = EndlessStartNight;
            _endlessOverdrive = 0f;

            gm.Restart();
            gm.SetNaibao(CreateNaibaoForNight(night));
            ClearJumpscare();

            _menuRoot.SetActive(false);
            _menuOpen = false;
            _endShown = false;
            _caughtHoldTimer = 0f;
            ResetDawn();
        }

        /// <summary>
        /// 标题页「永夜模式」：撑不到天亮，敌人一轮轮来；操作能换电，但也会把难度抬上去。
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

            if (_enemyDirector != null)
            {
                // 永夜有自己的难度爬升，自定义夜的覆盖要关掉
                _enemyDirector.ClearCustomLevels();
            }

            _endlessStage = EndlessStartNight;
            _endlessOverdrive = 0f;
            if (_enemyDirector != null)
            {
                _enemyDirector.SetNight(EndlessStartNight);
                _enemyDirector.SetOverdrive(0f);
            }

            // 有存档就接着上次跑（标题页那颗按钮这时写着「继续永夜」），
            // 没有就是新的一局 —— 顺手把残留的旧档清掉。
            EternalNightSave save;
            bool resume = TryLoadEternalNight(out save);

            gm.Restart();
            gm.SetNaibao(null);
            if (resume)
            {
                if (gm.LoadSave(save))
                {
                    ApplyEternalDifficulty(gm);
                }
            }
            else
            {
                ClearEternalNightSave();
            }

            ClearJumpscare();

            _menuRoot.SetActive(false);
            _menuOpen = false;
            _endShown = false;
            _caughtHoldTimer = 0f;
            ResetDawn();
        }

        /// <summary>
        /// 永夜模式难度爬升：时间越长越难，玩家动静越大也越难（见 <see cref="EndlessDifficulty"/>）。
        /// 爬到第 6 夜之后不再换夜，改用 <see cref="EnemyDirector.SetOverdrive"/> 继续加压。
        ///
        /// 只有档位 / 加压真的变了才调 —— SetNight 与 SetOverdrive 都会重设整局活动度，不能每帧调。
        /// </summary>
        private void UpdateEndlessRamp(GameManager gm)
        {
            if (gm == null || !gm.EndlessMode || _enemyDirector == null || gm.Clock == null)
            {
                return;
            }

            float hours = gm.Clock.ElapsedSeconds / Mathf.Max(1f, gm.Clock.SecondsPerHour);
            float noise = gm.Recharge != null ? gm.Recharge.Noise : 0f;

            int stage = EndlessDifficulty.TierFor(hours, noise);
            float overdrive = EndlessDifficulty.OverdriveFor(hours, noise);

            if (stage != _endlessStage)
            {
                _endlessStage = stage;
                _enemyDirector.SetNight(stage);
            }

            // 加压一档一档往上走（0.22），变化够大才重设，免得每帧刷敌人状态
            if (Mathf.Abs(overdrive - _endlessOverdrive) > 0.05f)
            {
                _endlessOverdrive = overdrive;
                _enemyDirector.SetOverdrive(overdrive);
            }
        }

        // ── 通关天亮 ──────────────────────────────────────────────────

        /// <summary>天亮渐白用时（秒）。</summary>
        private const float DawnRiseSeconds = 1.8f;

        /// <summary>白到顶之后停多久再弹结算（秒）。</summary>
        private const float DawnHoldSeconds = 0.9f;

        /// <summary>白幕最亮时的 alpha —— 不到 1，留一点办公室的影子。</summary>
        private const float DawnPeakAlpha = 0.95f;

        /// <summary>
        /// 通关那一刻：屏幕从当前画面逐渐变亮到近白，停一会儿，再弹结算。
        ///
        /// 照 FNAF 1 的 6AM —— 先看见「天亮了」，才知道自己活下来了；
        /// 直接蹦结算面板，那一夜的紧张没有落地的地方。
        /// </summary>
        private void PlayDawnThenWin()
        {
            if (_dawnOverlay == null)
            {
                ShowWinEnd();
                return;
            }

            // 用「白幕开着没有」当状态，不另立布尔 —— 少一个要记得重置的字段
            if (!_dawnOverlay.gameObject.activeSelf)
            {
                _dawnOverlay.gameObject.SetActive(true);
                _dawnOverlay.transform.SetAsLastSibling();
                _dawnTimer = 0f;
            }

            _dawnTimer += Time.deltaTime;

            // 缓出：先快后慢，像真的天在亮，而不是线性拉白
            float rise = Mathf.Clamp01(_dawnTimer / DawnRiseSeconds);
            rise = 1f - (1f - rise) * (1f - rise);

            _dawnOverlay.color = new Color(1f, 1f, 1f, rise * DawnPeakAlpha);

            if (_dawnTimer >= DawnRiseSeconds + DawnHoldSeconds)
            {
                ShowWinEnd();
            }
        }

        /// <summary>收起天亮白幕（通关演出中途回标题 / 重开时用）。</summary>
        private void ResetDawn()
        {
            _dawnTimer = 0f;

            if (_dawnOverlay != null)
            {
                _dawnOverlay.gameObject.SetActive(false);
            }
        }

        private void ShowWinEnd()
        {
            // 天亮白幕到此为止 —— 结算面板要露出来
            if (_dawnOverlay != null)
            {
                _dawnOverlay.gameObject.SetActive(false);
            }

            _endShown = true;
            _menuOpen = true;
            _menuRoot.SetActive(true);
            _menuRoot.transform.SetAsLastSibling(); // 置于跳杀层之上
            _titleView.SetActive(false);
            _levelView.SetActive(false);
            _customView.SetActive(false);
            _achvView.SetActive(false);
            _endView.SetActive(true);

            var gm = GameManager.Instance;
            int current = _enemyDirector != null ? _enemyDirector.NightIndex : 0;
            bool custom = _enemyDirector != null && _enemyDirector.CustomActive;
            bool wasLast = !custom && current >= NightCount - 1;
            bool unlockedNext = false;

            // 赢了就没有道歉这回事
            ShowEndNaibaoApology(null);

            // 自定义夜不动解锁进度 —— 它不属于任何一夜
            if (!custom && !wasLast && current + 1 > UnlockedNight)
            {
                PlayerPrefs.SetInt(UnlockKey, current + 1);
                PlayerPrefs.Save();
                unlockedNext = true;
            }

            // 通关最后一夜 → 解锁自定义夜（照 FNAF 1 的第 7 夜）。
            // 隐藏夜移除后，「最后一夜」就是第 5 夜 —— 这里靠 NightCount 自动跟随。
            if (wasLast && !CustomNightUnlocked)
            {
                PlayerPrefs.SetInt(CustomUnlockedKey, 1);
                PlayerPrefs.Save();
            }

            // 通关第 5 夜也是「贝利亚之夜」的第一个解锁条件（另一个是听完一通电话）。
            if (wasLast)
            {
                BelialUnlock.MarkNight5Cleared();
            }

            string achievementLine = CheckWinAchievements(gm, current, custom, wasLast);

            _endTitleText.text = custom ? "自定义夜 通关" : (wasLast ? "全部通关！" : NightName(current) + " 通关");
            _endBodyText.text = custom
                ? "你自调的难度也拿你没办法。" + achievementLine
                : (wasLast
                    ? "你撑过了所有夜晚 —— 奶龙也拿你没办法。" + achievementLine
                    : (unlockedNext ? "已解锁 " + NightName(current + 1) : "天亮了，你活下来了。") + achievementLine);

            _endPrimaryLabel.text = custom ? "再来一局" : (wasLast ? "返回标题" : "下一夜");
            _endPrimaryButton.interactable = custom || !wasLast;

            // 通关没有「谁说了句话」，清掉上一局残留的台词
            if (_endQuoteText != null)
            {
                _endQuoteText.text = string.Empty;
            }
        }

        /// <summary>
        /// 通关结算时检查成就，返回要追加在结算文案后面的几行（没有新成就就是空串）。
        /// 判定都靠 GameManager / EnemyDirector 的现状快照，不碰流程。
        ///
        /// 普通成就是「打到哪里」，彩蛋（隐藏成就）看的是「怎么活下来的」。
        /// </summary>
        private string CheckWinAchievements(GameManager gm, int current, bool custom, bool wasLast)
        {
            if (gm == null)
            {
                return string.Empty;
            }

            var newly = new System.Collections.Generic.List<AchievementCatalog.Def>();
            NightStats stats = gm.Stats;

            if (custom)
            {
                int total = CustomNightConfig.Load().TotalLevel();
                int max = EnemyBrain.MaxActivityLevel * EnemyIds.All.Length;

                if (total >= max)
                {
                    TryUnlock(AchievementCatalog.CustomMax, newly);

                    // 彩蛋「满格」：四只全 20，通关时还剩一格拉碴电
                    if (gm.Power != null && gm.Power.Level >= 1f)
                    {
                        TryUnlock(AchievementCatalog.FullHouse, newly);
                    }
                }
            }
            else
            {
                if (current == 0) TryUnlock(AchievementCatalog.FirstNight, newly);
                if (current == 2) TryUnlock(AchievementCatalog.HalfWay, newly);
                if (current == 4) TryUnlock(AchievementCatalog.FiveNights, newly);

                // 隐藏夜暂时移除，这条成就一起撤掉 ——
                // 否则通关第 5 夜会同时跳「把过去锁在门后」和「让影子先走」两条。
                // 恢复隐藏夜时取消注释。
                // if (wasLast) TryUnlock(AchievementCatalog.HiddenNight, newly);

                // 断电后仍撑到 6 点（断电演出没抓到人）
                if (gm.HasPowerOut) TryUnlock(AchievementCatalog.BlackoutSurvivor, newly);
            }

            // 剩余电量 ≥ 30%（故事 / 自定义都算）
            if (gm.Power != null && gm.Power.Level >= 30f)
            {
                TryUnlock(AchievementCatalog.PowerSaver, newly);
            }

            // 单夜逼退噜噜 3 次
            if (_enemyDirector != null && _enemyDirector.LuluRetreatCount >= 3)
            {
                TryUnlock(AchievementCatalog.LuluMaster, newly);
            }

            // 彩蛋：这一夜的账本
            if (stats != null)
            {
                if (stats.DoorToggles >= 12) TryUnlock(AchievementCatalog.DoorKeeper, newly);
                if (stats.MonitorToggles == 0) TryUnlock(AchievementCatalog.NoLookUp, newly);
                if (stats.LightToggles == 0) TryUnlock(AchievementCatalog.NoLight, newly);

                int cameraCount = _enemyDirector != null ? _enemyDirector.CameraCount : 0;
                if (stats.HasVisitedAll(cameraCount)) TryUnlock(AchievementCatalog.EveryCorner, newly);

                if (stats.KitchenSeconds >= 20f) TryUnlock(AchievementCatalog.KitchenSilence, newly);
                if (stats.NailongLooks >= 3) TryUnlock(AchievementCatalog.ItBlinksFirst, newly);
                if (stats.TotalSwitchToggles >= 40) TryUnlock(AchievementCatalog.FingerRhythm, newly);
            }

            return AchievementLine(newly);
        }

        /// <summary>
        /// 失败结算时的彩蛋：只判「死在第几个小时」那两条。
        /// 永夜模式没有 6 点，不判。
        /// </summary>
        private string CheckLoseAchievements(GameManager gm)
        {
            if (gm == null || gm.EndlessMode || gm.Stats == null)
            {
                return string.Empty;
            }

            var newly = new System.Collections.Generic.List<AchievementCatalog.Def>();

            if (gm.Stats.EndedAfterHour(5.8333f)) TryUnlock(AchievementCatalog.FiveFiftyNine, newly);
            if (gm.Stats.EndedBeforeHour(0.5f)) TryUnlock(AchievementCatalog.ZeroTwenty, newly);

            return AchievementLine(newly);
        }

        /// <summary>把这次新解锁的成就拼成「新成就：xxx」，后面跟它们的世界观旁白。</summary>
        private static string AchievementLine(System.Collections.Generic.List<AchievementCatalog.Def> newly)
        {
            if (newly == null || newly.Count == 0)
            {
                return string.Empty;
            }

            var titles = new System.Collections.Generic.List<string>();
            foreach (AchievementCatalog.Def def in newly)
            {
                titles.Add(def.Title);
            }

            string line = "\n新成就：" + string.Join("、", titles.ToArray());

            foreach (AchievementCatalog.Def def in newly)
            {
                if (!string.IsNullOrEmpty(def.Flavor))
                {
                    line += "\n「" + def.Flavor + "」";
                }
            }

            return line;
        }

        /// <summary>解锁并在 <paramref name="newly"/> 里记下定义；不是新解锁就不记。</summary>
        private static void TryUnlock(string id, System.Collections.Generic.List<AchievementCatalog.Def> newly)
        {
            if (!AchievementStore.Unlock(id))
            {
                return;
            }

            AchievementCatalog.Def def = AchievementCatalog.Find(id);
            if (def != null)
            {
                newly.Add(def);
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
            _customView.SetActive(false);
            _achvView.SetActive(false);
            _endView.SetActive(true);

            var gm = GameManager.Instance;
            if (gm != null && gm.EndlessMode)
            {
                ShowEndlessLoseEnd(gm);
                return;
            }

            bool custom = _enemyDirector != null && _enemyDirector.CustomActive;
            int current = _enemyDirector != null ? _enemyDirector.NightIndex : 0;

            // 被咬死的次数是「贝利亚之夜」的第一个解锁条件（只算前五夜，自定义夜不算）。
            if (!custom)
            {
                BelialUnlock.NoteDeath();
            }

            _endTitleText.text = "你被抓住了";
            _endBodyText.text = (custom
                ? "自定义夜 失败 —— 再来一次。"
                : NightName(current) + " 失败 —— 再来一次。") + CheckLoseAchievements(gm);
            _endPrimaryLabel.text = custom ? "重试" : "重试本夜";
            _endPrimaryButton.interactable = true;

            // 跳杀完，那只敌人说一句话。
            // 文本和语音用**同一次抽签**的结果 —— 分两次抽就会各说各的。
            int quoteIndex;
            _endQuoteText.text = QuoteFor(_jumpscareEnemy, out quoteIndex);
            PlayEndQuoteVoice(_jumpscareEnemy, quoteIndex);

            // 这局要算她的锅，她在左下角道个歉
            ShowEndNaibaoApology(gm);
        }

        /// <summary>
        /// 永夜模式的结算：报「撑了多久 / 破没破纪录」，主按钮直接再来一局。
        /// 纪录按游戏内时长（小时）算，用真实秒数存档，换节奏调参也不会作废旧纪录。
        /// </summary>
        private void ShowEndlessLoseEnd(GameManager gm)
        {
            // 这一局已经结束：存档作废，标题页下次点永夜就是从零开始
            ClearEternalNightSave();
            float secondsPerHour = gm.Clock != null ? gm.Clock.SecondsPerHour : NightClock.DefaultSecondsPerHour;
            int survived = gm.Clock != null ? Mathf.FloorToInt(gm.Clock.ElapsedSeconds) : 0;
            int best = EndlessBestSeconds;

            bool isRecord = survived > best;
            int score = gm.Score != null ? gm.Score.Score : EndlessScore.ScoreFor(survived, 0, secondsPerHour);
            int bestScore = EndlessBestScore;

            if (score > bestScore)
            {
                PlayerPrefs.SetInt(EndlessBestScoreKey, score);
                bestScore = score;
            }

            if (isRecord)
            {
                PlayerPrefs.SetInt(EndlessBestKey, survived);
                best = survived;
            }

            PlayerPrefs.Save();

            // 永夜成就：一局撑过 12 小时
            var newly = new System.Collections.Generic.List<AchievementCatalog.Def>();
            if (secondsPerHour > 0f && survived / secondsPerHour >= 12f)
            {
                TryUnlock(AchievementCatalog.EndlessDozen, newly);
            }

            string achievementLine = AchievementLine(newly);

            _endTitleText.text = isRecord ? "新纪录！" : "永夜模式结束";
            _endBodyText.text = "撑过 " + FormatSpan(survived, secondsPerHour)
                + (isRecord ? " · 这是你的最好成绩" : " · 最佳 " + FormatSpan(best, secondsPerHour))
                + " · 得分 " + score + " · 挡了 " + (gm.Score != null ? gm.Score.Blocks : 0)
                + " 次 · 历史最高 " + bestScore
                + achievementLine;

            _endPrimaryLabel.text = "再来一局";
            _endPrimaryButton.interactable = true;

            // 永夜模式死法多半是被抓，台词照旧；没抓到就没台词
            int quoteIndex;
            _endQuoteText.text = QuoteFor(_jumpscareEnemy, out quoteIndex);
            PlayEndQuoteVoice(_jumpscareEnemy, quoteIndex);

            // 永夜模式她不全程在场，一律收起来
            ShowEndNaibaoApology(null);
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

        /// <summary>永夜模式最好成绩（真实秒数），没打过就是 0。</summary>
        /// <summary>永夜模式历史最高分（没有存档就是 0）。</summary>
        private static int EndlessBestScore
        {
            get
            {
                int value = PlayerPrefs.GetInt(EndlessBestScoreKey, 0);
                return value < 0 ? 0 : value;
            }
        }

        private static int EndlessBestSeconds
        {
            get
            {
                int value = PlayerPrefs.GetInt(EndlessBestKey, 0);
                return value > 0 ? value : 0;
            }
        }

        /// <summary>
        /// 播「被谁抓到、说了哪一句」的语音。
        ///
        /// 素材放 <c>Assets/Resources/Audio/</c>，**从精确到笼统两级回退**：
        /// <list type="number">
        /// <item><c>voice_&lt;代号&gt;_&lt;句号&gt;</c> —— 如 <c>voice_nailong_1.mp3</c>，
        ///   对应台词池里第 2 句（下标 0 起，和「11. 编辑跳杀台词」窗口里显示的序号一致）。</item>
        /// <item><c>voice_&lt;代号&gt;</c> —— 如 <c>voice_nailong.mp3</c>，
        ///   这一只的通用录音（池子里每句都用它）。</item>
        /// </list>
        ///
        /// 每一级**先试音频、再试 mp4**：微信 / QQ 导出的录音常常是
        /// 「mp4 容器装的 AAC，扩展名却写着 .mp3」，Unity 的音频导入器不认，
        /// 只会导成 <see cref="VideoClip"/>。这种就走 VideoPlayer 只放音轨。
        ///
        /// 都没有就**只显示字幕**，不报错、不影响流程 ——
        /// 所以语音可以一条一条慢慢补，缺哪句就用哪句的文字版。
        /// </summary>
        private void PlayEndQuoteVoice(EnemyId id, int quoteIndex)
        {
            if (_endQuoteAudio == null)
            {
                return;
            }

            StopEndQuoteVoice();

            string code = id.Code();

            // ① 按句 —— 字幕和录音严格对应
            if (quoteIndex >= 0
                && PlayVoiceAt("Audio/voice_" + code + "_" + quoteIndex))
            {
                return;
            }

            // ② 按敌人 —— 没做逐句录音时的通用版
            if (PlayVoiceAt("Audio/voice_" + code))
            {
                return;
            }

            // 都没有 —— 静默跳过，字幕照常显示
        }

        /// <summary>
        /// 尝试播放某条路径的语音。**先当音频、再当 mp4**。
        /// 放到了返回 true；两样都不是返回 false（调用方继续回退）。
        /// </summary>
        private bool PlayVoiceAt(string path)
        {
            // ⓐ 真音频（mp3 / wav / ogg…）
            AudioClip clip = Resources.Load<AudioClip>(path);
            if (clip != null)
            {
                _endQuoteAudio.clip = clip;
                _endQuoteAudio.Play();
                return true;
            }

            // ⓑ mp4 容器（只取音轨，不渲染画面）
            if (_endQuoteVideo != null)
            {
                VideoClip video = Resources.Load<VideoClip>(path);
                if (video != null)
                {
                    _endQuoteVideo.clip = video;
                    _endQuoteVideo.Play();
                    return true;
                }
            }

            return false;
        }

        /// <summary>停掉台词语音（音频和 mp4 两条路都要停，不然会串音）。</summary>
        private void StopEndQuoteVoice()
        {
            if (_endQuoteAudio != null)
            {
                _endQuoteAudio.Stop();
            }

            if (_endQuoteVideo != null && _endQuoteVideo.isPlaying)
            {
                _endQuoteVideo.Stop();
            }
        }

        /// <summary>上一条说出去的台词（按敌人分），用来避免连续两次说同一句。</summary>
        private static readonly Dictionary<EnemyId, string> _lastQuote =
            new Dictionary<EnemyId, string>();

        /// <summary>
        /// 每只敌人被抓到后从**台词池**里随机抽一句。
        ///
        /// **优先读 Assets/Resources/death_quotes.json** —— 那是由编辑器窗口
        /// 「奶龙 / 11. 编辑跳杀台词」维护的；文件不存在或某只没写，
        /// 就退回 <see cref="DeathQuotes.DefaultPool"/> 里那套写死的文案。
        ///
        /// 抽的时候会避开这一只**上一次说过的那句**，免得连续两次被抓听到同样的话。
        /// 池子只有一句时只能重复（没办法）。
        /// </summary>
        private static string QuoteFor(EnemyId id)
        {
            int ignored;
            return QuoteFor(id, out ignored);
        }

        /// <summary>
        /// 同 <see cref="QuoteFor(EnemyId)"/>，但**把抽中的是池子里第几句一起带出来**。
        ///
        /// 结算画面拿这个下标去载对应的语音 —— 台词是随机抽的，
        /// 只有一条通用录音必然会出现「说的和写的不是同一句」。
        /// </summary>
        private static string QuoteFor(EnemyId id, out int quoteIndex)
        {
            DeathQuotes quotes = LoadDeathQuotes();

            if (quotes == null)
            {
                quotes = new DeathQuotes();   // 纯默认池
            }

            string previous;
            _lastQuote.TryGetValue(id, out previous);

            string line;
            quoteIndex = quotes.RandomLineIndexFor(id, previous, out line);

            if (!string.IsNullOrEmpty(line))
            {
                _lastQuote[id] = line;
            }

            return line;
        }

        /// <summary>缓存的台词表 —— 只在第一次用时读一次 Resources。</summary>
        private static DeathQuotes _deathQuotes;
        private static bool _deathQuotesLoaded;

        /// <summary>读台词文件；读不到返回 null（调用方退回默认）。</summary>
        private static DeathQuotes LoadDeathQuotes()
        {
            if (_deathQuotesLoaded)
            {
                return _deathQuotes;
            }

            _deathQuotesLoaded = true;

            var asset = Resources.Load<TextAsset>(DeathQuotes.ResourcePath);
            if (asset == null)
            {
                return null;
            }

            DeathQuotes parsed;
            if (DeathQuotes.TryParse(asset.text, out parsed))
            {
                _deathQuotes = parsed;
            }

            return _deathQuotes;
        }

        private void OnEndPrimaryClicked()
        {
            bool custom = _enemyDirector != null && _enemyDirector.CustomActive;
            int current = _enemyDirector != null ? _enemyDirector.NightIndex : 0;
            bool wasLast = !custom && current >= NightCount - 1;

            // 音频和 mp4 两条路都要停，不然离开结算页还在说
            StopEndQuoteVoice();

            // 永夜模式只有「死」这一种结局，主按钮就是再来一局
            var gm = GameManager.Instance;
            if (gm != null && gm.EndlessMode)
            {
                OnEndlessClicked();
                return;
            }

            // 自定义夜：胜（再来一局）负（重试）都是按当前设置重开
            if (custom)
            {
                StartCustomNight();
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

        /// <summary>
        /// 按存档里的自定义设置开一局自定义夜。
        /// 顺序照 <see cref="OnEndlessClicked"/> 的教训：先设好模式与难度，再 Restart。
        /// </summary>
        private void StartCustomNight()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            CustomNightConfig config = CustomNightConfig.Load();

            gm.SetEndless(false);
            _endlessStage = EndlessStartNight;
            _endlessOverdrive = 0f;

            if (_enemyDirector != null)
            {
                _enemyDirector.SetCustomLevels(config);
                _enemyDirector.SetOverdrive(0f);
            }

            gm.Restart();

            // 自定义夜没有「第几夜」的概念，默认她已经过了引导期
            gm.SetNaibao(new NaibaoDirector(false, null));

            ClearJumpscare();

            _menuRoot.SetActive(false);
            _menuOpen = false;
            _endShown = false;
            _caughtHoldTimer = 0f;
            ResetDawn();
        }

        /// <summary>
        /// 这一夜该让奶包干什么：第一夜她只负责说话（引导），第二夜起开始手欠。
        /// 永夜暂时不接她（难度已经够乱，再加一个说不清是谁的锅的变量不划算），
        /// 自定义夜按手欠处理。传 null 给 <see cref="GameManager.SetNaibao"/> 就是这一夜没有她。
        /// </summary>
        private static NaibaoDirector CreateNaibaoForNight(int night)
        {
            return new NaibaoDirector(night <= 0, null);
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

        private Image MakeMenuCard(Transform parent, string name, Vector2 anchor, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            PlaceRect((RectTransform)go.transform, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            Image image = go.GetComponent<Image>();
            image.sprite = MakeRoundedSprite();
            image.type = Image.Type.Sliced;
            image.color = new Color(0.085f, 0.11f, 0.16f, 1f);
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.42f, 0.49f, 0.61f, 0.55f);
            outline.effectDistance = new Vector2(1f, -1f);
            return image;
        }

        private static void SetMenuButtonColors(Button button)
        {
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.45f, 1.45f, 1.45f);
            colors.selectedColor = new Color(1.3f, 1.3f, 1.3f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            colors.disabledColor = new Color(0.65f, 0.65f, 0.65f, 1f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
        }

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

            image.type = Image.Type.Sliced;
            SetMenuButtonColors(button);

            var labelText = MakeLabel(go.transform, label, 32, new Color(1f, 0.96f, 0.85f),
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
