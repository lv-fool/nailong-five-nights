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
    /// 可玩原型。操作方式与界面布置对齐 FNAF 1 官方：
    /// 纯鼠标操作、按钮贴在两侧墙上、电量与使用等级在左下角、时钟在右上角。
    ///
    /// 操作：
    ///   · 鼠标移到屏幕左 / 右 1/3 → 平滑转头
    ///   · 鼠标移到屏幕底部边缘 → 升起 / 放下监控（hover 触发，不是点击）
    ///   · 点击两侧墙上的 DOOR / LIGHT → 开关门与灯（门在上、灯在下）
    ///   · 监控升起后，点右下角小地图上的 CAM 框 → 切机位（11 个机位都能点）
    ///
    /// 调试快捷键（官方没有，留着方便调参）：
    ///   · 1~9 / 0 / - 直接跳机位　· R 重开一夜　· [ / ] 换夜　· F1 显示 / 隐藏调试面板
    /// </summary>
    public partial class PrototypeScene : MonoBehaviour
    {
        /// <summary>立绘姿态。缺哪张按兜底链退，见 <see cref="CharacterArt.Get"/>。</summary>
        public enum CharacterPose
        {
            /// <summary>静态站立（门口 / 监控）。</summary>
            Idle = 0,

            /// <summary>直视镜头（逼近时）。</summary>
            Stare = 1,

            /// <summary>从门外探出头（噜噜被灯照到时）。</summary>
            Peek = 2,

            /// <summary>跳杀特写。</summary>
            Jumpscare = 3,

            /// <summary>受惊（被灯照到 / 被盯着）。</summary>
            Startled = 4,

            /// <summary>怒视。</summary>
            Angry = 5
        }

        /// <summary>一个角色的立绘。缺哪张哪张留空，运行时退回色块占位。</summary>
        [System.Serializable]
        public class CharacterArt
        {
            public EnemyId Id;
            public Sprite Idle;
            public Sprite Stare;

            /// <summary>从门外探头那张（噜噜专用）。没给就退回 Stare。</summary>
            public Sprite Peek;

            /// <summary>受惊那张（噜噜专用）。没给就退回 Stare。</summary>
            public Sprite Startled;

            /// <summary>怒视那张（噜噜专用）。没给就退回 Stare。</summary>
            public Sprite Angry;

            public Sprite Jumpscare;

            /// <summary>断电后「收电费」用的那张。没给就退回 Jumpscare。</summary>
            public Sprite Blackout;

            /// <summary>取某个姿态的图；缺了就沿兜底链往下退，全缺返回 null（交给色块）。</summary>
            public Sprite Get(CharacterPose pose)
            {
                switch (pose)
                {
                    case CharacterPose.Stare:
                        return Fallback(Stare);

                    case CharacterPose.Peek:
                        return Fallback(Peek);

                    case CharacterPose.Startled:
                        return Fallback(Startled);

                    case CharacterPose.Angry:
                        return Fallback(Angry);

                    case CharacterPose.Jumpscare:
                        return Fallback(Jumpscare);

                    default:
                        return Idle;
                }
            }

            /// <summary>有就用；没有就退 Stare，再没有退 Idle。</summary>
            private Sprite Fallback(Sprite sprite)
            {
                if (sprite != null)
                {
                    return sprite;
                }

                return Stare != null ? Stare : Idle;
            }
        }

        /// <summary>监控画面里立绘的高度。</summary>
        /// <summary>
        /// 监控画面里叠的敌人立绘高度（像素）。
        /// 别设太大 —— 660 时占画面 71%，整个人糊在镜头上把房间全挡了；
        /// 380 约占 41%，跟 FNAF 里敌人在监控画面中的比例接近。
        /// </summary>
        private const float MonitorCharacterHeight = 380f;

        // 3D 世界物体的排序层。门板在敌人后面一层（敌人站门里，门关上门板挡住它）；
        // 都比相机近，不会和全景内壁交叠闪烁（Sprite 渲染走 sortingOrder，同相机深度）。
        private const int SpriteSortDoor = -1;
        private const int SpriteSortEnemy = 0;

        /// <summary>正门口（前墙门洞 z=3.0），噜噜站在那儿。</summary>
        private static readonly Vector3 FrontDoorCenter = new Vector3(0f, 1.1f, 3.0f);

        /// <summary>
        /// 画布的逻辑高度。跳杀立绘等满屏 UI 按它铺。**不能当成 1080 用** ——
        /// CanvasScaler 按宽高折中缩放，宽屏下画布的逻辑高度会被压到 1080 以下。
        /// </summary>
        private float CanvasHeight
        {
            get
            {
                if (_canvas == null)
                {
                    return 1080f;
                }

                float height = _canvas.GetComponent<RectTransform>().rect.height;
                return height > 1f ? height : 1080f;
            }
        }

        /// <summary>
        /// 手电筒按钮中心离屏幕底边多高。按钮半高 30，所以底边在 172 - 30 = 142，
        /// 高过监控热区（0.10 × 1080 = 108）—— 免得点它的时候顺手把监控收了。
        /// </summary>
        private const float FlashlightButtonBottom = 172f;

        /// <summary>断电时奶龙立绘的高度。</summary>
        private const float BlackoutCharacterHeight = 880f;

        /// <summary>断电时奶龙贴左边多远（UI 参考分辨率下的像素）。</summary>
        private const float BlackoutAnchorX = 60f;

        /// <summary>拍点上脸亮到这个程度 —— 那一刻看得清清楚楚。</summary>
        private const float BlackoutLitAlpha = 0.96f;

        /// <summary>拍与拍之间沉下去的程度 —— 几乎看不见。</summary>
        private const float BlackoutDimAlpha = 0.05f;

        /// <summary>音乐停之后的漆黑段：只剩一个勉强能辨认的轮廓。</summary>
        private const float BlackoutFinalAlpha = 0.07f;

        /// <summary>
        /// 一拍里前这么长的一段时间是亮的，其余沉下去。
        /// 硬切（不是渐隐）—— 原版 Freddy 的脸就是这么跟着音乐闪的。
        /// </summary>
        private const float BlackoutLitFraction = 0.5f;

        /// <summary>音乐素材末尾这段衰减尾巴不算进音乐段 —— 音乐停下时画面正好黑下去。</summary>
        private const float BlackoutMusicTailTrim = 0.5f;

        /// <summary>跳杀立绘按这个宽度铺满屏幕（UI 参考分辨率）。</summary>
        private const float JumpscareWidth = 1920f;

        /// <summary>
        /// 镜头**直接跟鼠标**：鼠标在屏幕哪儿，镜头就朝哪儿。
        ///
        /// 这个值只是跟手的缓动时间（秒），用来吃掉鼠标的微小抖动，别调大 ——
        /// 大了镜头就会「拖」在鼠标后面。想完全 1:1 跟手就写 0。
        /// </summary>
        private const float TurnSmoothTime = 0.08f;

        /// <summary>
        /// 鼠标映射的「跟手区间」半宽：`pan = Clamp((x − 0.5) / K, −1, 1)`。
        ///
        /// K = 0.20 → 中间 40% 屏宽按比例跟手，**两边各 30% 是饱和区**（推到那儿视角
        /// 就不再变）。饱和区是必需的：墙上的按钮跟着墙走，鼠标要靠「推过去」才能压住它，
        /// 而绝对线性映射下墙上的点滑得比鼠标快（见 <see cref="BuildWallButtons"/>），
        /// 永远追不上。有了饱和区，饱和区内 dx/dmouse = 0，按钮才稳。
        ///
        /// ⚠️ **别再调小**：按钮满转时的屏幕 x 随窗口宽高比变 —— 窗口越宽水平视场越大、
        /// 按钮越往中间跑（16:9 时在 0.906，2.4:1 时只剩 0.798）。K 必须小到让**最靠中间
        /// 的那个位置也落在饱和区里**（0.5 + 0.20 = 0.70）。K = 0.20 能覆盖到约 3.5:1 的窗口。
        /// </summary>
        private const float PanSaturationK = 0.20f;

        /// <summary>
        /// 判「玩家在看哪一边」的 |_pan| 阈值。只有噜噜的手电筒在用（`IsShiningAtFront`）。
        /// 按**角度**定：0.64 × 满转 70° ≈ 45°，和满转 90° 时代一样，手感不变。
        /// </summary>
        private const float ViewSideThreshold = 0.64f;

        /// <summary>鼠标进入屏幕底部这个比例就切换监控。</summary>
        private const float MonitorZone = 0.10f;

        /// <summary>
        /// 监控机位表 —— 编号、房间名、前后顺序全照 FNAF 1 官方那张平面图。
        /// **数组下标就是机位号**；加机位只往这里加一行，别处不用动。
        /// </summary>
        private static readonly string[] CameraNames =
        {
            "CAM 1A    SHOW STAGE",       // 0  舞台
            "CAM 1B    DINING AREA",      // 1  餐厅（大厅）
            "CAM 1C    PIRATE COVE",      // 2  海盗湾
            "CAM 2A    WEST HALL",        // 3  西走廊（左走廊）
            "CAM 2B    W HALL CORNER",    // 4  西走廊转角
            "CAM 3     SUPPLY CLOSET",    // 5  储藏室
            "CAM 4A    EAST HALL",        // 6  东走廊（右走廊）
            "CAM 4B    E HALL CORNER",    // 7  东走廊转角
            "CAM 5     BACKSTAGE",        // 8  后台
            "CAM 6     KITCHEN",          // 9  厨房
            "CAM 7     RESTROOMS"         // 10 厕所
        };

        /// <summary>
        /// 厨房（CAM 6）的机位号 —— 官方这个机位**只有声音、没有画面**，
        /// 照官方处理：切到它就一片黑、只留机位名，全靠听。
        /// 奶龙也不去这里（见 <c>EnemyDirector._nailongSkipCameras</c>）。
        /// </summary>
        private const int KitchenCameraIndex = 9;

        // ---------------------------------------------------------------
        // 小地图：整张 FNAF 1 官方平面图 —— 舞台 / 餐厅 / 海盗湾 / 西走廊 /
        // 储藏室 / 东走廊 / 后台 / 厨房 / 厕所 / 办公室，位置照官方那张图。
        // 图上那 11 个 CAM 框全部可点，点一下切到对应机位。
        //
        // 房间坐标直接按官方图的原始像素写（967×791），画的时候统一缩放到面板 ——
        // 以后照图微调只改数字，不用自己换算。
        // ---------------------------------------------------------------

        /// <summary>只取官方图上有用的那块（四周留白不算）。</summary>
        private const float MapViewX0 = 28f;
        private const float MapViewY0 = 14f;
        private const float MapViewX1 = 934f;
        private const float MapViewY1 = 792f;

        /// <summary>地图整体缩放倍数（1 = 原始大小）。</summary>
        private const float MapPanelScale = 1.2f;

        /// <summary>地图面板的宽度；高度按官方图比例算。</summary>
        private const float MapPanelWidth = 420f * MapPanelScale;
        private const float MapPanelHeight =
            MapPanelWidth * (MapViewY1 - MapViewY0) / (MapViewX1 - MapViewX0);

        /// <summary>面板离屏幕右边留出的边距。</summary>
        private const float MapPanelMargin = 26f;

        /// <summary>
        /// 面板离屏幕**底边**留出的距离。
        /// 必须大于 <see cref="MonitorZone"/> × 参考高度（0.10 × 1080 = 108），
        /// 否则点地图下半截（CAM 2B / 4B / YOU 那几行）时鼠标会落进
        /// 「碰到底边就收监控」的热区，监控会莫名其妙关掉。
        /// </summary>
        private const float MapPanelBottomOffset = 140f;

        /// <summary>房间轮廓的线宽。</summary>
        private const float MapBorderThickness = 2f * MapPanelScale;

        /// <summary>CAM 标号框的线宽。</summary>
        private const float MapButtonBorderThickness = 3f * MapPanelScale;

        /// <summary>CAM 标号框里的字号。</summary>
        private const int MapLabelFontSize = 16;

        /// <summary>平面图上的一块矩形（房间 / 走廊 / CAM 标号框）。</summary>
        private struct MapRectSpec
        {
            public string Label;
            public Vector2 Center;
            public Vector2 Size;

            /// <summary>对应的机位索引；-1 表示这不是机位，只画不点。</summary>
            public int CameraIndex;
        }

        /// <summary>
        /// 把官方图上的一个像素矩形换算成面板本地坐标。
        /// 官方图 y 向下、面板 y 向上，所以纵向翻一下。
        /// </summary>
        private static MapRectSpec MapRoom(float x0, float y0, float x1, float y1,
            string label, int cameraIndex)
        {
            float cx = (MapViewX0 + MapViewX1) * 0.5f;
            float cy = (MapViewY0 + MapViewY1) * 0.5f;
            float scale = MapPanelWidth / (MapViewX1 - MapViewX0);

            return new MapRectSpec
            {
                Label = label,
                Center = new Vector2((x0 + x1) * 0.5f - cx, -((y0 + y1) * 0.5f - cy)) * scale,
                Size = new Vector2(x1 - x0, y1 - y0) * scale,
                CameraIndex = cameraIndex
            };
        }

        /// <summary>
        /// 房间轮廓 —— 跟官方原图一样**只描边、不填色**。
        /// 坐标是照原图量下来的，所以餐厅（大厅）看着大是因为它在原图上就这么大，
        /// 但因为不填色，不会糊成一大块。
        /// </summary>
        private static readonly MapRectSpec[] MapRooms =
        {
            MapRoom(437f, 71f, 588f, 145f, null, -1),      // 舞台
            MapRoom(210f, 145f, 735f, 452f, null, -1),     // 餐厅（大厅）
            MapRoom(132f, 330f, 210f, 440f, null, -1),     // 海盗湾
            MapRoom(313f, 452f, 441f, 748f, null, -1),     // 西走廊（含转角）
            MapRoom(205f, 519f, 284f, 668f, null, -1),     // 储藏室
            MapRoom(549f, 452f, 675f, 748f, null, -1),     // 东走廊（含转角）
            MapRoom(100f, 145f, 185f, 300f, null, -1),     // 后台
            MapRoom(666f, 480f, 845f, 628f, null, -1),     // 厨房
            MapRoom(764f, 193f, 917f, 448f, null, -1),     // 厕所
            MapRoom(437f, 652f, 517f, 780f, "YOU", -1)     // 办公室
        };

        /// <summary>把各房间连起来的走廊（细条），画在房间下面。</summary>
        private static readonly MapRectSpec[] MapCorridors =
        {
            MapRoom(185f, 205f, 212f, 255f, null, -1),   // 后台 ↔ 餐厅
            MapRoom(733f, 230f, 766f, 280f, null, -1),   // 餐厅 ↔ 厕所
            MapRoom(282f, 565f, 315f, 612f, null, -1),   // 储藏室 ↔ 西走廊
            MapRoom(515f, 690f, 551f, 732f, null, -1),   // 办公室 ↔ 东走廊
            MapRoom(798f, 446f, 832f, 482f, null, -1)    // 厨房 ↔ 厕所
        };

        /// <summary>
        /// 官方原图上那 11 个 CAM 标号框 —— 位置、大小都照原图量下来的。
        /// 每一个都对应 <see cref="CameraNames"/> 里的机位，点一下切过去。
        /// </summary>
        private static readonly MapRectSpec[] MapCamButtons =
        {
            MapRoom(316f, 25f, 441f, 105f, "CAM\n1A", 0),
            MapRoom(272f, 144f, 397f, 228f, "CAM\n1B", 1),
            MapRoom(40f, 205f, 164f, 287f, "CAM\n5", 8),
            MapRoom(782f, 208f, 908f, 290f, "CAM\n7", 10),
            MapRoom(201f, 318f, 327f, 400f, "CAM\n1C", 2),
            MapRoom(132f, 533f, 257f, 614f, "CAM\n3", 5),
            MapRoom(313f, 572f, 441f, 655f, "CAM\n2A", 3),
            MapRoom(313f, 661f, 441f, 743f, "CAM\n2B", 4),
            MapRoom(549f, 575f, 675f, 657f, "CAM\n4A", 6),
            MapRoom(549f, 663f, 675f, 745f, "CAM\n4B", 7),
            MapRoom(762f, 497f, 888f, 578f, "CAM\n6", 9)
        };

        // 没有素材时的兜底色（监控画面缺帧时退回黑）
        private static readonly Color MonitorFallback = new Color(0.05f, 0.06f, 0.07f);

        // 按钮配色沿用官方：关门是红按钮，开灯是绿按钮
        private static readonly Color DoorOn = new Color(0.78f, 0.16f, 0.16f);
        private static readonly Color DoorOff = new Color(0.40f, 0.11f, 0.11f);
        private static readonly Color LightOn = new Color(0.32f, 0.72f, 0.30f);
        private static readonly Color LightOff = new Color(0.13f, 0.32f, 0.14f);

        // 手电筒按钮：亮着是暖黄，松开是灰
        private static readonly Color FlashlightOn = new Color(0.92f, 0.80f, 0.36f);
        private static readonly Color FlashlightOff = new Color(0.24f, 0.23f, 0.20f);

        /// <summary>手电筒照在正前方时，画面整体提亮的强度。</summary>
        private const float FlashlightGlowAlpha = 0.35f;

        // 按钮底图：填色 + 一圈接近纯白的描边。Image.color 一乘，描边就比底色亮一档
        private static readonly Color ButtonFace = new Color(0.76f, 0.76f, 0.79f);
        private static readonly Color ButtonEdge = new Color(1f, 1f, 1f);

        // 使用等级五格条：1~2 绿、3 黄、4~5 红
        private static readonly Color UsageGreen = new Color(0.35f, 0.78f, 0.32f);
        private static readonly Color UsageAmber = new Color(0.92f, 0.72f, 0.20f);
        private static readonly Color UsageRed = new Color(0.90f, 0.26f, 0.22f);
        private static readonly Color UsageOff = new Color(0.20f, 0.20f, 0.22f);

        // 小地图 —— 照官方原图：房间只描边不填色，CAM 标号是深色小方块
        private static readonly Color MapPanelFill = new Color(0.055f, 0.065f, 0.075f, 1f);
        private static readonly Color MapRoomFill = new Color(0.075f, 0.085f, 0.095f, 1f);
        private static readonly Color MapRoomBorder = new Color(0.42f, 0.47f, 0.44f, 0.90f);
        private static readonly Color MapCorridorColor = new Color(0.34f, 0.38f, 0.36f, 0.85f);
        private static readonly Color MapButtonFill = new Color(0.11f, 0.12f, 0.14f, 1f);
        private static readonly Color MapButtonBorder = new Color(0.34f, 0.38f, 0.35f, 0.95f);
        private static readonly Color MapButtonText = new Color(0.52f, 0.57f, 0.52f, 1f);

        // 我们自己的三个机位
        private static readonly Color CamBoxOn = new Color(0.22f, 0.60f, 0.34f, 1f);
        private static readonly Color CamBoxOff = new Color(0.14f, 0.16f, 0.18f, 1f);
        private static readonly Color CamBoxBorder = new Color(0.50f, 0.60f, 0.52f, 0.95f);
        private static readonly Color CamBoxText = new Color(0.86f, 0.93f, 0.86f, 1f);

        [Header("素材（留空则用色块占位）")]
        /// <summary>门板正视贴图 —— 3D 世界里的门（吊在门洞里，不再是 UI 叠层）。</summary>
        [SerializeField] private Sprite _doorFaceLeft;
        [SerializeField] private Sprite _doorFaceRight;

        [SerializeField] private Sprite[] _monitorFrames;

        [Header("角色立绘（缺哪张哪张退回色块）")]
        [SerializeField] private CharacterArt[] _characters;

        private Canvas _canvas;

        /// <summary>3D 办公室（全景条 + 旋转相机）。背景是它，不是背景帧序列。</summary>
        private OfficeRoom _room;

        // 3D 世界物体：门板 + 门口敌人。挂在场景根（和 OfficeRoom 平级），
        // 由相机透视 —— 不再是 UI 叠层，也就没有「转场/滑入淡入」那套特效。
        private SpriteRenderer _doorLeft3D;
        private SpriteRenderer _doorRight3D;
        private SpriteRenderer _enemySideLeft;
        private SpriteRenderer _enemySideRight;
        private SpriteRenderer _enemyFront;

        // 门洞外的灯光特效：灯亮时门口亮起一团光（Additive 叠加），灯灭熄灭。
        private SpriteRenderer _doorLightLeft;
        private SpriteRenderer _doorLightRight;

        /// <summary>
        /// 断电时盖全屏的黑幕。平时整层透明，露出后面的 3D 房间（见 OfficeRoom）。
        /// </summary>
        private Image _bgBase;

        private CanvasGroup _leftWallGroup;
        private CanvasGroup _rightWallGroup;
        private Image _doorLeftButton;
        private Image _doorRightButton;
        private Image _lightLeftButton;
        private Image _lightRightButton;

        // LIGHT 按钮的光晕 —— 灯亮时绿光呼吸，灯灭熄灭。
        private Image _lightLeftGlow;
        private Image _lightRightGlow;
        private CanvasGroup _flashlightGroup;
        private Image _flashlightButton;
        private HoldButton _flashlightHold;
        private Image _flashlightGlow;
        private float _flashlightGlowAlpha;
        private Image _monitorOverlay;
        /// <summary>
        /// 监控画面里叠的敌人立绘槽位（最多三个）。
        /// 同一机位可能站着不止一个（比如舞台上的袋鼠 + 牛来），
        /// 所以用数组横排，而不是单个 Image。
        /// </summary>
        private Image[] _monitorCharacters;
        private CanvasGroup _monitorMapGroup;
        private Text _monitorLabel;
        private Image[] _camBoxes;
        private Image _monitorHandle;
        private Text _clockText;
        private Text _powerText;
        private Image[] _usageBars;

        /// <summary>左下角「电量 + Usage」整组。断电后收起来，跟原版一样只剩黑暗。</summary>
        private CanvasGroup _powerGroup;

        private CanvasGroup _debugGroup;
        private Text _debugText;
        private Image _jumpscareOverlay;
        private Image _jumpscareImage;
        private Text _jumpscareText;
        private Image _blackoutCharacter;
        private Font _font;
        private Sprite _solidSprite;
        private Sprite _buttonSprite;

        private EnemyDirector _enemyDirector;
        private AudioManager _audio;
        private bool _hasJumpscare;
        private EnemyId _jumpscareEnemy;
        private float _jumpscareTime;
        private bool _wasPowerOut;
        private float _footstepTimer;
        private bool _musicPlaying;
        private bool _debugVisible;

        /// <summary>转头进度：-1 全左、0 正前方、+1 全右。</summary>
        private float _pan;
        private float _panTarget;
        private float _panVelocity;
        private bool _wasInMonitorZone;

        private void Awake()
        {
            EnsureGameManager();
            EnsureEventSystem();
            EnsureEnemyDirector();
            EnsureAudioManager();
            EnsureRoom();

            _font = Resources.Load<Font>("Fonts/simhei");
            if (_font == null)
            {
                // 兜底：内置拉丁字体。中文会显示成方块/空白（功能不受影响）。
                // 想显示中文，放一个中文字体到 Assets/Resources/Fonts/ 下，命名 simhei。
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            if (_font == null)
            {
                Debug.LogWarning("[奶龙] 没取到任何字体，UI 文字不会显示（功能不受影响）。");
            }

            SyncPowerOutMusicLength();

            BuildUi();

            // 开始界面 / 关卡选择（见 PrototypeScene.Menu.cs）
            BuildMenu();
        }

        /// <summary>断电演出的音乐段有多长，由音频素材决定 —— 换素材不用改代码。</summary>
        private void SyncPowerOutMusicLength()
        {
            if (_audio == null || _enemyDirector == null)
            {
                return;
            }

            float length = _audio.PowerOutMusicLength;
            if (length <= 0f)
            {
                return;
            }

            // 末尾留一点衰减尾巴：音乐停下的那一刻，画面正好黑下去
            _enemyDirector.SetBlackoutMusicSeconds(length - BlackoutMusicTailTrim);
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

            // 菜单开着 / 出现结算面板时，冻住这一夜，等玩家选择
            HandleMenu(gm);
            if (_menuOpen)
            {
                return;
            }

            HandleDebugKeys();

            if (Input.GetKeyDown(KeyCode.R))
            {
                gm.Restart();
                ClearJumpscare();
            }

            HandleNightKeys(gm);
            HandleCameraKeys(gm);

            UpdateView(gm);
            HandleMonitorHotspot(gm);
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

        /// <summary>
        /// 3D 办公室：全景条 + 绕中轴旋转的相机。背景从此由相机真的转出来，
        /// 不再靠背景帧在两帧之间交叉淡化（那看着是转场，不是转头）。
        /// </summary>
        private void EnsureRoom()
        {
            var existing = FindObjectOfType<OfficeRoom>();
            _room = existing != null
                ? existing
                : new GameObject("OfficeRoom").AddComponent<OfficeRoom>();
        }

        // ---------------------------------------------------------------
        // 视角：镜头直接跟着鼠标走
        // ---------------------------------------------------------------

        private void UpdateView(GameManager gm)
        {
            if (CanOperate(gm))
            {
                float width = Screen.width;
                float x = width > 0f ? Input.mousePosition.x / width : 0.5f;

                // 鼠标位置映射成转头量：正中 0，推到 |x−0.5| ≥ PanSaturationK 就满转。
                // 中间那段按比例跟手，两边各留一段**饱和区**（推到那儿视角不再变）——
                // 贴墙的按钮只有落在这段里，鼠标才压得住、点得到（见 BuildWallButtons）。
                _panTarget = Mathf.Clamp((x - 0.5f) / PanSaturationK, -1f, 1f);
            }
            else
            {
                // 断电 / 天亮 / 被抓 —— 视角锁回正面，只能眼睁睁看着
                _panTarget = 0f;
            }

            // 一点缓动：跟手，但不让鼠标的微小抖动直接抖到画面上
            _pan = Mathf.SmoothDamp(_pan, _panTarget, ref _panVelocity, TurnSmoothTime);

            // 供敌人 AI 判断"玩家在看哪边"（只有噜噜的手电筒在用）。
            // 阈值按**角度**定：0.64 × 70° ≈ 45°，和满转 90° 时代一样 —— 手电筒手感不变。
            ViewSide view;
            if (_pan <= -ViewSideThreshold)
            {
                view = ViewSide.Left;
            }
            else if (_pan >= ViewSideThreshold)
            {
                view = ViewSide.Right;
            }
            else
            {
                view = ViewSide.Front;
            }

            gm.SetView(view);
        }

        /// <summary>鼠标碰到屏幕底部边缘就升 / 放监控（进入边缘算一次，不是每帧）。</summary>
        private void HandleMonitorHotspot(GameManager gm)
        {
            float height = Screen.height;
            bool inZone = height > 0f && Input.mousePosition.y / height <= MonitorZone;

            if (inZone && !_wasInMonitorZone && CanOperate(gm))
            {
                ToggleMonitor(gm);
            }

            _wasInMonitorZone = inZone;
        }

        private static bool CanOperate(GameManager gm)
        {
            return !gm.HasPowerOut && !gm.HasSurvived && !gm.HasBeenCaught;
        }

        private void ToggleMonitor(GameManager gm)
        {
            bool before = gm.MonitorUp;
            gm.ToggleMonitor();

            if (_audio != null && gm.MonitorUp != before)
            {
                _audio.PlayCameraSwitch();
            }
        }

        private void HandleCameraKeys(GameManager gm)
        {
            if (!gm.MonitorUp)
            {
                return;
            }

            // 调试用：数字键 1~9 跳机位 0~8，0 跳第 10 个，- 跳第 11 个
            int digitCount = CameraNames.Length < 9 ? CameraNames.Length : 9;
            for (int i = 0; i < digitCount; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    SelectCamera(gm, i);
                }
            }

            if (CameraNames.Length > 9 && Input.GetKeyDown(KeyCode.Alpha0))
            {
                SelectCamera(gm, 9);
            }

            if (CameraNames.Length > 10 && Input.GetKeyDown(KeyCode.Minus))
            {
                SelectCamera(gm, 10);
            }
        }

        private void SelectCamera(GameManager gm, int index)
        {
            int before = gm.CurrentCameraIndex;
            gm.SetCamera(index);

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

        private void HandleDebugKeys()
        {
            if (!Input.GetKeyDown(KeyCode.F1))
            {
                return;
            }

            _debugVisible = !_debugVisible;
            _debugGroup.alpha = _debugVisible ? 1f : 0f;
        }

        // ---------------------------------------------------------------
        // 画面
        // ---------------------------------------------------------------

        private void UpdateVisuals(GameManager gm)
        {
            // 跳杀 / 断电时玩家已经不能操作，界面一并收起来
            bool locked = gm.HasPowerOut || _hasJumpscare;

            // 背景、门片、门口立绘、断电演出
            if (gm.HasPowerOut)
            {
                SetBlackout();
                HideDoors();
                HideSideCharacters();
                HideFrontCharacter();

                // 断电 → 奶龙来收电费：黑暗里一闪一闪
                if (_hasJumpscare)
                {
                    _blackoutCharacter.gameObject.SetActive(false);
                }
                else
                {
                    ApplyBlackoutCharacter();
                }
            }
            else
            {
                _blackoutCharacter.gameObject.SetActive(false);
                ApplyRoomView();

                if (_hasJumpscare)
                {
                    HideWorldObjects();
                }
                else
                {
                    ApplyWorldObjects(gm);
                }
            }

            // 监控层：断电后整套掉线，别挡着断电演出
            bool monitorUp = gm.MonitorUp && !_hasJumpscare && !gm.HasPowerOut;
            if (monitorUp)
            {
                ApplyMonitorOverlay(gm);
            }

            _monitorOverlay.gameObject.SetActive(monitorUp);
            _monitorMapGroup.alpha = monitorUp ? 1f : 0f;
            _monitorMapGroup.blocksRaycasts = monitorUp;

            // 两侧墙上的按钮：贴在墙上 —— 每帧按世界点投影摆过去（看监控 / 断电时收起）。
            // 视角没转过去时按钮本来就在画面外，所以不用额外做淡入。
            ApplyWallButtons(monitorUp || locked);

            ApplyFlashlight(gm, monitorUp, locked);

            UpdateWallButtonColors(gm);

            // 底部监控把手：断电或被抓之后不再显示
            _monitorHandle.gameObject.SetActive(!gm.HasPowerOut && !gm.HasBeenCaught);
        }

        private void SetBlackout()
        {
            SetLayer(_bgBase, null, Color.black, 1f);
        }

        /// <summary>
        /// 正常视角：把 _pan 交给 3D 房间转相机。黑幕那层这时要全透明，别盖住房。
        /// </summary>
        private void ApplyRoomView()
        {
            SetLayer(_bgBase, null, Color.black, 0f);

            if (_room != null)
            {
                _room.SetPan(_pan);
            }
        }

        private void SetLayer(Image image, Sprite sprite, Color fallback, float alpha)
        {
            image.sprite = sprite != null ? sprite : _solidSprite;

            Color color = sprite != null ? Color.white : fallback;
            color.a = alpha;
            image.color = color;
            image.enabled = alpha > 0.001f;
        }

        private void HideDoors()
        {
            if (_doorLeft3D != null)
            {
                _doorLeft3D.gameObject.SetActive(false);
            }

            if (_doorRight3D != null)
            {
                _doorRight3D.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 门是 3D 世界物体：关上门就把门板 SpriteRenderer 点亮（立在门洞里），
        /// 开门就熄灭。**没有转场** —— 门就在那儿，开关是即时的（叠层那套
        /// 逐帧门片 / 交叉淡化已经删掉）。
        /// </summary>
        private void ApplyDoors(GameManager gm)
        {
            if (_doorLeft3D == null || _doorRight3D == null)
            {
                return;
            }

            // 门开着：整层看不见（门洞露出来，走廊在那头）
            _doorLeft3D.gameObject.SetActive(gm.LeftDoorClosed && _doorFaceLeft != null);
            _doorRight3D.gameObject.SetActive(gm.RightDoorClosed && _doorFaceRight != null);
        }

        // ---------------------------------------------------------------
        // 敌人立绘（3D 世界物体）
        // ---------------------------------------------------------------

        /// <summary>
        /// 每帧驱动门板 + 3 个站门口的敌人（世界 SpriteRenderer）。
        /// 门关 → 门板立起来；门开 + 灯亮 → 对应门口的敌人现身。没有转场。
        /// </summary>
        private void ApplyWorldObjects(GameManager gm)
        {
            ApplyDoors(gm);

            if (_enemySideLeft == null || _enemySideRight == null || _enemyFront == null)
            {
                return;
            }

            EnemyId leftId, rightId;
            bool leftPresent = TryGetEnemyAtSide(gm, Side.Left, out leftId);
            bool rightPresent = TryGetEnemyAtSide(gm, Side.Right, out rightId);

            ShowWorldCharacter(_enemySideLeft, leftPresent, leftId, CharacterPose.Idle, Side.Left);
            ShowWorldCharacter(_enemySideRight, rightPresent, rightId, CharacterPose.Idle, Side.Right);

            ApplyFrontCharacter3D(gm);

            // 门洞外的灯：灯亮 → 门口亮起一团光（呼吸），灯灭 → 熄灭
            float breathe = 0.85f + Mathf.Sin(Time.time * 2.4f) * 0.15f;
            ApplyDoorLight(_doorLightLeft, gm.LeftLightOn, breathe);
            ApplyDoorLight(_doorLightRight, gm.RightLightOn, breathe);
        }

        private static void ApplyDoorLight(SpriteRenderer light, bool on, float breathe)
        {
            if (light == null)
            {
                return;
            }

            light.gameObject.SetActive(on);

            Color color = light.color;
            color.a = on ? 0.55f * breathe : 0f;
            light.color = color;

            // ⚠️ **不要跟着 breathe 改 scale** —— 光斑要始终正好铺满门框，
            // 缩放会让它缩进门洞（露出暗边）或溢到门框外的墙上。
            // 呼吸只作用在亮度上。
        }

        /// <summary>
        /// 把立绘贴到世界 SpriteRenderer 上。尺寸按**真实米数**算：
        /// 门口站的人大约 1.8m 高（门洞 2.05m），立绘像素按各自 PNG 尺寸等比换算成
        /// 米制缩放（PPU 100 的基准），保证所有角色站门口高度一致、透视由相机算。
        /// </summary>
        private void ShowWorldCharacter(SpriteRenderer sr, bool present, EnemyId id,
            CharacterPose pose, Side side)
        {
            if (sr == null)
            {
                return;
            }

            if (!present)
            {
                sr.gameObject.SetActive(false);
                return;
            }

            var art = FindCharacter(id);
            Sprite sprite = art != null ? art.Get(pose) : null;
            if (sprite == null)
            {
                sr.gameObject.SetActive(false);
                return;
            }

            sr.gameObject.SetActive(true);
            sr.sprite = sprite;

            // 高度定 1.8m（人站门洞里，头顶低于门洞顶 2.05m）。
            // 按「目标米高 / (rect.height / PPU)」算缩放 —— Simple 模式下
            // 世界尺寸 = 像素 ÷ PPU × scale，所以 scale = 目标米高 ÷ (像素 ÷ PPU)。
            const float TargetHeightMeters = 1.8f;
            float worldH = sprite.rect.height / sprite.pixelsPerUnit;
            float s = worldH > 0.001f ? TargetHeightMeters / worldH : 1f;
            sr.drawMode = SpriteDrawMode.Simple;
            sr.transform.localScale = new Vector3(side == Side.Left ? -s : s, s, 1f);
        }

        private void HideWorldObjects()
        {
            HideDoors();

            if (_enemySideLeft != null)
            {
                _enemySideLeft.gameObject.SetActive(false);
            }

            if (_enemySideRight != null)
            {
                _enemySideRight.gameObject.SetActive(false);
            }

            if (_enemyFront != null)
            {
                _enemyFront.gameObject.SetActive(false);
            }

            if (_doorLightLeft != null)
            {
                _doorLightLeft.gameObject.SetActive(false);
            }

            if (_doorLightRight != null)
            {
                _doorLightRight.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 门口站着谁。门类敌人要**门没关 + 那一侧的灯亮着** ——
        /// 照 FNAF 1：不闪灯就看不见门外有没有人。
        /// </summary>
        private bool TryGetEnemyAtSide(GameManager gm, Side side, out EnemyId id)
        {
            id = EnemyId.Niulai;

            if (_enemyDirector == null)
            {
                return false;
            }

            bool closed = side == Side.Left ? gm.LeftDoorClosed : gm.RightDoorClosed;
            bool lightOn = side == Side.Left ? gm.LeftLightOn : gm.RightLightOn;

            if (closed || !lightOn)
            {
                return false;
            }

            foreach (var brain in _enemyDirector.Brains)
            {
                var door = brain as DoorEnemyBrain;
                if (door == null || door.Side != side || door.IsAttacking)
                {
                    continue;
                }

                // 站到门口 = Stage 到顶（门类敌人配了机位路径，MaxStage 不再是固定的 2）
                if (door.Stage >= door.MaxStage)
                {
                    id = door.Id;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 正前方那个噜噜 —— 3D 世界物体，站在正门口（世界坐标 z=3.0，前墙门洞）。
        /// 逼近时站姿，堵到门口换正对镜头；**堵到门口之后**被手电筒照着才换成受惊。
        /// 走世界坐标后「转头看不见它」是相机透视自然的结果，不用再叠 UI 淡出。
        /// </summary>
        private void ApplyFrontCharacter3D(GameManager gm)
        {
            var lulu = _enemyDirector != null
                ? _enemyDirector.GetBrain(EnemyId.Lulu) as LuluBrain
                : null;

            if (_enemyFront == null)
            {
                return;
            }

            if (lulu == null || lulu.IsAttacking || lulu.Stage < 1)
            {
                _enemyFront.gameObject.SetActive(false);
                return;
            }

            CharacterPose pose;

            // 正被手电筒照着 → 慌。
            // ⚠️ **只算它已经堵到正门口的时候** —— 还在逼近（侧身那张）时照它没用，
            // 立绘当然也不该有反应，不然玩家会以为「闪了但它不走」。
            // 判定跟 LuluBrain 那边保持一致：手电筒亮着 + 视角在正前方 + 没在看监控。
            bool atDoor = lulu.Stage >= lulu.MaxStage;
            bool shiningAtIt = atDoor && gm.FlashlightOn && !gm.MonitorUp
                && gm.CurrentView == ViewSide.Front;

            if (shiningAtIt)
            {
                pose = CharacterPose.Startled;
            }
            else if (atDoor)
            {
                // 堵到正门口 → 正对镜头
                pose = CharacterPose.Stare;
            }
            else
            {
                // 还在逼近 → 站姿
                pose = CharacterPose.Idle;
            }

            // 噜噜站正门口（前墙门洞 z=3.0）。素材朝左画，正前方直接面朝玩家，不用镜像。
            ShowWorldCharacter(_enemyFront, true, EnemyId.Lulu, pose, Side.Right);
        }

        private void HideFrontCharacter()
        {
            if (_enemyFront != null)
            {
                _enemyFront.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 手电筒：按钮跟着监控收起来；光只在「亮着 + 朝正前方」时打出来。
        /// 顺带兜底松手 —— 抬起事件被系统吞掉时（切窗口等）按钮会粘住。
        /// </summary>
        private void ApplyFlashlight(GameManager gm, bool monitorUp, bool locked)
        {
            bool usable = !monitorUp && !locked;

            _flashlightGroup.alpha = usable ? 1f : 0f;
            _flashlightGroup.blocksRaycasts = usable;

            if (_flashlightHold != null && _flashlightHold.IsHeld && !Input.GetMouseButton(0))
            {
                _flashlightHold.Release();
            }

            bool shiningFront = gm.FlashlightOn && usable && gm.CurrentView == ViewSide.Front;
            float target = shiningFront ? FlashlightGlowAlpha : 0f;

            _flashlightGlowAlpha = Mathf.MoveTowards(_flashlightGlowAlpha, target,
                Time.deltaTime * 5f);

            Color glow = _flashlightGlow.color;
            glow.a = _flashlightGlowAlpha;
            _flashlightGlow.color = glow;
            _flashlightGlow.enabled = _flashlightGlowAlpha > 0.001f;

            _flashlightButton.color = gm.FlashlightOn ? FlashlightOn : FlashlightOff;
        }

        private void OnFlashlightHold(bool held)
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            gm.SetFlashlight(held);

            if (_audio != null && gm.FlashlightOn == held)
            {
                _audio.PlayLight(held);
            }
        }

        private void HideSideCharacters()
        {
            if (_enemySideLeft != null)
            {
                _enemySideLeft.gameObject.SetActive(false);
            }

            if (_enemySideRight != null)
            {
                _enemySideRight.gameObject.SetActive(false);
            }

            HideMonitorCharacters();
        }

        /// <summary>把所有监控角色槽位收起来。</summary>
        private void HideMonitorCharacters()
        {
            if (_monitorCharacters == null)
            {
                return;
            }

            foreach (var img in _monitorCharacters)
            {
                if (img != null)
                {
                    img.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 断电演出：奶龙在黑暗里现形，脑袋一闪一闪。
        /// 亮度在「几乎全黑」和「看清它」之间**硬切**（方波，不是渐隐）—— 原版就是这么闪的；
        /// 越接近动手闪得越快，逼玩家盯着天亮。
        /// </summary>
        private void ApplyBlackoutCharacter()
        {
            var blackout = _enemyDirector != null ? _enemyDirector.Blackout : null;

            // 静默段：它还没来，画面就是纯黑
            if (blackout == null || blackout.Phase == BlackoutPhase.Silence)
            {
                _blackoutCharacter.gameObject.SetActive(false);
                return;
            }

            var art = FindCharacter(EnemyId.Nailong);
            Sprite sprite = art != null
                ? (art.Blackout != null ? art.Blackout : art.Jumpscare)
                : null;

            if (sprite == null)
            {
                _blackoutCharacter.gameObject.SetActive(false);
                return;
            }

            _blackoutCharacter.gameObject.SetActive(true);
            _blackoutCharacter.preserveAspect = true;
            _blackoutCharacter.sprite = sprite;
            FitHeight(_blackoutCharacter.rectTransform, sprite, BlackoutCharacterHeight);

            float alpha;
            float shake;

            if (blackout.Phase == BlackoutPhase.Music)
            {
                // 跟着音乐盒的拍子亮：拍点前半段照亮它，后半段沉回黑暗
                bool lit = blackout.BeatPhase < BlackoutLitFraction;
                alpha = lit ? BlackoutLitAlpha : BlackoutDimAlpha;
                shake = lit ? 5f : 2f;
            }
            else
            {
                // 音乐停了 —— 彻底黑掉，只剩一个勉强能辨认的轮廓
                alpha = BlackoutFinalAlpha;
                shake = 1.5f;
            }

            _blackoutCharacter.color = new Color(1f, 1f, 1f, alpha);
            _blackoutCharacter.rectTransform.anchoredPosition = new Vector2(
                BlackoutAnchorX + Mathf.Sin(Time.time * 29f) * shake,
                Mathf.Cos(Time.time * 23f) * shake);
        }

        /// <summary>按目标高度显示某个角色的立绘；没素材就退回该角色的代表色剪影。</summary>
        private void ShowCharacter(Image image, EnemyId id, CharacterPose pose, float height,
            float alpha = 1f)
        {
            var art = FindCharacter(id);
            Sprite sprite = art != null ? art.Get(pose) : null;

            image.gameObject.SetActive(true);

            if (sprite != null)
            {
                image.preserveAspect = true;
                image.sprite = sprite;
                image.color = new Color(1f, 1f, 1f, alpha);
                FitHeight(image.rectTransform, sprite, height);
            }
            else
            {
                // 色块剪影是竖长条，不能按正方形等比缩放
                Color tint = EnemyTint(id);
                tint.a *= alpha;

                image.preserveAspect = false;
                image.sprite = _solidSprite;
                image.color = tint;
                image.rectTransform.sizeDelta = new Vector2(height * 0.46f, height);
            }
        }

        private CharacterArt FindCharacter(EnemyId id)
        {
            if (_characters == null)
            {
                return null;
            }

            foreach (var art in _characters)
            {
                if (art != null && art.Id == id)
                {
                    return art;
                }
            }

            return null;
        }

        /// <summary>按精灵的宽高比把框设成指定高度 —— 配合 preserveAspect 就不会拉变形。</summary>
        private static void FitHeight(RectTransform rect, Sprite sprite, float height)
        {
            float aspect = sprite.rect.height > 0f
                ? sprite.rect.width / sprite.rect.height
                : 1f;

            rect.sizeDelta = new Vector2(height * aspect, height);
        }

        /// <summary>等比缩放到不超过给定的框，且尽量占满 —— 整张图完整可见，不裁切。</summary>
        private static Vector2 ContainSize(Sprite sprite, float maxWidth, float maxHeight)
        {
            float width = sprite.rect.width;
            float height = sprite.rect.height;

            if (width <= 0f || height <= 0f)
            {
                return new Vector2(maxWidth, maxHeight);
            }

            float scale = Mathf.Min(maxWidth / width, maxHeight / height);
            return new Vector2(width * scale, height * scale);
        }

        /// <summary>没有立绘素材时的兜底色，一个角色一个色。</summary>
        private static Color EnemyTint(EnemyId id)
        {
            switch (id)
            {
                case EnemyId.Niulai:
                    return new Color(0.72f, 0.30f, 0.22f, 0.88f);
                case EnemyId.Daishu:
                    return new Color(0.30f, 0.62f, 0.34f, 0.88f);
                case EnemyId.Lulu:
                    return new Color(0.36f, 0.44f, 0.78f, 0.88f);
                default:
                    return new Color(0.88f, 0.80f, 0.34f, 0.88f);
            }
        }

        private void ApplyMonitorOverlay(GameManager gm)
        {
            int index = gm.CurrentCameraIndex;

            // 厨房只有声音没有画面 —— 照官方，切过去一片黑，也不显示任何角色
            if (index == KitchenCameraIndex)
            {
                _monitorOverlay.sprite = _solidSprite;
                _monitorOverlay.color = Color.black;
                _monitorLabel.text = CameraName(index) + "    (AUDIO ONLY)";
                HideMonitorCharacters();
                UpdateCameraBoxes(index);
                return;
            }

            Sprite frame = null;

            if (_monitorFrames != null && index >= 0 && index < _monitorFrames.Length)
            {
                frame = _monitorFrames[index];
            }

            _monitorOverlay.sprite = frame != null ? frame : _solidSprite;
            _monitorOverlay.color = frame != null ? Color.white : MonitorFallback;
            _monitorLabel.text = CameraName(index);

            ApplyMonitorCharacter(gm);

            UpdateCameraBoxes(index);
        }

        /// <summary>小地图上把当前机位那一格点亮。</summary>
        private void UpdateCameraBoxes(int index)
        {
            for (int i = 0; i < _camBoxes.Length; i++)
            {
                _camBoxes[i].color = i == index ? CamBoxOn : CamBoxOff;
            }
        }

        /// <summary>
        /// 监控画面里叠谁。**同一机位可以站多个**（FNAF 1 的舞台上就有三只），
        /// 横排摆开；只有一个时居中。
        ///
        /// 奶龙的贴图按状态分：偷电态 stare（正对镜头）、进攻态 idle（侧身）。
        /// 袋鼠 / 牛来照 FNAF 1 沿机位路径推进，在监控里是站姿。
        /// **敌人站到门口后就拍不到它了** —— 那时它是门口的威胁，得转头看门灯。
        /// </summary>
        private void ApplyMonitorCharacter(GameManager gm)
        {
            if (_enemyDirector == null || _monitorCharacters == null)
            {
                return;
            }

            int cam = gm.CurrentCameraIndex;

            // 收集这一机位上能拍到的敌人（最多三个槽位）
            var ids = new EnemyId[_monitorCharacters.Length];
            var poses = new CharacterPose[_monitorCharacters.Length];
            int count = 0;

            var nailong = _enemyDirector.GetBrain(EnemyId.Nailong) as NailongBrain;
            if (nailong != null && !nailong.IsAttacking
                && nailong.Stage < nailong.MaxStage
                && nailong.CameraIndex == cam
                && count < ids.Length)
            {
                ids[count] = EnemyId.Nailong;
                poses[count] = nailong.IsApproaching ? CharacterPose.Idle : CharacterPose.Stare;
                count++;
            }

            // 门类敌人（袋鼠 / 牛来）：停在某个机位时才现形
            foreach (var brain in _enemyDirector.Brains)
            {
                var door = brain as DoorEnemyBrain;
                if (door == null || door is NailongBrain || door.IsAttacking)
                {
                    continue;
                }

                if (door.PathCameraIndex == cam && count < ids.Length)
                {
                    ids[count] = door.Id;
                    poses[count] = CharacterPose.Idle;
                    count++;
                }
            }

            // 噜噜（照 FNAF 1 的狐狸）：出生在海盗湾，往正门走时在路径机位**探头**
            var lulu = _enemyDirector.GetBrain(EnemyId.Lulu) as LuluBrain;
            if (lulu != null && !lulu.IsAttacking
                && lulu.Stage < lulu.MaxStage
                && lulu.PathCameraIndex == cam
                && count < ids.Length)
            {
                ids[count] = EnemyId.Lulu;
                poses[count] = CharacterPose.Peek;
                count++;
            }

            // 横排摆开：一个居中，两个左右分开，三个均分
            for (int i = 0; i < _monitorCharacters.Length; i++)
            {
                var img = _monitorCharacters[i];

                if (i >= count)
                {
                    img.gameObject.SetActive(false);
                    continue;
                }

                ShowCharacter(img, ids[i], poses[i], MonitorCharacterHeight);

                float step = count <= 1 ? 0f : (count == 2 ? 260f : 230f);
                float offsetX = (i - (count - 1) * 0.5f) * step;

                var rect = img.rectTransform;
                rect.anchoredPosition = new Vector2(offsetX, 46f);
                rect.sizeDelta = new Vector2(260f, MonitorCharacterHeight);
            }
        }

        private static string CameraName(int index)
        {
            if (index < 0 || index >= CameraNames.Length)
            {
                return "CAM --";
            }

            return CameraNames[index];
        }

        private void UpdateWallButtonColors(GameManager gm)
        {
            _doorLeftButton.color = gm.LeftDoorClosed ? DoorOn : DoorOff;
            _doorRightButton.color = gm.RightDoorClosed ? DoorOn : DoorOff;
            _lightLeftButton.color = gm.LeftLightOn ? LightOn : LightOff;
            _lightRightButton.color = gm.RightLightOn ? LightOn : LightOff;

            // LIGHT 光晕：灯亮 → 亮绿呼吸（径向渐变淡入淡出），灯灭 → 熄灭。
            // 呼吸只动 alpha 和 scale，不动颜色。
            float breathe = 0.82f + Mathf.Sin(Time.time * 2.4f) * 0.18f;
            ApplyLightGlow(_lightLeftGlow, gm.LeftLightOn, breathe);
            ApplyLightGlow(_lightRightGlow, gm.RightLightOn, breathe);
        }

        private static void ApplyLightGlow(Image glow, bool on, float breathe)
        {
            if (glow == null)
            {
                return;
            }

            Color color = glow.color;
            color.a = on ? 0.55f * breathe : 0f;
            glow.color = color;

            var rect = glow.rectTransform;
            rect.localScale = on
                ? new Vector3(breathe, breathe, 1f)
                : new Vector3(0.8f, 0.8f, 1f);
        }

        // ---------------------------------------------------------------
        // 跳杀（占位：全屏红 + 大字抖动。等 mp4 就绪后换成 VideoPlayer）
        // ---------------------------------------------------------------

        private void OnEnemyAttacked(EnemyId id)
        {
            _hasJumpscare = true;
            _jumpscareEnemy = id;
            _jumpscareTime = 0f;

            _jumpscareOverlay.gameObject.SetActive(true);
            _jumpscareOverlay.color = Color.black;

            var art = FindCharacter(id);

            // 走兜底链：没有专属跳杀图就退到 Stare（正面那张）——
            // 比全屏红 + 大字强，也免得绿子给的正脸图白放着用不上
            Sprite scare = art != null ? art.Get(CharacterPose.Jumpscare) : null;

            if (scare != null)
            {
                // 有跳杀图：等比缩放到完整可见且尽量大，居中怼在屏幕上
                _jumpscareImage.sprite = scare;
                _jumpscareImage.color = Color.white;
                _jumpscareImage.rectTransform.sizeDelta =
                    ContainSize(scare, JumpscareWidth, CanvasHeight);
                _jumpscareImage.rectTransform.anchoredPosition = Vector2.zero;
                _jumpscareImage.rectTransform.localScale = Vector3.one;

                _jumpscareImage.gameObject.SetActive(true);
                _jumpscareText.gameObject.SetActive(false);
            }
            else
            {
                // 没有图：退回大字占位
                _jumpscareImage.gameObject.SetActive(false);
                _jumpscareText.gameObject.SetActive(true);
                _jumpscareText.text = "JUMPSCARE\n" + id.Code().ToUpperInvariant();
                _jumpscareText.rectTransform.anchoredPosition = Vector2.zero;
            }
        }

        private void ClearJumpscare()
        {
            _hasJumpscare = false;
            _jumpscareOverlay.gameObject.SetActive(false);
        }

        /// <summary>跳杀：先是「冲脸」快速放大，然后一直抖。</summary>
        private void UpdateJumpscareShake()
        {
            if (!_hasJumpscare)
            {
                return;
            }

            _jumpscareTime += Time.deltaTime;
            float t = Time.time;

            float grow = Mathf.Lerp(0.80f, 1.02f, Mathf.Clamp01(_jumpscareTime / 0.18f));
            float x = Mathf.Sin(t * 46f) * 16f;
            float y = Mathf.Cos(t * 37f) * 12f;

            if (_jumpscareImage.gameObject.activeSelf)
            {
                _jumpscareImage.rectTransform.localScale = new Vector3(grow, grow, 1f);
                _jumpscareImage.rectTransform.anchoredPosition = new Vector2(x, y);
                return;
            }

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
                    _wasPowerOut = true;
                }

                UpdatePowerOutMusic();
                return;
            }

            _wasPowerOut = false;

            if (_musicPlaying)
            {
                _audio.StopPowerOutMusic();
                _musicPlaying = false;
            }

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

        /// <summary>
        /// 音乐盒只在断电演出的音乐段响。它一停，画面就该彻底黑下去 ——
        /// 原版 Freddy 的脸就是这么跟着音乐亮完、然后归于一片漆黑的。
        /// </summary>
        private void UpdatePowerOutMusic()
        {
            var blackout = _enemyDirector != null ? _enemyDirector.Blackout : null;
            bool wantMusic = blackout != null && blackout.Phase == BlackoutPhase.Music;

            if (wantMusic == _musicPlaying)
            {
                return;
            }

            _musicPlaying = wantMusic;

            if (wantMusic)
            {
                _audio.PlayPowerOutMusic();
            }
            else
            {
                _audio.StopPowerOutMusic();
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

        /// <summary>
        /// 由编辑器菜单「奶龙 → 5. 接入素材」调用，一次性注入背景帧序列、门片、
        /// 监控画面与角色立绘。
        /// 背景与门都是 **3D 世界物体**（门板 / 敌人站门里），由相机透视，
        /// 不再有 UI 叠层转场。
        /// </summary>
        public void SetArtwork(
            Sprite doorFaceLeft, Sprite doorFaceRight,
            Sprite[] monitorFrames, CharacterArt[] characters)
        {
            _doorFaceLeft = doorFaceLeft;
            _doorFaceRight = doorFaceRight;
            _monitorFrames = monitorFrames;
            _characters = characters;

            if (_doorLeft3D != null && _doorFaceLeft != null)
            {
                _doorLeft3D.sprite = _doorFaceLeft;
            }

            if (_doorRight3D != null && _doorFaceRight != null)
            {
                _doorRight3D.sprite = _doorFaceRight;
            }
        }

        // ---------------------------------------------------------------
        // HUD：时钟在右上角，电量与使用等级在左下角（对齐官方）
        // ---------------------------------------------------------------

        private void UpdateHud(GameManager gm)
        {
            _clockText.text = FormatHour(gm.Clock.CurrentHour);
            _powerText.text = "Power left: " + Mathf.CeilToInt(gm.Power.Level) + "%";
            UpdateUsageBars(gm.Power.UsageLevel);

            // 断电了就没有「还剩多少电、用了几格」这回事了
            _powerGroup.alpha = gm.HasPowerOut ? 0f : 1f;

            _debugText.text = BuildDebugInfo(gm);
        }

        private void UpdateUsageBars(int usageLevel)
        {
            for (int i = 0; i < _usageBars.Length; i++)
            {
                if (i >= usageLevel)
                {
                    _usageBars[i].color = UsageOff;
                    continue;
                }

                if (i < 2)
                {
                    _usageBars[i].color = UsageGreen;
                }
                else if (i == 2)
                {
                    _usageBars[i].color = UsageAmber;
                }
                else
                {
                    _usageBars[i].color = UsageRed;
                }
            }
        }

        private static string FormatHour(int hour)
        {
            return hour == 0 ? "12 AM" : hour + " AM";
        }

        private string BuildDebugInfo(GameManager gm)
        {
            var sb = new StringBuilder();

            if (_enemyDirector != null)
            {
                sb.Append("NIGHT ").Append(_enemyDirector.NightIndex + 1).Append("    ");

                foreach (var id in EnemyIds.All)
                {
                    var brain = _enemyDirector.GetBrain(id);
                    if (brain == null)
                    {
                        continue;
                    }

                    sb.Append(id.Code().ToUpperInvariant())
                      .Append(' ')
                      .Append(DescribeBrain(brain))
                      .Append(" (")
                      .Append(brain.ActivityLevel)
                      .Append(")    ");
                }

                sb.Append('\n');
            }

            sb.Append("DOOR L ").Append(gm.LeftDoorClosed ? "CLOSED" : "OPEN")
              .Append("    DOOR R ").Append(gm.RightDoorClosed ? "CLOSED" : "OPEN")
              .Append("    LIGHT L ").Append(gm.LeftLightOn ? "ON" : "OFF")
              .Append("    LIGHT R ").Append(gm.RightLightOn ? "ON" : "OFF")
              .Append('\n');

            if (_hasJumpscare)
            {
                sb.Append("CAUGHT BY ").Append(_jumpscareEnemy.Code().ToUpperInvariant()).Append('\n');
            }
            else if (gm.HasBeenCaught)
            {
                sb.Append("CAUGHT\n");
            }
            else if (gm.HasSurvived)
            {
                sb.Append("6 AM - YOU SURVIVED\n");
            }
            else if (gm.HasPowerOut)
            {
                sb.Append("POWER OUT\n");
            }

            sb.Append("Mouse: side thirds = turn, bottom edge = monitor, CAM box = switch feed\n");
            sb.Append("R restart    [ ] change night    F1 hide this panel");

            return sb.ToString();
        }

        private static string DescribeBrain(EnemyBrain brain)
        {
            // 奶龙既有路径位置、又蹲在某个机位，两个都要显示
            var nailong = brain as NailongBrain;
            if (nailong != null)
            {
                return "CAM " + (nailong.CameraIndex + 1) + " " + StageName(nailong.Stage);
            }

            var door = brain as DoorEnemyBrain;
            if (door != null)
            {
                // 配了机位路径（袋鼠 / 牛来）：照 FNAF 1 逐房间推进 ——
                // 没到门口就报当前机位，到了门口就报 AT DOOR。
                if (door.HasCameraPath)
                {
                    if (door.Stage >= door.MaxStage)
                    {
                        return "AT DOOR";
                    }

                    int cam = door.PathCameraIndex;
                    return cam >= 0 && cam < CameraNames.Length
                        ? "CAM " + (cam + 1) + " " + CameraNames[cam].Substring(4).Trim()
                        : StageName(door.Stage);
                }

                return StageName(door.Stage);
            }

            var lulu = brain as LuluBrain;
            if (lulu != null)
            {
                switch (lulu.Stage)
                {
                    case 0:
                        return "AWAY";
                    case 1:
                        return "APPROACHING";
                    default:
                        return "AT FRONT " + lulu.FlashCount + "/" + lulu.FlashesToRetreat;
                }
            }

            return "-";
        }

        private static string StageName(int stage)
        {
            switch (stage)
            {
                case 0:
                    return "AWAY";
                case 1:
                    return "CORRIDOR";
                default:
                    return "AT DOOR";
            }
        }

        // ---------------------------------------------------------------
        // 构建 UI（全部代码生成，不依赖任何素材文件）
        //
        // 层级自下而上：背景两层（当前帧 + 下一帧）→ 门口立绘 → 门片两层
        //             → 墙上按钮 → 监控层 → 把手 → HUD → 调试 → 跳杀
        // ---------------------------------------------------------------

        private void BuildUi()
        {
            _canvas = GetOrCreateCanvas();
            _solidSprite = MakeSolidSprite();

            BuildWorldObjects();
            BuildBackgroundLayers();
            BuildFlashlightGlow();
            BuildWallButtons();
            BuildFlashlightButton();
            BuildMonitorLayer();
            BuildMonitorHandle();
            BuildBlackoutLayer();
            BuildHud();
            BuildDebugLayer();
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

        /// <summary>
        /// 断电黑幕：一整铺满屏幕的黑层，压在 3D 房间之上。平时透明（见
        /// <see cref="ApplyRoomView"/>），断电 / 被抓时整屏盖黑（<see cref="SetBlackout"/>）。
        /// 背景本体是 OfficeRoom 渲的 3D 房间，不在这层。
        /// </summary>
        private void BuildBackgroundLayers()
        {
            _bgBase = MakeFullScreenImage("BlackoutCurtain", Color.black);
        }

        // ---------------------------------------------------------------
        // 3D 世界物体：门板 + 门口敌人。
        //
        // 背景是圆柱全景 + 旋转相机，所以门和敌人也放进**同一个 3D 世界**里，
        // 用真实米数摆在门洞 / 正门口，透视由相机自己算 —— 不再是 UI 叠层，
        // 也就没有「叠层转场 / 滑入淡入」那套东西了。
        // 门的坐标就是门洞平面（±2.6, ·, z∈[-1.8,-0.6]）→ 对眼点的偏航正好 77°~103°，
        // 和全景里画的门洞重合，关上时严丝合缝。
        // ---------------------------------------------------------------
        private static readonly Vector3 DoorWorldCenterLeft = new Vector3(-2.6f, 1.025f, -1.2f);
        private static readonly Vector3 DoorWorldCenterRight = new Vector3(2.6f, 1.025f, -1.2f);

        /// <summary>
        /// 门灯光斑的尺寸（米）—— **正好是门洞大小**。
        /// 光只落在门框里，不会在门框外的墙上留一块亮斑。
        /// 光斑贴图本身是「上亮下暗」的渐变（见 <see cref="MakeDoorLightSprite"/>），
        /// 所以虽然光心在门框正中，亮部是偏上的，像门外的灯从上面照进来。
        /// </summary>
        private const float DoorLightWidth = 1.2f;
        private const float DoorLightHeight = 2.05f;

        private void BuildWorldObjects()
        {
            _doorLeft3D = MakeWorldSprite("Door3DLeft", DoorWorldCenterLeft, SpriteSortDoor);
            _doorRight3D = MakeWorldSprite("Door3DRight", DoorWorldCenterRight, SpriteSortDoor);

            _enemySideLeft = MakeWorldSprite("EnemySideLeft", DoorWorldCenterLeft, SpriteSortEnemy);
            _enemySideRight = MakeWorldSprite("EnemySideRight", DoorWorldCenterRight, SpriteSortEnemy);
            _enemyFront = MakeWorldSprite("EnemyFront", FrontDoorCenter, SpriteSortEnemy);

            // 门洞外的灯光：Additive 叠加的径向光斑，亮灯时门口像真的亮了。
            _doorLightLeft = MakeDoorLight("DoorLightLeft", DoorWorldCenterLeft, SpriteSortDoor - 1);
            _doorLightRight = MakeDoorLight("DoorLightRight", DoorWorldCenterRight, SpriteSortDoor - 1);

            // ⚠️ SpriteRenderer 默认平面是 XY（法线朝 +z）。门板 / 门敌站在**侧墙**
            // （x=±2.6 平面），必须绕 Y 转 ±90° 让平面朝房间中心 ——
            // 否则相机侧看它，屏幕上是根竖线（2026-10-15 踩过，门洞区域一直渲染不出门）。
            // 右门（x=+2.6）法线朝 -x：绕 Y 转 -90°（+z → -x）。
            // 左门（x=-2.6）法线朝 +x：绕 Y 转 +90°（+z → +x）。
            _doorLeft3D.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            _doorRight3D.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            _enemySideLeft.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            _enemySideRight.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            // 正门口噜噜（z=+3.0）面朝房间中心（-z）：sprite 默认朝 +z，转 180°。
            _enemyFront.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            if (_doorFaceLeft != null)
            {
                _doorLeft3D.sprite = _doorFaceLeft;
                SetDoorFaceScale(_doorLeft3D);
            }

            if (_doorFaceRight != null)
            {
                _doorRight3D.sprite = _doorFaceRight;
                SetDoorFaceScale(_doorRight3D);
            }
        }

        /// <summary>
        /// 门板贴图是 1.2m × 2.05m 渲出来的（1200×2050 像素），但导入后 PPU 会被
        /// 压到 2048 以下（99.9），直接 scale=1 世界尺寸是 12×20.5m —— 比门洞大十倍。
        /// 按「贴图像素 / 门洞真实尺寸」重算 scale，让世界尺寸正好是 1.2×2.05m：
        /// scale.x = 1.2 / (rect.width / ppu)，scale.y = 2.05 / (rect.height / ppu)。
        /// </summary>
        private static void SetDoorFaceScale(SpriteRenderer sr)
        {
            var sp = sr.sprite;
            if (sp == null)
            {
                return;
            }

            const float DoorWidthMeters = 1.2f;
            const float DoorHeightMeters = 2.05f;

            float worldW = sp.rect.width / sp.pixelsPerUnit;
            float worldH = sp.rect.height / sp.pixelsPerUnit;

            if (worldW <= 0.001f || worldH <= 0.001f)
            {
                return;
            }

            sr.transform.localScale = new Vector3(
                DoorWidthMeters / worldW,
                DoorHeightMeters / worldH,
                1f);
        }

        /// <summary>
        /// 世界空间 SpriteRenderer —— 不缩放到米制以外，靠 PPU 保证「像素 = 真实米」：
        /// 立绘字面是真实身高的像素（噜噜 ~1.7m 的 PNG），尺寸就是对的。
        /// </summary>
        private static SpriteRenderer MakeWorldSprite(string name, Vector3 pivotPoint,
            int sortingOrder)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sortingOrder = sortingOrder;
            sr.color = Color.white;
            go.transform.position = pivotPoint;
            return sr;
        }

        /// <summary>
        /// 门口那盏灯：**上亮下暗**的渐变光斑，Additive 叠加（只加亮不遮黑），
        /// 灯亮时门框里像真的被门外的灯照亮了。默认隐藏，
        /// 由 <see cref="ApplyWorldObjects"/> 按灯状态点亮。
        /// 和门板一样在侧墙平面（绕 Y 转 ±90° 朝房间中心）。
        ///
        /// 光斑**正好和门洞一样大**（见 <see cref="DoorLightWidth"/>）——
        /// 光只落在门框里，不会在门框外的墙上留一块亮斑。
        /// </summary>
        private static SpriteRenderer MakeDoorLight(string name, Vector3 pivotPoint,
            int sortingOrder)
        {
            var sr = MakeWorldSprite(name, pivotPoint, sortingOrder);

            sr.sprite = MakeDoorLightSprite();
            sr.color = new Color(0.35f, 0.55f, 0.30f, 0f);   // 绿光，alpha 0 = 关

            // Additive：纯加亮，不产生「遮黑」的边
            var mat = new Material(Shader.Find("Sprites/Default"));
            if (mat != null)
            {
                // Sprites/Default 没有加法混合，用内置的加亮混合参数
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_ZWrite", 0);
                sr.sharedMaterial = mat;
            }

            // 和门板/门敌一样：侧墙平面朝房间中心
            sr.transform.rotation = pivotPoint.x > 0f
                ? Quaternion.Euler(0f, -90f, 0f)
                : Quaternion.Euler(0f, 90f, 0f);

            // ⚠️ localScale 是乘在 sprite **自然尺寸**（像素 ÷ PPU）上的，不是米。
            // 直接写 1.2 / 2.05 会得到 1.54 × 5.25 米的光斑，溢出到门框外 ——
            // 按「目标米数 ÷ 自然尺寸」折算才正好铺满门洞。
            sr.transform.localScale = new Vector3(
                DoorLightWidth / sr.sprite.bounds.size.x,
                DoorLightHeight / sr.sprite.bounds.size.y, 1f);

            sr.gameObject.SetActive(false);
            return sr;
        }







        /// <summary>
        /// 手电筒的光 —— 盖在画面之上的一层提亮，中心亮、往外渐隐，
        /// 像一束光打在正前方。只在「亮着 + 朝正前方」时淡入。
        /// </summary>
        private void BuildFlashlightGlow()
        {
            _flashlightGlow = MakeFullScreenImage("FlashlightGlow",
                new Color(1f, 0.97f, 0.86f, 0f));

            // 纯色铺满只会整体发灰，换成径向光斑
            _flashlightGlow.sprite = MakeRadialSprite();

            _flashlightGlow.enabled = false;
            _flashlightGlowAlpha = 0f;
        }

        /// <summary>手电筒按钮：按住才亮，松手就灭。</summary>
        private void BuildFlashlightButton()
        {
            _flashlightGroup = MakeGroup("FlashlightGroup");

            var button = MakeButton(_flashlightGroup.transform, "FlashlightButton", "FLASHLIGHT",
                new Vector2(0.5f, 0f), new Vector2(0f, FlashlightButtonBottom),
                new Vector2(240f, 64f), null);

            _flashlightButton = button.GetComponent<Image>();
            _flashlightButton.color = FlashlightOff;

            _flashlightHold = button.gameObject.AddComponent<HoldButton>();
            _flashlightHold.HoldChanged = OnFlashlightHold;

            _flashlightGroup.alpha = 1f;
            _flashlightGroup.blocksRaycasts = true;
        }





        /// <summary>墙上 DOOR / LIGHT 按钮的尺寸（画布逻辑像素）。</summary>
        private static readonly Vector2 WallButtonSize = new Vector2(190f, 62f);

        /// <summary>LIGHT 按钮的光晕尺寸（画布逻辑像素）—— 比按钮大一圈。</summary>
        private static readonly Vector2 LightGlowSize = new Vector2(240f, 92f);

        // 墙上按钮锚的**世界点**（米）。在侧墙上、门框**外侧**一点：
        // 门洞 z ∈ [-1.80, -0.60]，按钮取 z = -1.976（门框外沿 -1.80 再往外 0.18m），
        // 世界偏航角约 ±106.6°。高度按「门在上、灯在下」取 1.55 / 1.05。
        private static readonly Vector3 DoorButtonWorldRight = new Vector3(2.6f, 1.55f, -1.976f);
        private static readonly Vector3 LightButtonWorldRight = new Vector3(2.6f, 1.05f, -1.976f);
        private static readonly Vector3 DoorButtonWorldLeft = new Vector3(-2.6f, 1.55f, -1.976f);
        private static readonly Vector3 LightButtonWorldLeft = new Vector3(-2.6f, 1.05f, -1.976f);

        /// <summary>
        /// 两侧墙上的门 / 灯按钮：门在上、灯在下，与官方一致。
        ///
        /// **按钮贴在墙上**（不是钉在屏幕固定位置）：锚点是墙上一个固定的世界点
        /// （<see cref="DoorButtonWorld"/> 等），每帧投影到画布上摆过去 —— 转头时
        /// 按钮跟着墙一起移动，像画在墙上。
        ///
        /// 位置贴着门框**外侧**：门洞世界偏航角 ±77°~103°，按钮世界点约 ±106.6°，
        /// 满转 70° 时投到屏幕 x ≈ 0.905（门框外沿在 0.854），正好紧挨着门框。
        ///
        /// ⚠️ **必须靠 <see cref="PanSaturationK"/> 那段饱和区才能点得到**：
        /// 鼠标位置同时决定转头量，按钮又跟着墙走 —— 若映射是绝对线性的，鼠标每挪 1 单位，
        /// 墙上的点要滑 `1.09 × 满转角(rad) × sec²Δ` 单位（满转 70°、Δ=34° 时约 1.6），
        /// 比鼠标快，**永远追不上**。饱和区里 `dx/dmouse = 0`，按钮就钉住不动了。
        /// </summary>
        private void BuildWallButtons()
        {
            _leftWallGroup = MakeGroup("WallLeft");
            _doorLeftButton = MakeWallButton(_leftWallGroup.transform, "DoorLeft", "DOOR", OnDoorLeft);
            _lightLeftButton = MakeWallButton(_leftWallGroup.transform, "LightLeft", "LIGHT", OnLightLeft);
            _lightLeftGlow = MakeLightGlow(_lightLeftButton.transform);

            _rightWallGroup = MakeGroup("WallRight");
            _doorRightButton = MakeWallButton(_rightWallGroup.transform, "DoorRight", "DOOR", OnDoorRight);
            _lightRightButton = MakeWallButton(_rightWallGroup.transform, "LightRight", "LIGHT", OnLightRight);
            _lightRightGlow = MakeLightGlow(_lightRightButton.transform);
        }

        /// <summary>
        /// LIGHT 按钮的光晕 —— 叠在按钮底下的径向渐变，灯亮时亮绿呼吸。
        /// 放按钮 transform 的**最底**（SetAsFirstSibling），所以按钮文字盖在光上。
        /// </summary>
        private static Image MakeLightGlow(Transform button)
        {
            var go = new GameObject("LightGlow", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(button, false);
            go.transform.SetAsFirstSibling();

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = LightGlowSize;

            var image = go.GetComponent<Image>();
            image.sprite = MakeRadialSprite();
            image.color = new Color(0.35f, 0.95f, 0.30f, 0f);
            image.raycastTarget = false;

            return image;
        }

        private void BuildMonitorLayer()
        {
            _monitorOverlay = MakeFullScreenImage("MonitorOverlay", MonitorFallback, true);

            _monitorLabel = MakeText(_monitorOverlay.transform, "MonitorLabel", "CAM 1B",
                TextAnchor.MiddleCenter, 46, new Color(0.72f, 0.86f, 0.72f));
            StretchToParent(_monitorLabel.rectTransform);

            // 监控画面里的敌人立绘 —— 三个槽位，同一机位可以站多个
            _monitorCharacters = new Image[3];

            for (int i = 0; i < _monitorCharacters.Length; i++)
            {
                var charGo = new GameObject("MonitorCharacter" + i,
                    typeof(RectTransform), typeof(Image));
                charGo.transform.SetParent(_monitorOverlay.transform, false);

                var charRect = charGo.GetComponent<RectTransform>();
                charRect.anchorMin = new Vector2(0.5f, 0f);
                charRect.anchorMax = new Vector2(0.5f, 0f);
                charRect.pivot = new Vector2(0.5f, 0f);
                charRect.anchoredPosition = new Vector2(0f, 46f);
                charRect.sizeDelta = new Vector2(260f, MonitorCharacterHeight);

                var img = charGo.GetComponent<Image>();
                img.sprite = _solidSprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
                img.color = Color.white;

                _monitorCharacters[i] = img;
                charGo.SetActive(false);
            }

            // 小地图 —— 整张 FNAF 1 官方平面图，只有我们用得到的 3 间可点
            var map = new GameObject("MonitorMap", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            map.transform.SetParent(_monitorOverlay.transform, false);

            var mapRect = map.GetComponent<RectTransform>();
            PlaceRect(mapRect, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-MapPanelMargin, MapPanelBottomOffset),
                new Vector2(MapPanelWidth, MapPanelHeight));

            var mapBackground = map.GetComponent<Image>();
            mapBackground.sprite = _solidSprite;
            mapBackground.color = MapPanelFill;
            mapBackground.raycastTarget = false;

            _monitorMapGroup = map.GetComponent<CanvasGroup>();

            // 走廊先铺，房间盖在上面，接缝才干净
            for (int i = 0; i < MapCorridors.Length; i++)
            {
                MakeMapBox(map.transform, "Corridor" + i, MapCorridors[i],
                    MapRoomFill, MapCorridorColor, null,
                    MapBorderThickness, MapLabelFontSize, MapCorridorColor, null);
            }

            // 房间：只描边不填色（内层填成面板底色），跟官方原图一样
            for (int i = 0; i < MapRooms.Length; i++)
            {
                MakeMapBox(map.transform, "MapRoom" + i, MapRooms[i],
                    MapRoomFill, MapRoomBorder, MapRooms[i].Label,
                    MapBorderThickness, MapLabelFontSize, MapRoomBorder, null);
            }

            // CAM 标号框：位置照官方原图；只有我们有的三个可点
            _camBoxes = new Image[CameraNames.Length];
            for (int i = 0; i < MapCamButtons.Length; i++)
            {
                MapRectSpec spec = MapCamButtons[i];
                int camIndex = spec.CameraIndex;
                bool isCamera = camIndex >= 0;

                UnityEngine.Events.UnityAction onClick = null;
                if (isCamera)
                {
                    int index = camIndex;
                    onClick = delegate { OnCameraBox(index); };
                }

                Image box = MakeMapBox(map.transform, "CamButton" + i, spec,
                    isCamera ? CamBoxOff : MapButtonFill,
                    isCamera ? CamBoxBorder : MapButtonBorder,
                    spec.Label, MapButtonBorderThickness, MapLabelFontSize,
                    isCamera ? CamBoxText : MapButtonText,
                    onClick);

                if (isCamera)
                {
                    _camBoxes[camIndex] = box;
                }
            }

            _monitorMapGroup.alpha = 0f;
            _monitorMapGroup.blocksRaycasts = false;
        }

        private void BuildMonitorHandle()
        {
            var go = new GameObject("MonitorHandle", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.GetComponent<RectTransform>();
            PlaceRect(rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 14f), new Vector2(260f, 26f));

            _monitorHandle = go.GetComponent<Image>();
            _monitorHandle.sprite = _solidSprite;
            _monitorHandle.color = new Color(0.55f, 0.60f, 0.55f, 0.30f);
            _monitorHandle.raycastTarget = false;

            var label = MakeText(go.transform, "Label", "<<   MONITOR   >>",
                TextAnchor.MiddleCenter, 18, new Color(0.88f, 0.92f, 0.88f, 0.80f));
            StretchToParent(label.rectTransform);
        }

        /// <summary>
        /// 断电后从黑暗里冒出来的奶龙。
        /// 摆在**左门**位置 —— 它平时走右门，断电时却从另一边来「收电费」。
        /// </summary>
        private void BuildBlackoutLayer()
        {
            var go = new GameObject("BlackoutCharacter", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(BlackoutAnchorX, 0f);
            rect.sizeDelta = new Vector2(600f, BlackoutCharacterHeight);

            _blackoutCharacter = go.GetComponent<Image>();
            _blackoutCharacter.sprite = _solidSprite;
            _blackoutCharacter.preserveAspect = true;
            _blackoutCharacter.raycastTarget = false;
            _blackoutCharacter.color = Color.white;

            go.SetActive(false);
        }

        private void BuildHud()
        {
            var hud = new GameObject("Hud", typeof(RectTransform));
            hud.transform.SetParent(_canvas.transform, false);
            StretchToParent(hud.GetComponent<RectTransform>());

            // 时钟：右上角
            _clockText = MakeText(hud.transform, "ClockText", "12 AM",
                TextAnchor.UpperRight, 38, new Color(0.94f, 0.94f, 0.94f));
            PlaceRect(_clockText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-40f, -28f), new Vector2(300f, 54f));

            // 电量 + Usage：左下角。装在同一个组里，断电后整组收起
            var powerGroup = new GameObject("PowerGroup", typeof(RectTransform), typeof(CanvasGroup));
            powerGroup.transform.SetParent(hud.transform, false);
            StretchToParent(powerGroup.GetComponent<RectTransform>());
            _powerGroup = powerGroup.GetComponent<CanvasGroup>();

            _powerText = MakeText(powerGroup.transform, "PowerText", "Power left: 99%",
                TextAnchor.LowerLeft, 30, new Color(0.92f, 0.92f, 0.92f));
            PlaceRect(_powerText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(40f, 104f), new Vector2(420f, 40f));

            var usageLabel = MakeText(powerGroup.transform, "UsageLabel", "Usage:",
                TextAnchor.LowerLeft, 24, new Color(0.80f, 0.80f, 0.80f));
            PlaceRect(usageLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(40f, 62f), new Vector2(110f, 30f));

            _usageBars = new Image[PowerSystem.MaxUsageLevel];
            for (int i = 0; i < _usageBars.Length; i++)
            {
                var go = new GameObject("UsageBar" + i, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(powerGroup.transform, false);

                PlaceRect(go.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(152f + i * 30f, 62f), new Vector2(22f, 26f));

                var image = go.GetComponent<Image>();
                image.sprite = _solidSprite;
                image.color = UsageOff;
                image.raycastTarget = false;

                _usageBars[i] = image;
            }
        }

        private void BuildDebugLayer()
        {
            var go = new GameObject("DebugLayer", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_canvas.transform, false);
            StretchToParent(go.GetComponent<RectTransform>());

            _debugGroup = go.GetComponent<CanvasGroup>();
            _debugGroup.alpha = 0f;
            _debugGroup.blocksRaycasts = false;

            _debugText = MakeText(go.transform, "DebugText", string.Empty,
                TextAnchor.UpperLeft, 20, new Color(0.70f, 0.88f, 0.72f));
            PlaceRect(_debugText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(40f, 210f), new Vector2(1500f, 300f));
        }

        private void BuildJumpscareLayer()
        {
            _jumpscareOverlay = MakeFullScreenImage("JumpscareOverlay", Color.black);
            _jumpscareOverlay.transform.SetAsLastSibling();

            // 跳杀立绘：等比缩放到完整可见且尽量大，居中铺在跳杀层上
            var imageGo = new GameObject("JumpscareImage", typeof(RectTransform), typeof(Image));
            imageGo.transform.SetParent(_jumpscareOverlay.transform, false);

            var imageRect = imageGo.GetComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            imageRect.anchoredPosition = Vector2.zero;
            imageRect.sizeDelta = new Vector2(JumpscareWidth, CanvasHeight);

            _jumpscareImage = imageGo.GetComponent<Image>();
            _jumpscareImage.sprite = _solidSprite;
            _jumpscareImage.color = Color.white;
            _jumpscareImage.raycastTarget = false;
            imageGo.SetActive(false);

            // 没有跳杀素材时退回的大字占位
            _jumpscareText = MakeText(_jumpscareOverlay.transform, "JumpscareText", string.Empty,
                TextAnchor.MiddleCenter, 120, Color.white);
            StretchToParent(_jumpscareText.rectTransform);

            _jumpscareOverlay.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------
        // 按钮回调
        // ---------------------------------------------------------------

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

        private void OnCameraBox(int index)
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                SelectCamera(gm, index);
            }
        }

        // ---------------------------------------------------------------
        // 小工具
        // ---------------------------------------------------------------

        private CanvasGroup MakeGroup(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_canvas.transform, false);
            StretchToParent(go.GetComponent<RectTransform>());

            var group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            return group;
        }

        /// <summary>
        /// 墙上按钮。位置**不在建的时候定** —— 锚点钉在画布中心，之后每帧由
        /// <see cref="ApplyWallButtons"/> 按世界点投影摆过去。
        /// </summary>
        private Image MakeWallButton(Transform parent, string name, string label,
            UnityEngine.Events.UnityAction onClick)
        {
            var button = MakeButton(parent, name, label, new Vector2(0.5f, 0.5f), Vector2.zero,
                WallButtonSize, onClick);

            return button.GetComponent<Image>();
        }

        /// <summary>
        /// 把一个固定世界点投到 ScreenSpaceOverlay 画布上的本地坐标。
        /// 返回 false = 点在相机背后（或没有相机/画布），调用方应把按钮收起来。
        ///
        /// ⚠️ `WorldToScreenPoint` 在点跑到相机背后时会返回**镜像过的** x/y，
        /// 必须靠 `z <= 0` 丢掉，否则按钮会瞬移到屏幕另一边。
        /// </summary>
        private bool TryProjectToCanvas(Vector3 world, out Vector2 canvasLocal)
        {
            canvasLocal = Vector2.zero;

            var cam = _room != null ? _room.Camera : null;
            if (cam == null || _canvas == null)
            {
                return false;
            }

            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f)
            {
                return false;
            }

            // overlay 画布没有相机 → camera 参数传 null。缩放（CanvasScaler）它已经算进去了。
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, screen, null, out canvasLocal);
        }

        /// <summary>
        /// 每帧把墙上按钮摆到各自世界点的投影处。**必须在 `_room.SetPan(_pan)` 之后调** ——
        /// 相机朝向变了投影才准。
        ///
        /// 投影失败（点在相机背后）或落在画布外太远就整组收起来 —— 视角没转过去时
        /// 按钮本来就在画面外，不用额外做淡入。
        /// </summary>
        private void ApplyWallButtons(bool hidden)
        {
            ApplyWallSide(_leftWallGroup, _doorLeftButton, _lightLeftButton, hidden,
                DoorButtonWorldLeft, LightButtonWorldLeft);
            ApplyWallSide(_rightWallGroup, _doorRightButton, _lightRightButton, hidden,
                DoorButtonWorldRight, LightButtonWorldRight);
        }

        private void ApplyWallSide(CanvasGroup group, Image doorButton, Image lightButton,
            bool hidden, Vector3 doorWorld, Vector3 lightWorld)
        {
            if (hidden)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
                return;
            }

            Vector2 doorPos;
            Vector2 lightPos;
            bool okDoor = TryProjectToCanvas(doorWorld, out doorPos) && InsideCanvas(doorPos);
            bool okLight = TryProjectToCanvas(lightWorld, out lightPos) && InsideCanvas(lightPos);

            PlaceWallButton(doorButton, okDoor, doorPos);
            PlaceWallButton(lightButton, okLight, lightPos);

            bool any = okDoor || okLight;
            group.alpha = any ? 1f : 0f;
            group.blocksRaycasts = any;
        }

        private void PlaceWallButton(Image button, bool visible, Vector2 canvasLocal)
        {
            if (button == null)
            {
                return;
            }

            button.gameObject.SetActive(visible);
            if (visible)
            {
                ((RectTransform)button.transform).anchoredPosition = canvasLocal;
            }
        }

        /// <summary>投影点是不是落在画布内（留半个按钮的余量，让按钮能贴着边露出半个）。</summary>
        private bool InsideCanvas(Vector2 canvasLocal)
        {
            var rect = (RectTransform)_canvas.transform;
            Vector2 half = rect.rect.size * 0.5f + WallButtonSize * 0.5f;
            return Mathf.Abs(canvasLocal.x) <= half.x && Mathf.Abs(canvasLocal.y) <= half.y;
        }

        private Image MakeFullScreenImage(string name, Color color, bool raycast = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_canvas.transform, false);

            StretchToParent(go.GetComponent<RectTransform>());

            var image = go.GetComponent<Image>();
            image.sprite = _solidSprite != null ? _solidSprite : MakeSolidSprite();
            image.color = color;
            image.raycastTarget = raycast;

            return image;
        }

        private Button MakeButton(Transform parent, string name, string label,
            Vector2 anchor, Vector2 offset, Vector2 size,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            PlaceRect(go.GetComponent<RectTransform>(), anchor, new Vector2(0.5f, 0.5f), offset, size);

            var image = go.GetComponent<Image>();
            image.sprite = GetButtonSprite();
            image.type = Image.Type.Sliced;
            image.color = ButtonFace;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.colors = MakeButtonColors();

            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            var text = MakeText(go.transform, "Label", label,
                TextAnchor.MiddleCenter, 24, Color.white);
            StretchToParent(text.rectTransform);

            return button;
        }

        /// <summary>按钮被鼠标划过 / 按下时的明暗反馈（和状态色相乘，不打架）。</summary>
        private static ColorBlock MakeButtonColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.22f, 1.22f, 1.22f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            colors.fadeDuration = 0.06f;
            return colors;
        }

        /// <summary>
        /// 按钮底图：圆角矩形 + 一圈略亮的描边（九宫格拉伸，任意尺寸都不糊）。
        /// 生成一次就缓存住，所有按钮共用。
        /// </summary>
        private Sprite GetButtonSprite()
        {
            if (_buttonSprite == null)
            {
                _buttonSprite = MakeRoundedSprite(48, 12, 2, 15);
            }

            return _buttonSprite;
        }

        /// <summary>
        /// 画一张圆角矩形贴图：内部填 <c>ButtonFace</c>、边上一圈接近纯白。
        /// Image.color 一乘，描边就比底色亮一档 —— 按钮因此有「厚度」。
        /// <paramref name="sliceBorder"/> 是九宫格切边的像素数（别小于 radius + border）。
        /// </summary>
        private static Sprite MakeRoundedSprite(int size, int radius, int border, int sliceBorder)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "prototype_button";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float half = size * 0.5f;
            float inner = half - radius;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 到圆角矩形的有符号距离：< 0 在里面、> 0 在外面
                    float dx = Mathf.Abs(x + 0.5f - half) - inner;
                    float dy = Mathf.Abs(y + 0.5f - half) - inner;
                    float outside = Mathf.Sqrt(
                        Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f)
                        + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float distance = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;

                    // 边缘 1 像素做抗锯齿
                    float coverage = Mathf.Clamp01(0.5f - distance);
                    Color color = distance >= -border ? ButtonEdge : ButtonFace;

                    pixels[y * size + x] = new Color(color.r, color.g, color.b,
                        color.a * coverage);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            var spriteBorder = new Vector4(sliceBorder, sliceBorder, sliceBorder, sliceBorder);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, spriteBorder);
        }

        /// <summary>
        /// 平面图上的一块矩形：外圈描边、里面填色。
        /// 内层填成和底色一样就是「只描边不填色」的房间。
        /// <paramref name="onClick"/> 不为空就做成可点的机位框。
        /// 返回里面那张 —— 高亮时改它的颜色。
        /// </summary>
        private Image MakeMapBox(Transform parent, string name, MapRectSpec spec,
            Color fill, Color border, string label,
            float borderThickness, int fontSize, Color labelColor,
            UnityEngine.Events.UnityAction onClick)
        {
            var outer = new GameObject(name, typeof(RectTransform), typeof(Image));
            outer.transform.SetParent(parent, false);
            PlaceRect(outer.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), spec.Center, spec.Size);

            var outerImage = outer.GetComponent<Image>();
            outerImage.sprite = _solidSprite;
            outerImage.color = border;
            outerImage.raycastTarget = false;

            var innerGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            innerGo.transform.SetParent(outer.transform, false);

            var innerRect = innerGo.GetComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(borderThickness, borderThickness);
            innerRect.offsetMax = new Vector2(-borderThickness, -borderThickness);

            var innerImage = innerGo.GetComponent<Image>();
            innerImage.sprite = _solidSprite;
            innerImage.color = fill;

            if (!string.IsNullOrEmpty(label))
            {
                var text = MakeText(innerGo.transform, "Label", label,
                    TextAnchor.MiddleCenter, fontSize, labelColor);
                StretchToParent(text.rectTransform);
            }

            if (onClick != null)
            {
                var button = innerGo.AddComponent<Button>();
                button.targetGraphic = innerImage;
                button.onClick.AddListener(onClick);
            }
            else
            {
                innerImage.raycastTarget = false;
            }

            return innerImage;
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

        /// <summary>
        /// 手电筒的光斑：中心不透明、往外平方衰减到全透明。
        /// 拉伸铺满屏幕，看起来就是一束打在正前方的光。
        /// </summary>
        private static Sprite MakeRadialSprite()
        {
            const int size = 256;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "prototype_radial";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // 平方一下，边缘收得更快，光心更聚
                    float falloff = Mathf.Clamp01(1f - distance);
                    float alpha = falloff * falloff;

                    pixels[y * size + x] = new Color32(255, 247, 219, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
        }

        /// <summary>
        /// 门灯的光斑：**上亮下暗的垂直渐变**（不是径向）。
        /// 贴上去正好和门洞一样大，光只落在门框里。
        ///
        /// 为什么不用径向：径向的光心在正中，看着像门洞里悬了个发光球；
        /// 垂直渐变让亮部偏上，像门外的灯从上方照进来。
        /// 顶部和两侧都做了柔化，免得出现硬边。
        /// </summary>
        private static Sprite MakeDoorLightSprite()
        {
            const int w = 128, h = 256;

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.name = "prototype_door_light";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                // 纹理 y 向上：v = 0 是底、1 是顶
                float v = y / (float)(h - 1);

                float vert;
                if (v <= 0.82f)
                {
                    vert = v / 0.82f;
                    vert *= vert;                       // 往上加速变亮，亮部更靠上
                }
                else
                {
                    vert = 1f - (v - 0.82f) / 0.18f;    // 顶部 18% 收掉，别顶出一条硬边
                }

                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)(w - 1) * 2f - 1f;
                    float side = Mathf.Clamp01(1f - Mathf.Abs(u));
                    side *= side;                       // 两侧收得快一点

                    float alpha = Mathf.Clamp01(vert) * side;
                    pixels[y * w + x] = new Color32(255, 247, 219, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, w, h),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void PlaceRect(RectTransform rect, Vector2 anchor, Vector2 pivot,
            Vector2 offset, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }
    }
}
