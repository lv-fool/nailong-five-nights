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
    public class PrototypeScene : MonoBehaviour
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

        /// <summary>
        /// 门口立绘占画布高度的比例。
        /// **别改回写死的像素** —— CanvasScaler 按宽高折中缩放，宽屏下画布的逻辑高度
        /// 会被压到 1080 以下（当前 GameView 只有 925），写死 940 会把头顶切出屏幕。
        /// 按比例算，任何宽高比都放得下。
        ///
        /// 这个数是照背景图反推的：左右两张背景的门洞顶边落在画布高度 16.6% 处，
        /// 立绘底边再沉下去一点（见 <see cref="SideCharacterSinkRatio"/>），
        /// 头顶就正好顶在门框下沿 —— 照原版那种「站在门里、下半身被裁掉」。
        /// </summary>
        private const float SideCharacterHeightRatio = 0.86f;

        /// <summary>门口立绘默认沉到屏幕下方多少（占画布高度）。脚被裁掉，人才像站在门里。</summary>
        private const float SideCharacterSinkRatio = 0.08f;

        /// <summary>
        /// 门口立绘的横向锚点 —— 门洞在屏幕上的中心位置（占画布宽度）。
        /// 和 <c>tools/gen_placeholder_art.py</c> 里算出来的门洞投影一致：
        /// 左门 x ∈ [8.0%, 33.2%]、右门 x ∈ [66.8%, 92.0%]，中心分别是 20.6% / 79.4%。
        ///
        /// **立绘按门心对齐，不是按屏幕边对齐** —— 换个宽高比不同的角色（牛来又高又窄、
        /// 袋鼠又矮又宽），贴着屏幕边摆就会各自偏到一边去。
        /// </summary>
        private const float SideCharacterAnchorLeft = 0.206f;
        private const float SideCharacterAnchorRight = 0.794f;

        /// <summary>
        /// 镜头刚转起来时立绘停在哪儿 —— 故意放在画面外一点，
        /// 让它是**跟着门框从屏幕边缘滑进来**的，而不是凭空淡出来。
        /// </summary>
        private const float OffscreenAnchorLeft = -0.18f;
        private const float OffscreenAnchorRight = 1.18f;

        /// <summary>监控画面里立绘的高度。</summary>
        private const float MonitorCharacterHeight = 660f;

        /// <summary>正前方那个立绘（噜噜）占画布高度的比例 —— 比两侧大一圈，它是正对着你的。</summary>
        private const float FrontCharacterHeightRatio = 0.80f;

        /// <summary>
        /// 画布的逻辑高度。**不能当成 1080 用** —— 立绘尺寸都按它折算，
        /// 宽屏下才不会切到头顶。
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

        private float SideCharacterHeight
        {
            get { return CanvasHeight * SideCharacterHeightRatio; }
        }

        private float FrontCharacterHeight
        {
            get { return CanvasHeight * FrontCharacterHeightRatio; }
        }

        /// <summary>
        /// 正前方立绘的**下边缘切在哪儿**（占画布高度，从底往上算）。
        ///
        /// 噜噜是从后墙那道正门探进来的，**桌子在它前面** —— 所以膝盖以下不该看得见，
        /// 得被桌沿挡住。背景图里桌子的远边正好落在画布高度 62.8% 处（从上算），
        /// 也就是从下算 37.2%，这里对齐那条线，腿就「消失」在桌沿后面了。
        /// **改了 <c>tools/gen_placeholder_art.py</c> 里的桌子位置或视场角，这个数要跟着重算。**
        /// </summary>
        private const float FrontCharacterClipBottom = 0.372f;

        /// <summary>正前方立绘贴屏幕底边多高。要高于监控热区（0.10 × 1080 = 108）。</summary>
        private const float FrontCharacterBottom = 40f;

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
        /// 镜头**直接跟鼠标**：鼠标在屏幕哪儿，镜头就朝哪儿 ——
        /// 最左 = 满转左（-1）、正中 = 正前方（0）、最右 = 满转右（+1）。
        /// （不再是原版那种「碰左右三分之一才满转、中间不动」的跳档。）
        ///
        /// 这个值只是跟手的缓动时间（秒），用来吃掉鼠标的微小抖动，别调大 ——
        /// 大了镜头就会「拖」在鼠标后面。想完全 1:1 跟手就写 0。
        /// </summary>
        private const float TurnSmoothTime = 0.08f;

        /// <summary>鼠标进入屏幕底部这个比例就切换监控。</summary>
        private const float MonitorZone = 0.10f;

        /// <summary>
        /// 两侧墙上的 DOOR / LIGHT 按钮的淡入区间（按 |_pan|）。
        ///
        /// 门在侧墙上，**pan ≥ 0.72 左右门才进画面**（按钮也贴在门框那儿，
        /// 见 <see cref="BuildWallButtons"/>），所以淡入要等到转过去才起 ——
        /// 太早的话按钮会孤零零浮在没门的墙上。
        /// 到 <see cref="WallButtonFadeEnd"/> 完全不透明、可点（要 alpha > 0.5 才收点击）。
        ///
        /// ⚠️ 这两个数和按钮位置（0.12 / 0.88）是一套：
        /// 鼠标停在按钮上时 `|_pan|` = 0.76，所以 FadeEnd 必须 ≤ 0.76，
        /// 而且 FadeStart 要够小，让按钮整条（pan 0.66~0.86）都过 0.5 那条线。改一个核另一个。
        /// </summary>
        private const float WallButtonFadeStart = 0.54f;
        private const float WallButtonFadeEnd = 0.74f;

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
        /// <summary>门片也是逐帧的 —— 门关着时，门在画面上的位置随镜头一起动。</summary>
        [SerializeField] private Sprite[] _doorLeftClosedFrames;
        [SerializeField] private Sprite[] _doorRightClosedFrames;

        [SerializeField] private Sprite[] _monitorFrames;

        [Header("角色立绘（缺哪张哪张退回色块）")]
        [SerializeField] private CharacterArt[] _characters;

        private Canvas _canvas;

        /// <summary>3D 办公室（全景条 + 旋转相机）。背景是它，不是背景帧序列。</summary>
        private OfficeRoom _room;

        /// <summary>
        /// 断电时盖全屏的黑幕。平时整层透明，露出后面的 3D 房间（见 OfficeRoom）。
        /// </summary>
        private Image _bgBase;

        private Image _sideLeftCharacter;
        private Image _sideRightCharacter;

        /// <summary>正前方那个立绘 —— 噜噜从正门来，站在这儿。</summary>
        private Image _frontCharacter;

        /// <summary>正前方立绘的裁切遮罩（桌沿以下裁掉）。</summary>
        private RectMask2D _frontCharacterClip;

        private float _frontCharacterClipPadding = -1f;

        /// <summary>门片两层，跟背景同样两层结构。</summary>
        private Image _doorBase;
        private Image _doorBlend;
        private CanvasGroup _leftWallGroup;
        private CanvasGroup _rightWallGroup;
        private Image _doorLeftButton;
        private Image _doorRightButton;
        private Image _lightLeftButton;
        private Image _lightRightButton;
        private CanvasGroup _flashlightGroup;
        private Image _flashlightButton;
        private HoldButton _flashlightHold;
        private Image _flashlightGlow;
        private float _flashlightGlowAlpha;
        private Image _monitorOverlay;
        private Image _monitorCharacter;
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

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
            {
                Debug.LogWarning("[奶龙] 没取到内置字体，HUD 上的文字不会显示（功能不受影响）。");
            }

            SyncPowerOutMusicLength();

            BuildUi();
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

                // 鼠标位置直接映射成转头量：左端 -1、正中 0、右端 +1。
                _panTarget = Mathf.Clamp(x * 2f - 1f, -1f, 1f);
            }
            else
            {
                // 断电 / 天亮 / 被抓 —— 视角锁回正面，只能眼睁睁看着
                _panTarget = 0f;
            }

            // 一点缓动：跟手，但不让鼠标的微小抖动直接抖到画面上
            _pan = Mathf.SmoothDamp(_pan, _panTarget, ref _panVelocity, TurnSmoothTime);

            // 供敌人 AI 判断"玩家在看哪边"：转过一半才算真的看向那一侧
            ViewSide view;
            if (_pan <= -0.5f)
            {
                view = ViewSide.Left;
            }
            else if (_pan >= 0.5f)
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
                    HideDoors();
                    HideSideCharacters();
                    HideFrontCharacter();
                }
                else
                {
                    ApplyDoors(gm);
                    ApplySideCharacters(gm);
                    ApplyFrontCharacter(gm);
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

            // 两侧墙上的按钮：跟着转头淡入淡出，看监控时收起来
            //
            // ⚠️ 淡入区间（WallButtonFadeStart/End）必须和按钮位置配套，否则按不到：
            // 镜头跟鼠标 —— 鼠标停在按钮上时 |_pan| 就等于 2×按钮屏幕x − 1
            // （按钮在 0.12 / 0.88 → |_pan| = 0.76），而下面 blocksRaycasts 要
            // alpha > 0.5 才收点击。淡入太靠后（|_pan| 到不了）按钮就永远点不着。
            float shown = Mathf.Clamp01(
                (Mathf.Abs(_pan) - WallButtonFadeStart) / (WallButtonFadeEnd - WallButtonFadeStart));
            float leftAlpha = monitorUp || locked || _pan >= 0f ? 0f : shown;
            float rightAlpha = monitorUp || locked || _pan <= 0f ? 0f : shown;

            _leftWallGroup.alpha = leftAlpha;
            _rightWallGroup.alpha = rightAlpha;
            _leftWallGroup.blocksRaycasts = leftAlpha > 0.5f;
            _rightWallGroup.blocksRaycasts = rightAlpha > 0.5f;

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
            _doorBase.gameObject.SetActive(false);
            _doorBlend.gameObject.SetActive(false);
        }

        /// <summary>
        /// 门片跟着镜头一起走 —— 帧序号直接由 _pan 按自己的帧数算，
        /// 满转（|_pan| = 1）正好落在序列末帧，那一帧是正侧面对准相机的那一张。
        /// 原来是从背景序列借帧号（背景改用 3D 相机后就没了），现在自己算。
        /// </summary>
        private void ApplyDoors(GameManager gm)
        {
            Sprite[] frames = _pan < 0f ? _doorLeftClosedFrames : _doorRightClosedFrames;
            bool closed = _pan < 0f ? gm.LeftDoorClosed : gm.RightDoorClosed;

            if (!closed || frames == null || frames.Length <= 1)
            {
                HideDoors();
                return;
            }

            // 第 0 帧留空（正前方看不到门），所以有效帧是 1..n-1
            float t = Mathf.Abs(Mathf.Clamp(_pan, -1f, 1f)) * (frames.Length - 1);
            int i0 = Mathf.Clamp(Mathf.FloorToInt(t), 0, frames.Length - 1);
            int i1 = Mathf.Min(i0 + 1, frames.Length - 1);
            float frac = Mathf.Clamp01(t - i0);

            SetDoorLayer(_doorBase, At(frames, i0), 1f);
            SetDoorLayer(_doorBlend, i1 != i0 ? At(frames, i1) : null, frac);
        }

        private static Sprite At(Sprite[] frames, int index)
        {
            return index >= 0 && index < frames.Length ? frames[index] : null;
        }

        private void SetDoorLayer(Image image, Sprite sprite, float alpha)
        {
            if (sprite == null || alpha <= 0.001f)
            {
                image.gameObject.SetActive(false);
                return;
            }

            image.gameObject.SetActive(true);
            image.sprite = sprite;

            Color color = Color.white;
            color.a = alpha;
            image.color = color;
        }

        // ---------------------------------------------------------------
        // 敌人立绘
        // ---------------------------------------------------------------

        private void ApplySideCharacters(GameManager gm)
        {
            ApplySideCharacter(gm, Side.Left, _sideLeftCharacter);
            ApplySideCharacter(gm, Side.Right, _sideRightCharacter);
        }

        private void ApplySideCharacter(GameManager gm, Side side, Image image)
        {
            EnemyId id;
            bool present = TryGetEnemyAtSide(gm, side, out id);

            // 门口是**随镜头扫进画面**的：镜头转到 70° 上下，门框才从屏幕边缘露出来。
            // 所以立绘不能按「转了多少」线性淡入 —— 那会在门还在画面外的时候就先冒出来。
            // 这两条曲线都对着门洞进画面的角度来：
            //   · 0.70 之前完全不显示（门还没进来）
            //   · 0.74 → 1.0 之间从画面外滑到门心，同时淡入
            //
            // ⚠️ 取的是**带符号**的 _pan：往左转时右边门口的人不该跟着冒出来。
            float turn = Mathf.Clamp01(side == Side.Left ? -_pan : _pan);
            float look = Mathf.Clamp01((turn - 0.70f) / 0.24f);
            float alpha = present ? look : 0f;

            if (alpha <= 0.01f)
            {
                image.gameObject.SetActive(false);
                return;
            }

            // 门口站着的都是门类敌人（噜噜改走正门了），用站姿
            float heightRatio, sinkRatio;
            SideCharacterFit(id, out heightRatio, out sinkRatio);

            ShowCharacter(image, id, CharacterPose.Idle, CanvasHeight * heightRatio, alpha);

            // 门口的敌人一律面朝玩家。素材都是朝左画的 ——
            // 右门原样就对着玩家，左门要镜像过来。
            // pivot 在立绘底边正中，镜像以 pivot 为轴，所以横向位置不会跑。
            var rect = image.rectTransform;
            bool mirrored = side == Side.Left;

            // 门是随镜头扫进来的，立绘就得跟着门一起滑 —— 站位固定在门心，
            // 但转的过程中门心自己就在往画面里移。不跟着走的话，人比门先出现。
            float slide = Mathf.InverseLerp(0.74f, 1f, turn);
            float anchorX = side == Side.Left
                ? Mathf.Lerp(OffscreenAnchorLeft, SideCharacterAnchorLeft, slide)
                : Mathf.Lerp(OffscreenAnchorRight, SideCharacterAnchorRight, slide);

            rect.anchorMin = new Vector2(anchorX, 0f);
            rect.anchorMax = new Vector2(anchorX, 0f);

            rect.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
            rect.anchoredPosition = new Vector2(0f, -CanvasHeight * sinkRatio);
        }

        /// <summary>
        /// 门口立绘的尺寸 —— 素材构图不一样，按同一个高度对齐会不协调：
        /// 牛来是全身拟人照（含长腿），身体只占整图的上半截；袋鼠是四足站姿，本来就矮胖。
        /// 所以每个角色给一组系数，都乘在画布高度上。
        ///
        /// <c>sinkRatio</c> = 立绘底边沉到屏幕下方多少 ——
        /// 照原版那种「它站在门里、下半身被画面裁掉」，露出来的身体才够大。
        /// </summary>
        private static void SideCharacterFit(EnemyId id, out float heightRatio,
            out float sinkRatio)
        {
            switch (id)
            {
                case EnemyId.Niulai:
                    // 全身照含腿，同一个「高度」下身体显得小一圈，所以放大一点、沉得更深。
                    // 头顶要落在门框下沿（画布高 22%）：头顶 = 1 - 高度 + 下沉
                    heightRatio = 0.94f;
                    sinkRatio = 0.16f;
                    break;

                default:
                    heightRatio = SideCharacterHeightRatio;
                    sinkRatio = SideCharacterSinkRatio;
                    break;
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

                // Stage 2 = 站在门口
                if (door.Stage >= 2)
                {
                    id = door.Id;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 正前方那个立绘 —— 噜噜从正门摸进来。
        /// 逼近时是站姿，堵到门口换成正对镜头；**堵到门口之后**被手电筒照着才换成受惊。
        /// </summary>
        private void ApplyFrontCharacter(GameManager gm)
        {
            var lulu = _enemyDirector != null
                ? _enemyDirector.GetBrain(EnemyId.Lulu) as LuluBrain
                : null;

            ApplyFrontCharacterClip();

            // 只有正前方才看得见 —— 转头看两边时它不在画面里
            float look = Mathf.Clamp01(1f - Mathf.Abs(_pan) * 1.6f);

            if (lulu == null || lulu.IsAttacking || lulu.Stage < 1 || look <= 0.01f)
            {
                HideFrontCharacter();
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

            ShowCharacter(_frontCharacter, EnemyId.Lulu, pose, FrontCharacterHeight, look);
        }

        private void HideFrontCharacter()
        {
            if (_frontCharacter != null)
            {
                _frontCharacter.gameObject.SetActive(false);
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
            _sideLeftCharacter.gameObject.SetActive(false);
            _sideRightCharacter.gameObject.SetActive(false);
            _monitorCharacter.gameObject.SetActive(false);
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
                _monitorCharacter.gameObject.SetActive(false);
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
        /// 监控画面里叠谁。奶龙优先（它蹲在机位偷电，正对镜头所以用 stare）。
        /// **它一离开机位就不在监控里了** —— 到了右门口它就不再是「蹲机位」的状态，
        /// 这时它属于门口的威胁，玩家得转头看右门、而不是在监控里找它。
        /// </summary>
        private void ApplyMonitorCharacter(GameManager gm)
        {
            var nailong = _enemyDirector != null
                ? _enemyDirector.GetBrain(EnemyId.Nailong) as NailongBrain
                : null;

            if (nailong != null && !nailong.IsAttacking
                && nailong.Stage < nailong.MaxStage
                && nailong.CameraIndex == gm.CurrentCameraIndex)
            {
                ShowCharacter(_monitorCharacter, EnemyId.Nailong, CharacterPose.Stare,
                    MonitorCharacterHeight);
                return;
            }

            // 噜噜改走正门了，不再出现在任何机位里
            _monitorCharacter.gameObject.SetActive(false);
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
        /// 背景与门片都是**整段镜头序列**：[0] 正前方，[1..N] 逐帧转过去。
        /// </summary>
        public void SetArtwork(
            Sprite[] doorLeftClosedFrames, Sprite[] doorRightClosedFrames,
            Sprite[] monitorFrames, CharacterArt[] characters)
        {
            _doorLeftClosedFrames = doorLeftClosedFrames;
            _doorRightClosedFrames = doorRightClosedFrames;
            _monitorFrames = monitorFrames;
            _characters = characters;
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

            BuildBackgroundLayers();
            BuildSideCharacters();
            BuildFrontCharacter();
            BuildDoorLayers();
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

        /// <summary>
        /// 门口立绘：贴在屏幕左右下角，压在门片之下 —— 门关上了就盖住它。
        /// 没有立绘素材时退回该角色的代表色剪影。
        /// </summary>
        private void BuildSideCharacters()
        {
            _sideLeftCharacter = MakeSideCharacter("SideCharacterLeft",
                new Vector2(SideCharacterAnchorLeft, 0f), new Vector2(0.5f, 0f));
            _sideRightCharacter = MakeSideCharacter("SideCharacterRight",
                new Vector2(SideCharacterAnchorRight, 0f), new Vector2(0.5f, 0f));
        }

        /// <summary>
        /// 正前方那个立绘 —— 噜噜从正门来，站在这儿。
        /// 外面套一层 RectMask2D：**桌沿以下的部分要裁掉**，腿才会被桌子挡住。
        /// </summary>
        private void BuildFrontCharacter()
        {
            // 遮罩铺满整块画布，靠 padding 从底部裁 —— 这样立绘自己的锚点
            // 还是按整块画布算，位置不用跟着父物体改。
            var clipGo = new GameObject("FrontCharacterClip",
                typeof(RectTransform), typeof(RectMask2D));
            clipGo.transform.SetParent(_canvas.transform, false);
            StretchToParent(clipGo.GetComponent<RectTransform>());
            _frontCharacterClip = clipGo.GetComponent<RectMask2D>();

            var go = new GameObject("FrontCharacter", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(clipGo.transform, false);

            var rect = go.GetComponent<RectTransform>();
            PlaceRect(rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, FrontCharacterBottom),
                new Vector2(FrontCharacterHeight * 0.7f, FrontCharacterHeight));

            var image = go.GetComponent<Image>();
            image.sprite = _solidSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, 0.9f);

            go.SetActive(false);
            _frontCharacter = image;
            ApplyFrontCharacterClip();
        }

        /// <summary>按当前画布高度重算遮罩裁掉多少 —— 宽屏下画布高度不是 1080，不能写死。</summary>
        private void ApplyFrontCharacterClip()
        {
            if (_frontCharacterClip == null)
            {
                return;
            }

            float bottom = CanvasHeight * FrontCharacterClipBottom;

            if (Mathf.Approximately(bottom, _frontCharacterClipPadding))
            {
                return;
            }

            _frontCharacterClipPadding = bottom;
            _frontCharacterClip.padding = new Vector4(0f, bottom, 0f, 0f);
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

        private Image MakeSideCharacter(string name, Vector2 anchor, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_canvas.transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(620f, SideCharacterHeight);

            var image = go.GetComponent<Image>();
            image.sprite = _solidSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = new Color(0.5f, 0.5f, 0.5f, 0.85f);

            go.SetActive(false);
            return image;
        }

        private void BuildDoorLayers()
        {
            _doorBase = MakeFullScreenImage("DoorBase", Color.white);
            _doorBase.gameObject.SetActive(false);

            _doorBlend = MakeFullScreenImage("DoorBlend", Color.white);
            _doorBlend.gameObject.SetActive(false);
        }

        /// <summary>
        /// 两侧墙上的门 / 灯按钮：门在上、灯在下，与官方一致。
        /// 位置**贴着门框**（屏幕 x = 0.12 / 0.88）—— 门在侧墙上（世界偏航角约 ±107°~128°），
        /// 镜头要转到 pan ≥ 0.72 门才进画面，而鼠标停在按钮上时 |_pan| = 2×0.88−1 = 0.76，
        /// 正好是门框内沿刚进画面的角度 —— 所以按钮就落在门框边上。
        ///
        /// ⚠️ 按钮是**固定屏幕位置**，不是挂在 3D 墙上的 —— 因为镜头跟鼠标，
        /// 鼠标位置同时决定转头量：按钮要是跟着 3D 点走，鼠标一挪过去镜头就跟着转、
        /// 按钮又被甩开，永远追不上（不稳定平衡）。固定屏幕位置才追得上。
        /// 位置和 <see cref="WallButtonFadeStart"/>/<see cref="WallButtonFadeEnd"/> 是一套，
        /// 改一个就得核另一个。
        /// </summary>
        private void BuildWallButtons()
        {
            _leftWallGroup = MakeGroup("WallLeft");
            _doorLeftButton = MakeWallButton(_leftWallGroup.transform, "DoorLeft", "DOOR",
                new Vector2(0.12f, 0.66f), OnDoorLeft);
            _lightLeftButton = MakeWallButton(_leftWallGroup.transform, "LightLeft", "LIGHT",
                new Vector2(0.12f, 0.34f), OnLightLeft);

            _rightWallGroup = MakeGroup("WallRight");
            _doorRightButton = MakeWallButton(_rightWallGroup.transform, "DoorRight", "DOOR",
                new Vector2(0.88f, 0.66f), OnDoorRight);
            _lightRightButton = MakeWallButton(_rightWallGroup.transform, "LightRight", "LIGHT",
                new Vector2(0.88f, 0.34f), OnLightRight);
        }

        private void BuildMonitorLayer()
        {
            _monitorOverlay = MakeFullScreenImage("MonitorOverlay", MonitorFallback, true);

            _monitorLabel = MakeText(_monitorOverlay.transform, "MonitorLabel", "CAM 1B",
                TextAnchor.MiddleCenter, 46, new Color(0.72f, 0.86f, 0.72f));
            StretchToParent(_monitorLabel.rectTransform);

            // 监控画面里的敌人立绘（奶龙蹲在机位里）
            var charGo = new GameObject("MonitorCharacter", typeof(RectTransform), typeof(Image));
            charGo.transform.SetParent(_monitorOverlay.transform, false);

            var charRect = charGo.GetComponent<RectTransform>();
            charRect.anchorMin = new Vector2(0.5f, 0f);
            charRect.anchorMax = new Vector2(0.5f, 0f);
            charRect.pivot = new Vector2(0.5f, 0f);
            charRect.anchoredPosition = new Vector2(0f, 46f);
            charRect.sizeDelta = new Vector2(420f, MonitorCharacterHeight);

            _monitorCharacter = charGo.GetComponent<Image>();
            _monitorCharacter.sprite = _solidSprite;
            _monitorCharacter.preserveAspect = true;
            _monitorCharacter.raycastTarget = false;
            _monitorCharacter.color = Color.white;
            charGo.SetActive(false);

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

        private Image MakeWallButton(Transform parent, string name, string label,
            Vector2 anchor, UnityEngine.Events.UnityAction onClick)
        {
            var button = MakeButton(parent, name, label, anchor, Vector2.zero,
                new Vector2(190f, 62f), onClick);

            return button.GetComponent<Image>();
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
