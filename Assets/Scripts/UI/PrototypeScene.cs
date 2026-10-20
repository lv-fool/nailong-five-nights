using System.Collections.Generic;
using System.IO;
using System.Text;
using NailongFNAF.Audio;
using NailongFNAF.Core;
using NailongFNAF.Gameplay;
using UnityEngine;
using UnityEngine.Video;
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

            /// <summary>
            /// **按房间的专属姿势**：下标 = 机位号，值 = 那张立绘。
            ///
            /// 每个敌人 × 每个房间可以各画一张（比如奶龙在餐厅是「坐椅子上」，
            /// 在舞台是「躲在幕布后」，在储藏室是「蹲在纸箱后」）。
            /// 空着的下标自动退回 <see cref="Idle"/>，所以可以一张一张慢慢补。
            ///
            /// 文件名约定见 <see cref="RoomPoseFileName"/>。
            /// </summary>
            public Sprite[] RoomPoses;

            /// <summary>取某个房间的姿势；没画就退 Idle（再退 Stare）。</summary>
            public Sprite GetRoomPose(int cameraIndex)
            {
                if (RoomPoses != null
                    && cameraIndex >= 0
                    && cameraIndex < RoomPoses.Length
                    && RoomPoses[cameraIndex] != null)
                {
                    return RoomPoses[cameraIndex];
                }

                return Fallback(Idle);
            }

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

        /// <summary>
        /// 监控里敌人立绘整体抬高多少像素。
        /// 目的是让**上半身露在家具遮罩之上** —— 家具（桌子/椅子/纸箱）
        /// 在画面下部约占 27%，不抬高的话整只敌人都会被裁掉（2026-10-18 踩过）。
        /// </summary>
        private const float MonitorCharacterLift = 150f;

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
        /// 跳杀视频在 StreamingAssets 下的目录（相对路径）。
        /// StreamingAssets 是**不压缩、保持原格式**进包体的，适合 mp4 ——
        /// Resources 也能放，但那会把视频塞进序列化资源里，白白多占一份体积。
        /// </summary>
        private const string JumpscareVideoFolder = "Video/Jumpscares";

        /// <summary>
        /// 视频倍速。素材 5.16 秒，**只播前半段**（约 2.58 秒）再乘这个倍速，
        /// 实际 0.74 秒就播完 —— 原作也是一闪而过。
        /// </summary>
        private const float JumpscareVideoSpeed = 3.5f;

        /// <summary>
        /// 跳杀视频播多久（秒）= 前半段时长 ÷ 倍速。
        /// 素材 5.16 秒 → 前半段 2.58 秒 → ÷ 3.5 ≈ 0.74 秒。
        /// </summary>
        private const float JumpscareVideoSeconds = 2.58f / JumpscareVideoSpeed;

        /// <summary>
        /// 水印遮挡块在 1920×1080 视频里的位置（**左下角为原点**，x 向右、y 向上）。
        ///
        /// 生成的素材右下角烧着「千问AI生成」，实测水印占 x 1722~1884、y 41~69，
        /// 四周是纯黑（平均亮度 0.002），所以直接盖一块黑色就抹平了，不用重编码视频。
        /// 这里比实测范围各留一圈余量，防止边缘残留。
        /// </summary>
        private static readonly Rect JumpscareWatermarkRect =
            new Rect(1680f, 20f, 240f, 80f);

        /// <summary>
        /// 跳杀音效在 Resources 下的路径（不含扩展名）。
        /// 用 **mp3 放 Resources** 而不是丢 StreamingAssets 自己解析 ——
        /// Unity 原生支持 mp3 导入，直接 Resources.Load 就是 AudioClip，
        /// 省掉手写解码器（试过 WAV 手写读取，换成 mp3 后没必要了）。
        /// </summary>
        private const string JumpscareSfxPath = "Audio/sfx_jumpscare";

        /// <summary>门口立绘的目标高度（米）。门洞 2.05m，留一点头顶余量。</summary>
        private const float TargetHeightMeters = 1.8f;

        /// <summary>
        /// 正门噜噜往下沉多少米 —— 让桌子挡住它下半身。
        ///
        /// ⚠️ **sprite 的 pivot 在正中间**（立绘 955x1176，pivot 477.5/588），
        /// 所以 transform.position 是**中心点**、不是脚底。踩过这个坑：
        /// 按脚底推算沉了 0.78m，结果只剩头皮露在外面（可见 12.9%）。
        ///
        /// 正确推导（桌子顶 0.80m、立绘高 1.293m、锚点原在 y=1.1）：
        ///   目标：露出顶部 62.8%（对齐旧 UGUI 版裁到的位置）
        ///   顶部 = 0.80 + 1.293 x 0.628 = 1.612m
        ///   中心 = 1.612 - 1.293/2 = 0.966m
        ///   sink = 1.1 - 0.966 = 0.134m
        ///
        /// 换立绘（高度变了）要重算：sink = 1.1 - (0.80 + H*0.628 - H/2)。
        /// </summary>
        private const float LuluSinkMeters = 0.134f;

        /// <summary>
        /// 门口立绘的目标宽度上限（米）。门洞 1.2m，留一点边 ——
        /// 超出就会被门框挡住 / 比门还宽（袋鼠、牛来踩过）。
        /// </summary>
        private const float TargetWidthMeters = 1.05f;

        /// <summary>
        /// 中文字体在 Resources 下的路径（不含扩展名）。
        /// 思源黑体（Source Han Sans CN），SIL OFL 1.1，可随包分发。
        /// 换字体：把同族字体丢进 Assets/Resources/Fonts/，改成同名即可 —— 代码只认这个名字。
        /// </summary>
        private const string FontResourcePath = "Fonts/SourceHanSansCN-Normal";

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
        private const int KitchenCameraIndex = NightStats.KitchenCameraIndex;

        /// <summary>
        /// 每个机位的**房间代号**（用于立绘文件名，如 char_nailong_mon_1b.png）。
        /// 只画关键房间也可以 —— 没图的自动退 Idle。
        /// </summary>
        private static readonly string[] RoomCodes =
        {
            "mon_1a",   // 0  舞台
            "mon_1b",   // 1  餐厅
            "mon_1c",   // 2  海盗湾
            "mon_2a",   // 3  西走廊
            "mon_2b",   // 4  西转角
            "mon_3",    // 5  储藏室
            "mon_4a",   // 6  东走廊
            "mon_4b",   // 7  东转角
            "mon_5",    // 8  后台
            "mon_6",    // 9  厨房（没画面，不用画）
            "mon_7"     // 10 厕所
        };

        /// <summary>机位总数（= 房间代号表长度），供编辑器和测试用。</summary>
        public static int RoomCodeCount
        {
            get { return RoomCodes != null ? RoomCodes.Length : 0; }
        }

        /// <summary>某机位的房间代号；越界返回空串。</summary>
        public static string RoomCode(int cameraIndex)
        {
            if (RoomCodes == null || cameraIndex < 0 || cameraIndex >= RoomCodes.Length)
            {
                return string.Empty;
            }

            return RoomCodes[cameraIndex];
        }

        /// <summary>
        /// 某个角色在某个房间的专属立绘文件名（不含扩展名）。
        /// 例：char_nailong_mon_1b（奶龙在餐厅的姿势）。
        /// </summary>
        public static string RoomPoseFileName(EnemyId id, int cameraIndex)
        {
            string room = RoomCode(cameraIndex);
            if (string.IsNullOrEmpty(room))
            {
                return string.Empty;
            }

            return "char_" + id.Code() + "_" + room;
        }

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

        /// <summary>被敌人突破后，该侧门灯按钮的颜色（几乎全黑，看得出按不动）。</summary>
        private static readonly Color Disabled = new Color(0.06f, 0.06f, 0.06f);

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
        /// 监控画面的**底图**（挂在 overlay 下面单独一层）。
        ///
        /// 为什么不直接画在 overlay 上：底图要能**单独平移 / 缩放**（以后换成分块背景
        /// 得靠这个对齐），而 overlay 同时还是敌人立绘、小地图、机位名的父节点 ——
        /// 缩放 overlay 会把那些一起缩掉。所以拆出一层专门的底图。
        /// </summary>
        private Image _monitorFrame;

        /// <summary>底图缩放后四周露出来的部分用纯黑填，别让办公室画面透上来。</summary>
        private Image _monitorBackdrop;

        /// <summary>
        /// 监控画面里叠的敌人立绘槽位（最多三个）。
        /// 同一机位可能站着不止一个（比如舞台上的袋鼠 + 牛来），
        /// 所以用数组横排，而不是单个 Image。
        /// </summary>
        private Image[] _monitorCharacters;

        /// <summary>
        /// 人工画的遮挡多边形（归一化 0..1，左下原点）。
        /// 由编辑器菜单「奶龙 / 6. 编辑监控遮挡区」生成，
        /// 运行时优先用它 —— 比程序自动算的遮罩准，因为是人按真实家具轮廓画的。
        /// </summary>
        private Dictionary<int, List<OccluderShape>> _occluderPolys;

        /// <summary>每个槽位当前是不是「近档」（近档不参与遮挡裁剪）。</summary>
        private readonly bool[] _slotIsNear = new bool[3];


        private CanvasGroup _monitorMapGroup;
        private Text _monitorLabel;
        private Image[] _camBoxes;
        private Image _monitorHandle;
        private Text _clockText;
        private Text _powerText;
        private Image[] _usageBars;

        /// <summary>永夜模式专属的那一行 HUD（已撑多久 / 还差多少攒一次补给）。</summary>
        private Text _endlessText;

        /// <summary>奶包那一整组（左上角立绘 + 说话框）。</summary>
        private CanvasGroup _naibaoGroup;

        /// <summary>奶包的立绘。</summary>
        private Image _naibaoPortrait;

        /// <summary>奶包正在说的那句话。</summary>
        private Text _naibaoLine;

        /// <summary>已经演到第几句了（跟 <see cref="GameManager.NaibaoCueSerial"/> 比）。</summary>
        private int _naibaoSerial;

        /// <summary>这一轮说话演到什么时候（用 unscaledTime，暂停也能收起）。</summary>
        private float _naibaoHideAt;

        /// <summary>立绘缓存：文件名 → 运行时切出来的 Sprite。</summary>
        private readonly Dictionary<string, Sprite> _naibaoSprites = new Dictionary<string, Sprite>();

        /// <summary>左下角「电量 + Usage」整组。断电后收起来，跟原版一样只剩黑暗。</summary>
        private CanvasGroup _powerGroup;

        private CanvasGroup _debugGroup;
        private Text _debugText;

        /// <summary>小地图上的敌人头像（4 个敌人各一个）。调试时随时显示。</summary>
        private Image[] _debugMapPins;

        /// <summary>调试面板里「每只敌人当前在哪个机位」的文本。</summary>
        private Text[] _debugEnemyPosText;

        /// <summary>电量调试用的显示文本。</summary>
        private Text _debugPowerText;
        private Image _jumpscareOverlay;
        private Image _jumpscareImage;
        private Text _jumpscareText;
        private RawImage _jumpscareVideoImage;
        private VideoPlayer _jumpscareVideo;
        private RenderTexture _jumpscareVideoTarget;
        private bool _jumpscareVideoPlaying;
        private float _jumpscareVideoTimer;
        private Image _jumpscareWatermarkMask;
        private AudioSource _jumpscareAudio;
        private AudioClip _jumpscareSfx;
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

            _font = Resources.Load<Font>(FontResourcePath);
            if (_font == null)
            {
                // 兜底：内置拉丁字体。中文会显示成方块/空白（功能不受影响）。
                // 想显示中文，放一个中文字体到 Assets/Resources/Fonts/ 下，命名见上面的常量。
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
            ReleaseJumpscareVideoTarget();

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

            // 永夜模式的暂停：Esc 开/关面板。暂停期间这一帧就到此为止 ——
            // 时间已经靠 timeScale = 0 冻住了，别的更新再跑也没意义。
            if (HandlePause(gm))
            {
                return;
            }

            HandleDebugKeys();

            // 永夜模式：撑得越久敌人越凶（普通模式什么都不做）
            UpdateEndlessRamp(gm);

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
            UpdateJumpscareVideo();
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

            _enemyDirectorRef = _enemyDirector;   // 给静态辅助方法用
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

            // 面板上有按钮，打开时才接收点击
            _debugGroup.blocksRaycasts = _debugVisible;
            _debugGroup.interactable = _debugVisible;
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

            // 尺寸按**高度和宽度双约束**算，取小的那个缩放系数 ——
            // 只卡高度的话，宽高比大的角色（袋鼠 _idle 0.899、牛来 _stare 1.056）
            // 横向会撑出 1.6~1.9m，比 1.2m 的门洞还宽（2026-10-18 修）。
            //
            // Simple 模式下世界尺寸 = 像素 ÷ PPU × scale，
            // 所以 scale = min(目标高 ÷ 自然高, 目标宽 ÷ 自然宽)。
            float worldH = sprite.rect.height / sprite.pixelsPerUnit;
            float worldW = sprite.rect.width / sprite.pixelsPerUnit;
            if (worldH <= 0.001f || worldW <= 0.001f)
            {
                sr.gameObject.SetActive(false);
                return;
            }

            float s = Mathf.Min(TargetHeightMeters / worldH, TargetWidthMeters / worldW);

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

            if (lulu == null || lulu.IsAttacking || lulu.Stage < lulu.MaxStage)
            {
                _enemyFront.gameObject.SetActive(false);
                return;
            }

            CharacterPose pose;

            // 正被手电筒照着 → 慌。
            // ⚠️ **只算它已经堵到正门口的时候** —— 还在逼近（侧身那张）时照它没用，
            // 立绘当然也不该有反应，不然玩家会以为「闪了但它不走」。
            // 判定跟 LuluBrain 那边保持一致：手电筒亮着 + 视角在正前方 + 没在看监控。
            bool atDoor = true;   // 到这儿必然是 Stage >= MaxStage = 堵在正门口
            bool shiningAtIt = atDoor && gm.FlashlightOn && !gm.MonitorUp
                && gm.CurrentView == ViewSide.Front;

            if (shiningAtIt)
            {
                pose = CharacterPose.Startled;
            }
            else
            {
                // 堵到正门口 → 正对镜头
                pose = CharacterPose.Stare;
            }

            // 噜噜站正门口（前墙门洞 z=3.0）。素材朝左画，正前方直接面朝玩家，不用镜像。
            ShowWorldCharacter(_enemyFront, true, EnemyId.Lulu, pose, Side.Right);

            // **桌子挡住下半身**：以前靠 UI 层的 RectMask2D 裁，
            // 改成 3D 世界物体后遮罩失效，只能用「把立绘往下沉」来模拟。
            // 沉多少见 LuluSinkMeters 的推导（保持露出约 62.8%）。
            _enemyFront.transform.position =
                FrontDoorCenter + new Vector3(0f, -LuluSinkMeters, 0f);
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
                SetMonitorCharacterVisible(img, false);
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

        /// <summary>
        /// 在监控画面上显示某个敌人，**优先用它在当前房间的专属姿势**。
        /// 没画那张就自动退回 <see cref="CharacterPose.Idle"/> —— 所以可以一张一张补图。
        /// </summary>
        /// <summary>
        /// 显示 / 隐藏一个监控敌人（连同它外面的遮罩父节点）。
        /// ⚠️ 不能只开关 Image 本身 —— 父节点的 RectMask2D 还留着的话，
        /// 会挡住别的绘制或留下空节点。
        /// </summary>
        private static void SetMonitorCharacterVisible(Image image, bool visible)
        {
            if (image == null)
            {
                return;
            }

            var parent = image.transform.parent;
            if (parent != null)
            {
                parent.gameObject.SetActive(visible);
            }
            else
            {
                image.gameObject.SetActive(visible);
            }
        }

        private void ShowRoomCharacter(Image image, EnemyId id, int cameraIndex,
            CharacterPose fallbackPose, float height)
        {
            var art = FindCharacter(id);
            Sprite sprite = null;

            if (art != null)
            {
                sprite = art.GetRoomPose(cameraIndex);

                // GetRoomPose 在没专属图时已经退到 Idle；如果连 Idle 都没有，
                // 再退到调用方要的姿态（如噜噜的 Peek）
                if (sprite == null)
                {
                    sprite = art.Get(fallbackPose);
                }
            }

            image.gameObject.SetActive(true);

            if (sprite != null)
            {
                image.preserveAspect = true;
                image.sprite = sprite;
                image.color = Color.white;
                FitHeight(image.rectTransform, sprite, height);
            }
            else
            {
                Color tint = EnemyTint(id);
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
                SetMonitorFrame(_solidSprite, Color.black);
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

            SetMonitorFrame(frame != null ? frame : _solidSprite,
                frame != null ? Color.white : MonitorFallback);
            _monitorLabel.text = CameraName(index);

            // 底图的平移 / 缩放（编辑器里拖过才有）
            ApplyMonitorBackground(index);

            ApplyMonitorCharacter(gm);

            UpdateCameraBoxes(index);
        }

        /// <summary>把画面贴图设到底图那一层（没有底图层的旧场景就退回 overlay 本体）。</summary>
        private void SetMonitorFrame(Sprite sprite, Color color)
        {
            if (_monitorFrame != null)
            {
                _monitorFrame.sprite = sprite;
                _monitorFrame.color = color;
            }
            else
            {
                _monitorOverlay.sprite = sprite;
                _monitorOverlay.color = color;
            }
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

            // ⚠️ **位置和角色绑定，不按「当前有几个」重排**。
            // 以前是按 count 均分（1 个居中 / 2 个 ±130 / 3 个 ±230）——
            // 结果同一间屋里走了一个人，剩下的人会**整个挪一下**，看着像画面在动。
            // 现在每个敌人有固定的站位（按机位号做种子，同一机位永远同一处），
            // 谁走谁留都不影响别人。
            for (int i = 0; i < _monitorCharacters.Length; i++)
            {
                var img = _monitorCharacters[i];

                if (i >= count)
                {
                    SetMonitorCharacterVisible(img, false);
                    continue;
                }

                SetMonitorCharacterVisible(img, true);

                // 用「这个敌人 + 这个房间」的专属姿势，没画就退 Idle
                ShowRoomCharacter(img, ids[i], cam, poses[i], MonitorCharacterHeight);

                var rect = img.rectTransform;

                // 这只在这个机位是近还是远（确定性，不会每帧变）
                int night = _enemyDirector != null ? _enemyDirector.NightIndex : 0;
                bool near = MonitorPrefersNear(ids[i], cam, night);
                _slotIsNear[i] = near;

                // ⚠️ **位置只由「哪个敌人 + 哪个机位」决定，和在场人数无关。**
                //
                // 之前两版都错在同一个地方：拿 i（槽位索引）或 count（在场人数）参与布局。
                // 只要同一间屋子里有人进出，别人的坐标就会跟着变 —— 看着就是画面在动。
                //
                // 现在：给四只各分一条**固定的横向分区**（互不重叠），
                // 每只在自己那条里按机位做一点确定性偏移。谁来谁走都不影响别人。
                Vector2 slot = MonitorSlotPosition(ids[i], cam);

                // 远近两档：
                //   近 —— 站在家具**前面**：放大、位置靠下（离镜头近）、不裁剪
                //   远 —— 站在家具**后面**：缩小、位置靠上（离镜头远）、参与裁剪
                float lift = near
                    ? MonitorCharacterLift
                    : MonitorCharacterLift * MonitorFarLiftRatio;

                // 远档要**靠近遮挡箱**（房间里画了遮挡区的话）——
                // 这样它才真的躲在家具后面，而不是站在旁边的空地上
                float x = near ? slot.x : PullTowardOccluder(slot.x, cam);

                // **编辑器里手拖过的点位优先**：直接定死立绘底边中点，
                // 不再叠 lift、也不再被拉向遮挡箱 —— 你摆哪儿它就站哪儿。
                MonitorPoint custom = FindMonitorPoint(ids[i], cam);
                if (custom != null)
                {
                    var overlayRt = (RectTransform)_monitorOverlay.transform;
                    x = (custom.x - 0.5f) * overlayRt.rect.width;
                    rect.anchoredPosition = new Vector2(x, custom.y * overlayRt.rect.height);
                }
                else
                {
                    rect.anchoredPosition = new Vector2(x, slot.y + lift);
                }

                // ⚠️ **框的宽高比必须和立绘一致**，不能写死 260×380 ——
                // Image 开着 preserveAspect，框比例和精灵不一致时，
                // 精灵会在框里**居中留白**（上下各一条），实际绘制区比框小。
                // 遮挡是拿框的四角算归一化的，框大于实际绘制区 → 采样区被拉长且上移，
                // 表现就是「遮挡偏下」。
                // （踩过：精灵 0.81 宽高比塞进 0.68 的框，实际只有 320 高）
                if (img.sprite != null && img.preserveAspect)
                {
                    FitHeight(rect, img.sprite, MonitorCharacterHeight);
                }
                else
                {
                    rect.sizeDelta = new Vector2(MonitorCharacterHeight * 0.46f, MonitorCharacterHeight);
                }

                float scale = MonitorSlotScale(ids[i], cam);
                scale *= near ? MonitorNearScale : MonitorFarScale;
                if (custom != null && custom.scale > 0.01f)
                {
                    scale *= custom.scale;
                }
                rect.localScale = new Vector3(scale, scale, 1f);
            }

            // 只给「远档」的敌人套遮挡（近档站在家具前面，不裁）
            ApplyOccluderToCharacters(cam);
        }

        /// <summary>

        /// <summary>
        /// 某个机位上**可以站人的位置**（按房间里的道具定，不是网格）。
        ///
        /// 每间屋子的陈设不同，能站的点也不同 ——
        /// 有椅子的地方站椅子旁，有箱子的站箱子旁，走廊就贴墙边。
        /// 坐标是照着 <c>Assets/Art/Monitoring/mon_*.png</c> 的实际构图量的
        /// （画布参考宽 1920，x = 0 是画面正中）。
        ///
        /// ⚠️ **只跟机位有关，跟谁站在这里无关。** 这是「走人不挪」的保证。
        /// </summary>
        private static Vector2[] RoomStandPoints(int cameraIndex)
        {
            switch (cameraIndex)
            {
                // CAM 1A 舞台：台面中央 + 左右黑箱旁 + 右侧台角
                case 0:
                    return new[]
                    {
                        new Vector2(-480f, 32f),
                        new Vector2(-160f, 44f),
                        new Vector2(190f, 40f),
                        new Vector2(490f, 28f)
                    };

                // CAM 1B 餐厅：长桌旁的三把椅子 + 桌侧
                case 1:
                    return new[]
                    {
                        new Vector2(-470f, 56f),
                        new Vector2(-180f, 74f),
                        new Vector2(150f, 68f),
                        new Vector2(460f, 50f)
                    };

                // CAM 1C 海盗湾：台座左中右
                case 2:
                    return new[]
                    {
                        new Vector2(-420f, 46f),
                        new Vector2(-140f, 54f),
                        new Vector2(160f, 48f),
                        new Vector2(430f, 42f)
                    };

                // CAM 2A 西走廊：贴墙，左右各两点
                case 3:
                    return new[]
                    {
                        new Vector2(-430f, 58f),
                        new Vector2(-170f, 52f),
                        new Vector2(160f, 56f),
                        new Vector2(410f, 62f)
                    };

                // CAM 2B 西转角：纸箱旁 + 门框侧
                case 4:
                    return new[]
                    {
                        new Vector2(-440f, 38f),
                        new Vector2(-190f, 52f),
                        new Vector2(140f, 46f),
                        new Vector2(400f, 40f)
                    };

                // CAM 3 储藏室：纸箱堆旁 / 架子前
                case 5:
                    return new[]
                    {
                        new Vector2(-450f, 54f),
                        new Vector2(-170f, 62f),
                        new Vector2(160f, 48f),
                        new Vector2(420f, 44f)
                    };

                // CAM 4A 东走廊
                case 6:
                    return new[]
                    {
                        new Vector2(-420f, 56f),
                        new Vector2(-150f, 50f),
                        new Vector2(170f, 58f),
                        new Vector2(430f, 62f)
                    };

                // CAM 4B 东转角
                case 7:
                    return new[]
                    {
                        new Vector2(-430f, 44f),
                        new Vector2(-160f, 54f),
                        new Vector2(150f, 46f),
                        new Vector2(410f, 40f)
                    };

                // CAM 5 后台：架子前横排
                case 8:
                    return new[]
                    {
                        new Vector2(-440f, 50f),
                        new Vector2(-150f, 58f),
                        new Vector2(160f, 52f),
                        new Vector2(430f, 46f)
                    };

                // CAM 7 厕所：洗手池 / 镜子前
                case 10:
                    return new[]
                    {
                        new Vector2(-430f, 44f),
                        new Vector2(-160f, 52f),
                        new Vector2(150f, 48f),
                        new Vector2(420f, 56f)
                    };

                // CAM 6 厨房只有声音、没有画面 —— 不该有人站在这里
                default:
                    return new[] { new Vector2(0f, 40f) };
            }
        }

        // ---------------- 远近两档 ----------------

        /// <summary>近档：更靠前 —— 立绘放大、**不参与遮挡**（它站在家具前面）。</summary>
        private const float MonitorNearScale = 1.14f;

        /// <summary>远档：更靠里 —— 立绘缩小、**参与遮挡**（被家具挡住下半身）。</summary>
        private const float MonitorFarScale = 0.80f;

        /// <summary>远档的纵向位置系数（越小越靠里/靠上）。</summary>
        private const float MonitorFarLiftRatio = 0.78f;

        /// <summary>
        /// 这只敌人在这个机位上属于「近」还是「远」。
        ///
        /// **确定性哈希**（敌人 + 机位 + 夜数），不是每帧 random：
        /// 位置一旦定了就不再变，否则会「抖动」—— 之前踩过这个坑。
        /// 加进夜数是为了让不同夜晚的构图有变化。
        /// </summary>
        private bool MonitorPrefersNear(EnemyId id, int cameraIndex, int nightIndex)
        {
            int seed = ((int)id * 7919)
                       ^ (cameraIndex * 104729)
                       ^ (nightIndex * 15485863);

            return Hash01(seed ^ 0x2545f491) < 0.5f;
        }

        /// <summary>
        /// 某个敌人在某个机位上的站位。
        ///
        /// **只由「哪个敌人 + 哪个机位」决定，和在场人数完全无关** ——
        /// 别人来不来、走不走都不影响它的坐标（这是「位置不固定」的根治办法）。
        ///
        /// 具体算法：先取该房间的可站点列表，
        /// 再用「敌人身份」选一个固定的点。不同敌人天然分开，
        /// 而且位置都属于真实存在的道具旁边，不是网格里的空位。
        /// </summary>
        public static Vector2 MonitorSlotPosition(EnemyId id, int cameraIndex)
        {
            Vector2[] points = RoomStandPoints(cameraIndex);
            if (points.Length == 0)
            {
                return Vector2.zero;
            }

            // 用敌人身份决定它站哪个点（固定映射，不看别人在不在）。
            // 四只各占一个不同下标，所以同一间屋子最多同时出现四只也不会叠。
            int pick;
            switch (id)
            {
                case EnemyId.Nailong:
                    pick = 1;   // 偏中（奶龙爱往中间凑）
                    break;
                case EnemyId.Daishu:
                    pick = 0;   // 最左
                    break;
                case EnemyId.Niulai:
                    pick = 2;   // 偏右
                    break;
                case EnemyId.Lulu:
                    pick = 3;   // 最右
                    break;
                default:
                    pick = 0;
                    break;
            }

            Vector2 p = points[pick % points.Length];

            // 极小抖动（±10px）：让重复出现的构图不完全一样，
            // 但小到不会离开道具范围，也不会挤到别人的点上
            int seed = ((int)id * 7919) ^ (cameraIndex * 104729);
            p.x += (Hash01(seed) * 2f - 1f) * 10f;
            p.y += (Hash01(seed ^ 0x5bf03635) * 2f - 1f) * 8f;

            return p;
        }

        

        /// <summary>
        /// 让「远档」的敌人**靠近遮挡箱**（房间里画了遮挡区的话）。
        ///
        /// 近档不动 —— 它站在家具前面，本来就该在画面开阔处。
        /// 计算方式：把立绘横向拉向遮挡区的水平中心。
        /// </summary>
        private float PullTowardOccluder(float slotX, int cameraIndex)
        {
            EnsureOccluderPolys();

            List<OccluderShape> polys;
            if (_occluderPolys == null
                || !_occluderPolys.TryGetValue(cameraIndex, out polys)
                || polys == null
                || polys.Count == 0)
            {
                return slotX;   // 这个房间没画遮挡区，不拉
            }

            // 取所有遮挡多边形的横向中心（归一化 0..1 → 画布坐标）
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            foreach (var shape in polys)
            {
                if (shape.Points == null)
                {
                    continue;
                }

                foreach (var p in shape.Points)
                {
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                }
            }

            if (maxX <= minX)
            {
                return slotX;
            }

            var overlayRect = (RectTransform)_monitorOverlay.transform;
            float width = overlayRect.rect.width;
            float boxCenterX = ((minX + maxX) * 0.5f - 0.5f) * width;

            // 往箱子中心靠 60%
            return Mathf.Lerp(slotX, boxCenterX, 0.6f);
        }

        /// <summary>
        /// 把敌人立绘**裁掉**前景道具占据的那一块（不盖色块）。
        ///
        /// 做法：每个敌人立绘外面套一个 RectMask2D 的父节点，
        /// 父节点的有效区域 = 画面减去「桌面以下」，敌人超出桌面的部分自然不画。
        /// 这样露出来的是背景图里真正的桌子，不会有任何额外色块。
        /// </summary>
        /// <summary>
        /// 给每个敌人立绘套上「前景遮挡遮罩」—— 被家具挡住的像素**不画**。
        ///
        /// 遮罩图由 <c>tools/render_monitor.py</c> 渲染监控画面时一并产出
        /// （<c>mon_1b_occl.png</c> 之类），白色 = 家具。
        /// 这样椅子之间的空隙会正确保留，而不是像矩形裁剪那样一刀切平。
        /// </summary>
        /// <summary>遮挡形状的类型。</summary>
        private enum OccluderKind
        {
            Polygon = 0,
            Rect = 1,
            Ellipse = 2,
        }

        /// <summary>
        /// 一块遮挡区域。
        ///
        /// 矩形 / 椭圆也存成 4 个点的包围盒 —— 这样数据格式统一，
        /// 只是栅格化时按 Kind 走不同的填法。
        /// </summary>
        private struct OccluderShape
        {
            public OccluderKind Kind;
            public Vector2[] Points;
        }
        /// <summary>
        /// 读人工画的遮挡多边形（Resources/occluders.json）。
        /// 文件不存在 / 解析失败都静默跳过 —— 那样就退回程序遮罩。
        /// </summary>
        private void EnsureOccluderPolys()
        {
            if (_occluderPolys != null)
            {
                return;
            }

            _occluderPolys = new Dictionary<int, List<OccluderShape>>();

            var asset = Resources.Load<TextAsset>("occluders");
            if (asset == null)
            {
                return;
            }

            try
            {
                var doc = JsonUtility.FromJson<OccluderJson>(asset.text);
                if (doc == null || doc.rooms == null)
                {
                    return;
                }

                foreach (var room in doc.rooms)
                {
                    var list = new List<OccluderShape>();
                    if (room.shapes != null)
                    {
                        foreach (var shape in room.shapes)
                        {
                            if (shape.points == null || shape.points.Length < 6)
                            {
                                continue;
                            }

                            int n = shape.points.Length / 2;
                            var poly = new Vector2[n];
                            for (int i = 0; i < n; i++)
                            {
                                poly[i] = new Vector2(shape.points[i * 2], shape.points[i * 2 + 1]);
                            }

                            list.Add(new OccluderShape
                            {
                                // kind 缺省 0 = 多边形，老数据自动兼容
                                Kind = (OccluderKind)shape.kind,
                                Points = poly,
                            });
                        }
                    }
                    _occluderPolys[room.room] = list;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[奶龙] 遮挡多边形解析失败：" + e.Message);
            }
        }

        // ---------------- 监控画面布局（编辑器「奶龙 / 6. 监控画面编辑器」生成）----------------

        /// <summary>
        /// 每个机位的底图变换 + 敌人点位。
        /// 由编辑器窗口拖动生成，运行时直接套用 —— 不用改代码就能调构图。
        /// 文件不存在就整套退回原来的自动布局，不会报错。
        /// </summary>
        [System.Serializable]
        private class MonitorLayoutDoc
        {
            public MonitorLayoutRoom[] rooms;
        }

        [System.Serializable]
        private class MonitorLayoutRoom
        {
            public int room;

            /// <summary>底图相对屏幕中心的像素偏移。</summary>
            public float bgX;
            public float bgY;

            /// <summary>底图缩放（1 = 铺满）。</summary>
            public float bgScale = 1f;

            public MonitorPoint[] points;
        }

        [System.Serializable]
        private class MonitorPoint
        {
            /// <summary>EnemyId 的整数值（Niulai=0 / Daishu=1 / Lulu=2 / Nailong=3）。</summary>
            public int enemy;

            /// <summary>归一化坐标，左下原点：x = 立绘底边中点，y = 底边距画面底部。</summary>
            public float x = 0.5f;
            public float y = 0.06f;

            /// <summary>额外缩放（1 = 原尺寸）。</summary>
            public float scale = 1f;
        }

        private Dictionary<int, MonitorLayoutRoom> _monitorLayout;

        private void EnsureMonitorLayout()
        {
            if (_monitorLayout != null)
            {
                return;
            }

            _monitorLayout = new Dictionary<int, MonitorLayoutRoom>();

            var asset = Resources.Load<TextAsset>("monitor_layout");
            if (asset == null)
            {
                return;
            }

            try
            {
                var doc = JsonUtility.FromJson<MonitorLayoutDoc>(asset.text);
                if (doc == null || doc.rooms == null)
                {
                    return;
                }

                foreach (var room in doc.rooms)
                {
                    if (room != null)
                    {
                        _monitorLayout[room.room] = room;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[奶龙] 监控布局解析失败：" + e.Message);
            }
        }

        /// <summary>取某个敌人在某个机位上手动摆的点位；没配就返回 null（走自动布局）。</summary>
        private MonitorPoint FindMonitorPoint(EnemyId id, int cameraIndex)
        {
            EnsureMonitorLayout();

            MonitorLayoutRoom room;
            if (_monitorLayout == null
                || !_monitorLayout.TryGetValue(cameraIndex, out room)
                || room == null
                || room.points == null)
            {
                return null;
            }

            foreach (var p in room.points)
            {
                if (p != null && p.enemy == (int)id)
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>把底图的平移 / 缩放套到画面上。没配该机位就复位。</summary>
        private void ApplyMonitorBackground(int cameraIndex)
        {
            if (_monitorFrame == null)
            {
                return;
            }

            EnsureMonitorLayout();

            var rt = _monitorFrame.rectTransform;

            MonitorLayoutRoom room;
            if (_monitorLayout != null && _monitorLayout.TryGetValue(cameraIndex, out room) && room != null)
            {
                float s = room.bgScale <= 0.01f ? 1f : room.bgScale;
                rt.anchoredPosition = new Vector2(room.bgX, room.bgY);
                rt.localScale = new Vector3(s, s, 1f);
            }
            else
            {
                rt.anchoredPosition = Vector2.zero;
                rt.localScale = Vector3.one;
            }
        }

        /// <summary>多边形遮罩纹理缓存（按机位）。</summary>
        private readonly Dictionary<int, Texture2D> _polyMaskCache = new Dictionary<int, Texture2D>();

        /// <summary>
        /// 把人工画的多边形**栅格化成遮罩纹理**（白 = 挡住）。
        /// 没有该机位的多边形就返回 null，调用方退回程序遮罩。
        ///
        /// 纹理只生成一次，之后缓存 —— 每帧重算太浪费。
        /// </summary>
        private Texture2D GetPolygonMaskTexture(int cameraIndex)
        {
            Texture2D cached;
            if (_polyMaskCache.TryGetValue(cameraIndex, out cached))
            {
                return cached;
            }

            List<OccluderShape> polys;
            if (_occluderPolys == null
                || !_occluderPolys.TryGetValue(cameraIndex, out polys)
                || polys == null
                || polys.Count == 0)
            {
                _polyMaskCache[cameraIndex] = null;
                return null;
            }

            // ⚠️ 分辨率直接决定**切割边缘的精细度**。
            // 立绘在屏幕上约 264px 宽，只采样遮罩的一小块（约 0.1 宽）；
            // 512 宽时那一小块只有 ~54 个遮罩像素去铺 264 个屏幕像素，
            // 边缘会明显发块（锯齿状台阶）。
            // 用和原图一致的 1920×1080 → 约 1:1，边缘干净。
            const int W = 1920;
            const int H = 1080;

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;

            // ⚠️ **必须 Point**：Bilinear 会在多边形边界插值出 0~1 的中间值，
            // 结果是被挡住的边缘呈半透明（看起来像玻璃）。
            // 这张图是「遮 / 不遮」的二值数据，不需要平滑。
            tex.filterMode = FilterMode.Point;

            var px = new Color32[W * H];
            var clear = new Color32(0, 0, 0, 255);
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = clear;
            }

            var white = new Color32(255, 255, 255, 255);

            // **扫描线填充**：逐行求多边形与扫描线的交点，排序后成对填充。
            // 复杂度 O(H × 边数)，比逐像素 point-in-polygon 快几十倍 ——
            // 所以敢上 1920×1080（否则 200 万像素 × 每个顶点会明显卡）。
            var xs = new List<float>(16);

            foreach (var shape in polys)
            {
                var poly = shape.Points;
                if (poly == null || poly.Length < 3)
                {
                    continue;
                }

                if (shape.Kind == OccluderKind.Rect || shape.Kind == OccluderKind.Ellipse)
                {
                    // 矩形 / 椭圆只存包围盒的四角，用前两个对角点即可
                    FillBox(px, W, H, poly, shape.Kind == OccluderKind.Ellipse);
                    continue;
                }

                for (int y = 0; y < H; y++)
                {
                    float ny = (y + 0.5f) / H;
                    xs.Clear();

                    for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    {
                        float yi = poly[i].y;
                        float yj = poly[j].y;

                        // 半开区间，避免顶点被算两次
                        if ((yi > ny) == (yj > ny))
                        {
                            continue;
                        }

                        float t = (ny - yi) / (yj - yi);
                        xs.Add(poly[i].x + t * (poly[j].x - poly[i].x));
                    }

                    if (xs.Count < 2)
                    {
                        continue;
                    }

                    xs.Sort();

                    int row = y * W;
                    for (int k = 0; k + 1 < xs.Count; k += 2)
                    {
                        int x0 = Mathf.Clamp(Mathf.CeilToInt(xs[k] * W - 0.5f), 0, W - 1);
                        int x1 = Mathf.Clamp(Mathf.FloorToInt(xs[k + 1] * W - 0.5f), 0, W - 1);

                        for (int x = x0; x <= x1; x++)
                        {
                            px[row + x] = white;
                        }
                    }
                }
            }

            tex.SetPixels32(px);
            tex.Apply();

            _polyMaskCache[cameraIndex] = tex;
            return tex;
        }

        /// <summary>
        /// 把「包围盒」填成矩形或椭圆。
        ///
        /// 矩形 / 椭圆在数据里只存四个角（包围盒），所以这里取对角两点的
        /// 最小 / 最大即可，不依赖点的顺序。
        /// 椭圆用归一化后的 (dx/a)² + (dy/b)² <= 1 判定。
        /// </summary>
        private static void FillBox(Color32[] px, int w, int h, Vector2[] corners, bool ellipse)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var p in corners)
            {
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }

            int x0 = Mathf.Clamp(Mathf.FloorToInt(minX * w), 0, w - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX * w), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(minY * h), 0, h - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY * h), 0, h - 1);

            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            float rx = Mathf.Max((maxX - minX) * 0.5f, 1e-5f);
            float ry = Mathf.Max((maxY - minY) * 0.5f, 1e-5f);

            var white = new Color32(255, 255, 255, 255);

            for (int y = y0; y <= y1; y++)
            {
                float ny = (y + 0.5f) / h;
                int row = y * w;

                for (int x = x0; x <= x1; x++)
                {
                    if (ellipse)
                    {
                        float nx = (x + 0.5f) / w;
                        float dx = (nx - cx) / rx;
                        float dy = (ny - cy) / ry;

                        if (dx * dx + dy * dy > 1f)
                        {
                            continue;
                        }
                    }

                    px[row + x] = white;
                }
            }
        }
        /// <summary>射线法判断点是否在多边形内。</summary>
        private static bool PointInPolygon(Vector2[] poly, float x, float y)
        {
            bool inside = false;
            int n = poly.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 pi = poly[i];
                Vector2 pj = poly[j];
                if (((pi.y > y) != (pj.y > y))
                    && (x < (pj.x - pi.x) * (y - pi.y) / (pj.y - pi.y + 1e-9f) + pi.x))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        [System.Serializable]
        private class OccluderJson
        {
            public OccluderRoomJson[] rooms;
        }

        [System.Serializable]
        private class OccluderRoomJson
        {
            public int room;
            public OccluderShapeJson[] shapes;
        }

        [System.Serializable]
        private class OccluderShapeJson
        {
            /// <summary>0=多边形 1=矩形 2=椭圆（缺省 0，兼容老数据）</summary>
            public int kind;

            public float[] points;
        }

        private void ApplyOccluderToCharacters(int cameraIndex)
        {
            if (_monitorCharacters == null)
            {
                return;
            }

            // 优先用**人工画的多边形**（更准），没有才退回程序生成的遮罩图
            EnsureOccluderPolys();

            Sprite mask = null;
            Texture2D polyTex = GetPolygonMaskTexture(cameraIndex);
            if (polyTex != null)
            {
                mask = Sprite.Create(polyTex, new Rect(0, 0, polyTex.width, polyTex.height),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            // 自动遮罩（render_monitor.py 输出的 mon_xx_occl.png）已停用 ——
            // 现在**只用人工画的多边形**（菜单「奶龙 / 6. 编辑监控遮挡区」）。
            // 没画就完全不遮挡。

            for (int slot = 0; slot < _monitorCharacters.Length; slot++)
            {
                var img = _monitorCharacters[slot];
                if (img == null)
                {
                    continue;
                }

                var mat = img.material;
                if (mat == null)
                {
                    continue;
                }

                // ⚠️ **近档不裁剪** —— 它站在家具前面，被裁掉就穿帮了
                bool clipThis = mask != null
                    && slot < _slotIsNear.Length
                    && !_slotIsNear[slot];

                if (clipThis)
                {
                    mat.SetTexture("_Occluder", mask.texture);
                    mat.SetFloat("_UseOccluder", 1f);

                    // ⚠️ 这里**不要**再改 filterMode ——
                    // 多边形遮罩纹理自己设的是 Point（硬边），
                    // 早先这行把 Bilinear 写回去，覆盖了 Point，
                    // 结果遮挡边缘又变成半透明（2026-10-18 踩过）。

                    // 把立绘在**监控画面**里的归一化矩形传进去，
                    // shader 用它把立绘 uv 映射到遮罩图的对应区域。
                    //
                    // 用世界角点反算，而不是拼 anchoredPosition ——
                    // 立绘外面套着 mask 父节点、pivot 又在底部，纯算偏移会错。
                    //
                    // ⚠️ 基准必须是**监控图本身占的那块区域**，不是 overlay：
                    // overlay 是 2241x925（铺满画布），而监控图是 16:9 的 1920x1080，
                    // 两者宽高比不同，直接按 overlay 算会让采样坐标整体偏掉。
                    var imgRect = img.rectTransform;
                    var overlayRect = (RectTransform)_monitorOverlay.transform;

                    // 监控图在 overlay 里的**实际显示区域**。
                    // 图是 16:9，overlay 是 2241x925（宽高比 2.42:1），
                    // 所以图会被拉伸铺满 —— 遮罩和背景图用的是同一张 1920x1080 内容，
                    // 两者在 overlay 里占的是**同一块区域**，直接按 overlay 归一化即可。
                    //
                    // （早先担心的「宽高比不一致」其实不存在：拉伸对背景和遮罩
                    //   是同时发生的，所以它们的归一化坐标仍然一一对应。）
                    // ⚠️ 必须用 **InverseTransformPoint 转进 overlay 的本地空间**，
                    // 不能拿世界坐标相减再除以 rect.size ——
                    // Canvas 有缩放系数（CanvasScaler），世界单位 ≠ 本地单位，
                    // 混用会算出**偏小且偏移**的矩形，遮挡就跑到画面别处去了
                    // （2026-10-18 踩过：算出来 0.05 宽，实际应该是 0.09）。
                    Vector3[] corners = new Vector3[4];
                    imgRect.GetWorldCorners(corners);

                    Rect local = overlayRect.rect;

                    Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                    Vector2 max = new Vector2(float.MinValue, float.MinValue);
                    for (int ci = 0; ci < 4; ci++)
                    {
                        Vector3 p = overlayRect.InverseTransformPoint(corners[ci]);
                        min.x = Mathf.Min(min.x, p.x);
                        min.y = Mathf.Min(min.y, p.y);
                        max.x = Mathf.Max(max.x, p.x);
                        max.y = Mathf.Max(max.y, p.y);
                    }

                    // 本地坐标 → 0..1（以 overlay 左下角为原点）
                    float u0 = (min.x - local.xMin) / local.width;
                    float du = (max.x - min.x) / local.width;

                    // 纵向：UI 的 y 向上、纹理 v 也向上（v=0 是纹理**底行**），
                    // 而且立绘 sprite 的 texcoord.y = 0 对应它的**底边**。
                    //
                    // shader 里 suv.y = v0 + texcoord.y * dv，所以：
                    //   texcoord.y=0（立绘底）→ 要采到遮罩的「立绘底所在高度」
                    //   ⇒ v0 必须是**底边**的归一化高度，不是顶边的！
                    //
                    // ⚠️ 早先这里写成 1-topNorm（顶边），整个采样区被抬高了一个 dv，
                    // 结果角色完全采不到家具那块 —— 遮挡看着像"没生效"（2026-10-18）。
                    float bottomNorm = (min.y - local.yMin) / local.height;
                    float topNorm = (max.y - local.yMin) / local.height;
                    float dv = topNorm - bottomNorm;
                    float v0 = bottomNorm;

                    mat.SetVector("_OccluderRect", new Vector4(u0, v0, du, dv));
                }
                else
                {
                    mat.SetFloat("_UseOccluder", 0f);
                }
            }
        }

        /// <summary>某个敌人在某机位上的固定大小系数（0.88~1.06，确定性）。</summary>
        private static float MonitorSlotScale(EnemyId id, int cameraIndex)
        {
            int seed = ((int)id * 31337) ^ (cameraIndex * 48611) ^ 0x2f1b3c5d;
            return Mathf.Lerp(0.88f, 1.06f, Hash01(seed));
        }

        /// <summary>整数种子 → [0,1) 的确定性伪随机（不用 Unity 的 Random，避免污染全局状态）。</summary>
        private static float Hash01(int seed)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return (x & 0xFFFFFF) / 16777216f;
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
            // 被敌人突破的那一侧：门灯按钮变暗、失效 —— 照 FNAF 1 的绝望感，
            // 玩家看着按钮但按不动。
            bool leftDead = gm.IsSideDisabled(Side.Left);
            bool rightDead = gm.IsSideDisabled(Side.Right);

            _doorLeftButton.color = leftDead ? Disabled : (gm.LeftDoorClosed ? DoorOn : DoorOff);
            _doorRightButton.color = rightDead ? Disabled : (gm.RightDoorClosed ? DoorOn : DoorOff);
            _lightLeftButton.color = leftDead ? Disabled : (gm.LeftLightOn ? LightOn : LightOff);
            _lightRightButton.color = rightDead ? Disabled : (gm.RightLightOn ? LightOn : LightOff);

            // LIGHT 光晕：灯亮 → 亮绿呼吸（径向渐变淡入淡出），灯灭 → 熄灭。
            // 呼吸只动 alpha 和 scale，不动颜色。
            float breathe = 0.82f + Mathf.Sin(Time.time * 2.4f) * 0.18f;
            ApplyLightGlow(_lightLeftGlow, gm.LeftLightOn && !leftDead, breathe);
            ApplyLightGlow(_lightRightGlow, gm.RightLightOn && !rightDead, breathe);
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
        // 跳杀：优先播 StreamingAssets 里的 mp4（3.5 倍速、只播前半段），
        // 起不来就退静态惊吓图 —— 永远不会黑屏。
        // ---------------------------------------------------------------

        private void OnEnemyAttacked(EnemyId id)
        {
            _hasJumpscare = true;
            _jumpscareEnemy = id;
            _jumpscareTime = 0f;

            _jumpscareOverlay.gameObject.SetActive(true);
            _jumpscareOverlay.color = Color.black;

            // 优先播视频；起不来（缺素材 / 解码失败）就走静态图兜底
            if (TryPlayJumpscareVideo(id))
            {
                return;
            }

            ShowStaticJumpscare(id);
        }

        /// <summary>
        /// 静态跳杀图兜底。视频缺失、解码失败、平台不支持时都走这条 ——
        /// **跳杀永远不会变成黑屏**。
        /// </summary>
        private void ShowStaticJumpscare(EnemyId id)
        {
            StopJumpscareVideo();

            var art = FindCharacter(id);

            // 走兜底链：没有专属跳杀图就退到 Stare（正面那张）——
            // 比全屏红 + 大字强，也免得给的正脸图白放着用不上
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
            StopJumpscareVideo();
            StopJumpscareSfx();
            _jumpscareOverlay.gameObject.SetActive(false);
        }

        /// <summary>
        /// 掐掉跳杀音效。
        /// ⚠️ 音效有 3 秒，比视频（0.74 秒）长得多 —— 跳杀结束、重开一夜、
        /// 退回菜单这些时候必须主动停，否则会拖到下一局还在响。
        /// </summary>
        private void StopJumpscareSfx()
        {
            if (_jumpscareAudio != null && _jumpscareAudio.isPlaying)
            {
                _jumpscareAudio.Stop();
            }
        }

        // ---------------------------------------------------------------
        // 跳杀视频
        //
        // 素材在 Assets/Video/Jumpscares/char_<代号>_jumpscare.mp4（StreamingAssets）。
        // **视频缺失、解码失败、平台不支持 —— 一律静默退回静态跳杀图**，
        // 所以这一层永远不会把跳杀变成黑屏。
        // ---------------------------------------------------------------

        /// <summary>跳杀视频的 StreamingAssets 相对路径。代号见 <see cref="EnemyIds.Code"/>。</summary>
        private static string JumpscareVideoPath(EnemyId id)
        {
            return JumpscareVideoFolder + "/char_" + id.Code() + "_jumpscare.mp4";
        }

        /// <summary>
        /// 尝试起播跳杀视频。成功返回 true（并已铺好视频层）；
        /// 素材缺失 / 起不来返回 false，交给调用方走静态图兜底。
        /// </summary>
        private bool TryPlayJumpscareVideo(EnemyId id)
        {
            string path = Path.Combine(Application.streamingAssetsPath, JumpscareVideoPath(id));
            if (!File.Exists(path))
            {
                return false;
            }

            EnsureJumpscareVideo();
            if (_jumpscareVideo == null || _jumpscareVideoTarget == null)
            {
                return false;
            }

            _jumpscareVideoImage.gameObject.SetActive(true);
            _jumpscareImage.gameObject.SetActive(false);
            _jumpscareText.gameObject.SetActive(false);

            // 起播前先占位成黑：视频第一帧解码出来之前别露出白色 RawImage
            _jumpscareVideoImage.color = Color.black;

            _jumpscareVideoPlaying = true;
            _jumpscareVideoTimer = 0f;

            // 视频里烧着水印 —— 用一块黑盖掉（四周本来就是纯黑，看不出来）
            if (_jumpscareWatermarkMask != null)
            {
                _jumpscareWatermarkMask.gameObject.SetActive(true);
            }

            PlayJumpscareSfx();

            // 异步准备：好了在 prepareCompleted 里 Play，坏了在 errorReceived 里退回静态图
            _jumpscareVideo.url = path;
            _jumpscareVideo.Prepare();

            return true;
        }

        /// <summary>视频层 + VideoPlayer 只建一次，之后反复用。</summary>
        private void EnsureJumpscareVideo()
        {
            if (_jumpscareVideo != null)
            {
                return;
            }

            // 播到一张 RenderTexture 上，再用 RawImage 铺满屏幕 ——
            // 视频不能像 Sprite 那样直接塞进 UGUI。
            var target = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            target.Create();
            _jumpscareVideoTarget = target;

            var imageGo = new GameObject("JumpscareVideo", typeof(RectTransform), typeof(RawImage));
            imageGo.transform.SetParent(_jumpscareOverlay.transform, false);

            var rect = imageGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            // 按画布裁：视频是 16:9，画布不是的时候宁可切边也不留黑边
            rect.sizeDelta = new Vector2(JumpscareWidth, CanvasHeight);

            _jumpscareVideoImage = imageGo.GetComponent<RawImage>();
            _jumpscareVideoImage.texture = target;
            _jumpscareVideoImage.raycastTarget = false;
            imageGo.SetActive(false);

            // 水印遮挡块：纯黑，盖在视频右下角。
            // 视频是 1920×1080，RawImage 按 sizeDelta 铺开 —— 这里按比例算位置，
            // 画布不是 16:9 时也能跟住（视频被裁边，遮罩跟着一起裁）。
            var maskGo = new GameObject("JumpscareWatermarkMask",
                typeof(RectTransform), typeof(Image));
            maskGo.transform.SetParent(imageGo.transform, false);

            var maskRect = maskGo.GetComponent<RectTransform>();
            maskRect.anchorMin = new Vector2(
                JumpscareWatermarkRect.x / 1920f,
                JumpscareWatermarkRect.y / 1080f);
            maskRect.anchorMax = new Vector2(
                (JumpscareWatermarkRect.x + JumpscareWatermarkRect.width) / 1920f,
                (JumpscareWatermarkRect.y + JumpscareWatermarkRect.height) / 1080f);
            maskRect.offsetMin = Vector2.zero;
            maskRect.offsetMax = Vector2.zero;

            var mask = maskGo.GetComponent<Image>();
            mask.sprite = _solidSprite;
            mask.color = Color.black;
            mask.raycastTarget = false;
            _jumpscareWatermarkMask = mask;
            maskGo.SetActive(false);

            // 跳杀音效：单独一条轨，别占环境音 / 音效的 source
            var audioGo = new GameObject("JumpscareAudio");
            audioGo.transform.SetParent(transform, false);
            var audio = audioGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;
            audio.volume = 0.9f;
            _jumpscareAudio = audio;

            var player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.isLooping = false;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            player.targetTexture = target;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.aspectRatio = VideoAspectRatio.Stretch;
            // 起播回调：有画面了就把黑占位换掉
            player.prepareCompleted += OnJumpscareVideoPrepared;
            player.errorReceived += OnJumpscareVideoError;

            _jumpscareVideo = player;
        }

        private void OnJumpscareVideoPrepared(VideoPlayer source)
        {
            if (_jumpscareVideoImage == null || !_jumpscareVideoPlaying)
            {
                return;
            }

            _jumpscareVideoImage.color = Color.white;
            _jumpscareVideoTimer = 0f;

            // 倍速 + 只播前半段：播到一半就掐掉
            source.playbackSpeed = JumpscareVideoSpeed;
            source.Play();
        }

        /// <summary>视频出问题（文件损坏 / 解码器不支持）→ 立刻退回静态图。</summary>
        private void OnJumpscareVideoError(VideoPlayer source, string message)
        {
            if (!_jumpscareVideoPlaying)
            {
                return;
            }

            Debug.LogWarning("[奶龙] 跳杀视频起不来，退回静态图：" + message);
            ShowStaticJumpscare(_jumpscareEnemy);
        }

        /// <summary>把视频停下来并清屏，免得下一次跳杀卡在上一帧。</summary>
        private void StopJumpscareVideo()
        {
            _jumpscareVideoPlaying = false;
            _jumpscareVideoTimer = 0f;

            if (_jumpscareVideo != null && _jumpscareVideo.isPlaying)
            {
                _jumpscareVideo.Stop();
            }

            if (_jumpscareVideoImage != null)
            {
                _jumpscareVideoImage.gameObject.SetActive(false);
            }

            if (_jumpscareWatermarkMask != null)
            {
                _jumpscareWatermarkMask.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 播跳杀音效。素材放在 <c>Resources/Audio/sfx_jumpscare</c>（mp3 / wav 都行，
        /// Unity 会导入成 AudioClip）。缺失时静默跳过，不影响跳杀本身。
        /// </summary>
        private void PlayJumpscareSfx()
        {
            if (_jumpscareAudio == null)
            {
                return;
            }

            if (_jumpscareSfx == null)
            {
                // 只加载一次，之后缓存住
                _jumpscareSfx = Resources.Load<AudioClip>(JumpscareSfxPath);
                if (_jumpscareSfx == null)
                {
                    return;
                }
            }

            _jumpscareAudio.Stop();
            _jumpscareAudio.clip = _jumpscareSfx;
            _jumpscareAudio.Play();
        }

        /// <summary>计时：播满 JumpscareVideoSeconds 就掐掉，然后接着抖。</summary>
        private void UpdateJumpscareVideo()
        {
            if (!_jumpscareVideoPlaying)
            {
                return;
            }

            // 还没准备好（Prepare 未完成）不计时 —— 保证完整播满 2.5 秒
            if (_jumpscareVideo == null || !_jumpscareVideo.isPlaying)
            {
                return;
            }

            _jumpscareVideoTimer += Time.deltaTime;
            if (_jumpscareVideoTimer >= JumpscareVideoSeconds)
            {
                StopJumpscareVideo();
            }
        }

        /// <summary>退出时把 RenderTexture 放掉，不然它会一直占着显存。</summary>
        private void ReleaseJumpscareVideoTarget()
        {
            if (_jumpscareVideoTarget == null)
            {
                return;
            }

            _jumpscareVideoTarget.Release();
            Destroy(_jumpscareVideoTarget);
            _jumpscareVideoTarget = null;
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

            // 视频在放的时候不再抖静态图 —— 让视频自己动（计时在 Update 里）
            if (_jumpscareVideoPlaying)
            {
                return;
            }

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
            // 普通模式显示「几点」；永夜模式小时数不封顶，只显示这一夜的第几点（夜数另有提示）
            _clockText.text = FormatHour(gm.EndlessMode ? gm.Clock.HourOfNight : gm.Clock.CurrentHour);
            _powerText.text = "Power left: " + Mathf.CeilToInt(gm.Power.Level) + "%";
            UpdateUsageBars(gm.Power.UsageLevel);
            UpdateEndlessHud(gm);
            UpdateNaibao(gm);

            // 断电了就没有「还剩多少电、用了几格」这回事了
            _powerGroup.alpha = gm.HasPowerOut ? 0f : 1f;

            _debugText.text = BuildDebugInfo(gm);

            if (_debugPowerText != null)
            {
                _debugPowerText.text = "当前 " + Mathf.CeilToInt(gm.Power.Level) + "%";
            }

            UpdateDebugMapPins();
            UpdateDebugEnemyPositions();
        }

        /// <summary>
        /// 奶包说话：她每来一次，<see cref="GameManager.NaibaoCueSerial"/> 就 +1。
        /// 这里靠 serial 变化判断「这是新的一句」，换了就把立绘和台词换掉、亮起来；
        /// 台词按这次动作的时长再留一会儿，超时淡出。
        /// </summary>
        private void UpdateNaibao(GameManager gm)
        {
            if (_naibaoGroup == null)
            {
                return;
            }
            bool alive = gm.Naibao != null && !gm.HasBeenCaught;
            if (!alive)
            {
                _naibaoGroup.alpha = 0f;
                return;
            }

            if (gm.NaibaoCueSerial != _naibaoSerial)
            {
                _naibaoSerial = gm.NaibaoCueSerial;
                ShowNaibaoCue(gm.LastNaibaoCue);
            }

            float target = Time.unscaledTime < _naibaoHideAt ? 1f : 0f;
            _naibaoGroup.alpha = Mathf.MoveTowards(_naibaoGroup.alpha, target,
                Time.unscaledDeltaTime * 4f);
        }

        /// <summary>把一句奶包台词摆到屏幕上：换姿势、换台词、定什么时候收。</summary>
        private void ShowNaibaoCue(NaibaoCue cue)
        {
            if (cue.Line == null)
            {
                return;
            }

            if (_naibaoLine != null)
            {
                _naibaoLine.text = cue.Line;
            }

            if (_naibaoPortrait != null)
            {
                Sprite sprite = NaibaoSprite(cue.Pose);
                _naibaoPortrait.sprite = sprite;
                _naibaoPortrait.enabled = sprite != null;
            }

            // 有持续时间的动作（抢监控、偷加耗电）多留一会儿，纯说话就留 6 秒
            float hold = cue.Seconds > 0f ? cue.Seconds + 3.5f : 6f;
            _naibaoHideAt = Time.unscaledTime + hold;
        }

        /// <summary>
        /// 立绘走 <c>Resources.Load&lt;Texture2D&gt;</c> 而不是 <c>Load&lt;Sprite&gt;</c>：
        /// 这些 PNG 是按普通贴图导入的，没有 Sprite 元数据，直接 Load&lt;Sprite&gt; 会拿到 null。
        /// 切出来的 Sprite 缓存住，别每句台词都新建一个。
        /// </summary>
        private Sprite NaibaoSprite(NaibaoPose pose)
        {
            string file = NaibaoPoseFile(pose);
            Sprite cached;
            if (_naibaoSprites.TryGetValue(file, out cached))
            {
                return cached;
            }

            var texture = Resources.Load<Texture2D>("Naibao/" + file);
            Sprite sprite = null;
            if (texture != null)
            {
                texture.wrapMode = TextureWrapMode.Clamp;
                sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            }

            _naibaoSprites[file] = sprite;
            return sprite;
        }

        private static string NaibaoPoseFile(NaibaoPose pose)
        {
            switch (pose)
            {
                case NaibaoPose.Guide:
                    return "char_naibao_guide";
                case NaibaoPose.Huffy:
                    return "char_naibao_huffy";
                case NaibaoPose.Smug:
                    return "char_naibao_smug";
                case NaibaoPose.Bored:
                    return "char_naibao_bored";
                default:
                    return "char_naibao_peek";
            }
        }

        /// <summary>
        /// 永夜模式那一行：撑了多久、下次补给还有几秒 / 预计补多少、当前危险度、刚补了多少。
        /// 普通模式整行隐藏（永夜模式才有的东西，别在五夜流程里多一行噪音）。
        /// </summary>
        private void UpdateEndlessHud(GameManager gm)
        {
            if (_endlessText == null)
            {
                return;
            }

            var recharge = gm.Recharge;
            if (!gm.EndlessMode || recharge == null || gm.Clock == null)
            {
                if (_endlessText.gameObject.activeSelf)
                {
                    _endlessText.gameObject.SetActive(false);
                }

                return;
            }

            if (!_endlessText.gameObject.activeSelf)
            {
                _endlessText.gameObject.SetActive(true);
            }

            float hours = gm.Clock.ElapsedSeconds / Mathf.Max(1f, gm.Clock.SecondsPerHour);
            int danger = EndlessDifficulty.TierFor(hours, recharge.Noise) + 1;
            bool overload = EndlessDifficulty.OverdriveFor(hours, recharge.Noise) > 0f;

            var sb = new StringBuilder();
            sb.Append("永夜 · 已撑 ").Append(gm.Clock.CurrentHour).Append(" 小时");
            sb.Append(" · ").Append(Mathf.CeilToInt(recharge.SecondsToNext)).Append(" 秒后补 ");
            sb.Append(Mathf.RoundToInt(recharge.NextGain)).Append("%");
            sb.Append(" · 危险 ").Append(danger).Append("/").Append(NightCount);
            if (overload)
            {
                sb.Append("（过载）");
            }

            // 刚补完电的 1.6 秒内，把「+8%」挂在后面，让玩家知道刚才那一下有回报。
            // 电量本来就在 99~100%，一次结算可能只补进零点几个百分点 —— 四舍五入成 0
            // 的话就别显示「+0%」了，看着像 bug。
            int gained = Mathf.RoundToInt(gm.LastGain);
            if (gm.LastGainAge < 1.6f && gained > 0)
            {
                sb.Append(" · +").Append(gained).Append("%");
            }

            _endlessText.text = sb.ToString();
        }

        /// <summary>
        /// 把四个敌人的当前机位画到小地图上（调试用）。
        /// 坐标直接复用 <see cref="MapCamButtons"/> 的像素框 —— 和玩家看到的地图完全对齐。
        /// </summary>
        private void UpdateDebugMapPins()
        {
            if (_debugMapPins == null || _enemyDirector == null)
            {
                return;
            }

            bool visible = _debugVisible;
            for (int i = 0; i < _debugMapPins.Length; i++)
            {
                if (_debugMapPins[i] != null && _debugMapPins[i].transform.parent.gameObject.activeSelf != visible)
                {
                    _debugMapPins[i].transform.parent.gameObject.SetActive(visible);
                }
            }

            if (!visible || _monitorMapGroup == null)
            {
                return;
            }

            foreach (EnemyId id in EnemyIds.All)
            {
                var brain = _enemyDirector.GetBrain(id);
                if (brain == null)
                {
                    continue;
                }

                int pinIndex = PinIndexOf(id);
                if (pinIndex < 0 || pinIndex >= _debugMapPins.Length)
                {
                    continue;
                }

                int cam = DebugCameraOf(brain);
                Image pin = _debugMapPins[pinIndex];

                if (cam < 0)
                {
                    // 不在任何机位（在门口 / 断电阶段）—— 收起来
                    pin.gameObject.SetActive(false);
                    continue;
                }

                Vector2 local;
                if (!TryMapCameraLocal(cam, out local))
                {
                    pin.gameObject.SetActive(false);
                    continue;
                }

                pin.gameObject.SetActive(true);

                // 头像 = 该角色的**正脸立绘**（Stare），一眼认得出是谁
                Sprite face = FaceSpriteOf(id);
                if (face != null)
                {
                    pin.sprite = face;
                    pin.preserveAspect = true;
                }
                else
                {
                    pin.sprite = _solidSprite;
                    pin.color = PinColor(id);
                }

                // 同一机位站了多只时横向错开，不然叠成一个
                int sameCamIndex = 0;
                foreach (EnemyId other in EnemyIds.All)
                {
                    if (other == id)
                    {
                        break;
                    }

                    var ob = _enemyDirector.GetBrain(other);
                    if (ob != null && DebugCameraOf(ob) == cam)
                    {
                        sameCamIndex++;
                    }
                }

                float spread = sameCamIndex * 22f;
                pin.rectTransform.sizeDelta = new Vector2(34f, 34f);
                pin.rectTransform.anchoredPosition = local + new Vector2(spread, 0f);
            }
        }

        /// <summary>小地图头像用的正脸立绘。缺图返回 null（退回色块）。</summary>
        private Sprite FaceSpriteOf(EnemyId id)
        {
            var art = FindCharacter(id);
            return art != null ? art.Get(CharacterPose.Stare) : null;
        }

        private static int PinIndexOf(EnemyId id)
        {
            switch (id)
            {
                case EnemyId.Niulai: return 0;
                case EnemyId.Daishu: return 1;
                case EnemyId.Lulu: return 2;
                case EnemyId.Nailong: return 3;
                default: return -1;
            }
        }

        /// <summary>这个敌人当前在哪个机位上（-1 = 不在任何机位）。</summary>
        private static int DebugCameraOf(EnemyBrain brain)
        {
            var nailong = brain as NailongBrain;
            if (nailong != null)
            {
                // 到门口就没机位了
                if (nailong.IsAttacking || nailong.Stage >= nailong.MaxStage)
                {
                    return -1;
                }

                return nailong.IsApproaching ? nailong.PathCameraIndex : nailong.CameraIndex;
            }

            var door = brain as DoorEnemyBrain;
            if (door != null)
            {
                return door.IsAttacking ? -1 : door.PathCameraIndex;
            }

            var lulu = brain as LuluBrain;
            if (lulu != null)
            {
                return lulu.IsAttacking ? -1 : lulu.PathCameraIndex;
            }

            return -1;
        }

        /// <summary>机位号 → 小地图面板上的本地坐标（直接取 CAM 框的中心）。</summary>
        private static bool TryMapCameraLocal(int cameraIndex, out Vector2 local)
        {
            for (int i = 0; i < MapCamButtons.Length; i++)
            {
                if (MapCamButtons[i].CameraIndex == cameraIndex)
                {
                    local = MapCamButtons[i].Center;
                    return true;
                }
            }

            local = Vector2.zero;
            return false;
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

            // overlay 本身只当**容器**用，别再往上画东西（否则底图一缩放，
            // 敌人立绘、小地图、机位名会跟着缩）。底色压黑即可。
            _monitorOverlay.color = Color.black;

            // 1) 纯黑垫底：底图缩小 / 平移后露出来的地方必须是黑的，
            //    否则会透出办公室画面，看着像"监控没铺满"。
            _monitorBackdrop = MakeFullScreenImage("MonitorBackdrop", Color.black, false);
            _monitorBackdrop.transform.SetParent(_monitorOverlay.transform, false);
            StretchToParent(_monitorBackdrop.rectTransform);
            _monitorBackdrop.transform.SetAsFirstSibling();

            // 2) 底图：可单独平移 / 缩放（以后换分块背景靠它对齐）
            _monitorFrame = MakeFullScreenImage("MonitorFrame", Color.white, false);
            _monitorFrame.transform.SetParent(_monitorOverlay.transform, false);
            StretchToParent(_monitorFrame.rectTransform);
            _monitorFrame.transform.SetSiblingIndex(1);

            _monitorLabel = MakeText(_monitorOverlay.transform, "MonitorLabel", "CAM 1B",
                TextAnchor.MiddleCenter, 46, new Color(0.72f, 0.86f, 0.72f));
            StretchToParent(_monitorLabel.rectTransform);

            // 监控画面里的敌人立绘 —— 三个槽位，同一机位可以站多个
            _monitorCharacters = new Image[3];

            for (int i = 0; i < _monitorCharacters.Length; i++)
            {
                // 每个敌人外面套一层**空父节点**（只有 RectTransform，**没有 RectMask2D**）。
                //
                // ⚠️ **千万不要加 RectMask2D** —— 它是 IMaterialModifier，
                // UGUI 的 materialForRendering 遇到它会换成模板材质，
                // **直接忽略我们自定义的 shader**，遮挡完全不生效。
                // 表现是「开着遮挡但敌人一个像素都没少」（2026-10-18 踩了很久）。
                //
                // 现在的裁剪全靠 MonitorCharacter.shader 逐像素 clip，不需要 RectMask2D。
                var maskGo = new GameObject("MonitorCharacterMask" + i,
                    typeof(RectTransform));
                maskGo.transform.SetParent(_monitorOverlay.transform, false);

                // 保险：场景里若残留旧的 RectMask2D（早期版本建的），一并清掉
                var staleMask = maskGo.GetComponent<RectMask2D>();
                if (staleMask != null)
                {
                    DestroyImmediate(staleMask);
                }

                var maskRect = maskGo.GetComponent<RectTransform>();
                maskRect.anchorMin = new Vector2(0.5f, 0f);
                maskRect.anchorMax = new Vector2(0.5f, 0f);
                maskRect.pivot = new Vector2(0.5f, 0f);
                maskRect.anchoredPosition = Vector2.zero;
                maskRect.sizeDelta = new Vector2(1920f, MonitorCharacterHeight + 120f);

                var charGo = new GameObject("MonitorCharacter" + i,
                    typeof(RectTransform), typeof(Image));
                charGo.transform.SetParent(maskGo.transform, false);

                var charRect = charGo.GetComponent<RectTransform>();
                charRect.anchorMin = new Vector2(0.5f, 0f);
                charRect.anchorMax = new Vector2(0.5f, 0f);
                charRect.pivot = new Vector2(0.5f, 0f);
                // ⚠️ 立绘底部要**高于家具遮罩的顶边**，否则整个人都被家具挡住。
                // 遮罩白区在画面下部约 27%（从底部算 0.106~0.379 归一化），
                // overlay 高 925 → 遮罩顶边约在 350px 处。
                // 把立绘底放在 300 左右：下半截被桌子挡住，上半身露出来。
                charRect.anchoredPosition = new Vector2(0f, 300f);
                charRect.sizeDelta = new Vector2(260f, MonitorCharacterHeight);

                var img = charGo.GetComponent<Image>();
                img.sprite = _solidSprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
                img.color = Color.white;

                // 用支持「遮挡遮罩」的材质 —— 每个槽位一份，互相独立
                var shader = Shader.Find("NailongFNAF/MonitorCharacter");
                if (shader != null)
                {
                    img.material = new Material(shader);
                }

                _monitorCharacters[i] = img;
                maskGo.SetActive(false);
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

            // 永夜模式那一行（普通模式直接隐藏）：摆在电量条正上方
            _endlessText = MakeText(hud.transform, "EndlessText", string.Empty,
                TextAnchor.LowerLeft, 24, new Color(0.75f, 0.88f, 0.75f));
            PlaceRect(_endlessText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(40f, 150f), new Vector2(760f, 34f));
            _endlessText.gameObject.SetActive(false);

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

            BuildNaibao();
        }

        /// <summary>
        /// 奶包：左上角一张立绘 + 一个说话框。
        ///
        /// 位置选左上不是随便挑的：时钟在右上、电量与 Usage 在左下、
        /// 监控小地图在右下、手电筒在底中，四个角只剩这里是空的 ——
        /// 摆这儿不挡门灯按钮，也不挡手电筒和小地图。
        /// </summary>
        private void BuildNaibao()
        {
            var go = new GameObject("Naibao", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_canvas.transform, false);
            StretchToParent(go.GetComponent<RectTransform>());

            _naibaoGroup = go.GetComponent<CanvasGroup>();
            _naibaoGroup.alpha = 0f;
            _naibaoGroup.blocksRaycasts = false;

            // 说话框：深色半透明底，字在主色上才读得清
            var box = new GameObject("NaibaoLine",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            box.transform.SetParent(go.transform, false);
            PlaceRect(box.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(292f, -46f), new Vector2(700f, 196f));

            var boxImage = box.GetComponent<Image>();
            boxImage.sprite = _buttonSprite;
            boxImage.color = new Color(0.04f, 0.05f, 0.07f, 0.86f);
            boxImage.raycastTarget = false;

            Text nameLabel = MakeText(box.transform, "NaibaoName", "奶包",
                TextAnchor.UpperLeft, 24, new Color(1f, 0.84f, 0.42f));
            PlaceRect(nameLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -12f), new Vector2(140f, 30f));

            _naibaoLine = MakeText(box.transform, "NaibaoText", string.Empty,
                TextAnchor.UpperLeft, 26, new Color(0.94f, 0.94f, 0.94f));
            _naibaoLine.horizontalOverflow = HorizontalWrapMode.Wrap;
            PlaceRect(_naibaoLine.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -48f), new Vector2(660f, 144f));

            // 立绘：竖长条，贴左上角
            var portrait = new GameObject("NaibaoPortrait",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            portrait.transform.SetParent(go.transform, false);
            PlaceRect(portrait.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(30f, -18f), new Vector2(244f, 530f));

            _naibaoPortrait = portrait.GetComponent<Image>();
            _naibaoPortrait.preserveAspect = true;
            _naibaoPortrait.raycastTarget = false;
            _naibaoPortrait.enabled = false;
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
                new Vector2(40f, 300f), new Vector2(1500f, 200f));

            BuildDebugControls(go.transform);
            BuildDebugMapPins();
        }

        /// <summary>
        /// 调试面板下半部分：一排「一键跳杀」按钮 + 电量调节。
        /// 坐标全用画布比例锚点（画布尺寸是运行时定的，别写死像素）。
        /// </summary>
        private void BuildDebugControls(Transform parent)
        {
            var panel = new GameObject("DebugControls", typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            var panelRect = (RectTransform)panel.transform;
            // 面板放进一个带深色底的容器 —— 否则调试文字会和 HUD（电量/时间）
            // 叠在一起，互相看不清。位置行那块尤其容易压到 "Power left"。
            PlaceRect(panelRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(40f, -60f), new Vector2(1100f, 420f));

            var bg = new GameObject("DebugBg", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            bg.transform.SetParent(panel.transform, false);
            var bgRect = (RectTransform)bg.transform;
            PlaceRect(bgRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(1100f, 420f));
            var bgImg = bg.GetComponent<Image>();
            bgImg.sprite = _buttonSprite;
            bgImg.color = new Color(0.05f, 0.06f, 0.08f, 0.86f);
            bgImg.raycastTarget = false;
            bg.transform.SetAsFirstSibling();   // 垫在最底下

            // ── 一键跳杀 ──
            Text jumpLabel = MakeLabel(panel.transform, "跳杀：", 18,
                new Color(0.85f, 0.55f, 0.55f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(4f, 24f), new Vector2(64f, 40f));
            jumpLabel.alignment = TextAnchor.MiddleLeft;

            string[] names = { "牛来", "袋鼠", "噜噜", "奶龙" };
            EnemyId[] order =
            {
                EnemyId.Niulai, EnemyId.Daishu, EnemyId.Lulu, EnemyId.Nailong
            };

            for (int i = 0; i < order.Length; i++)
            {
                EnemyId id = order[i];
                var btnGo = new GameObject("JumpBtn" + i,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                btnGo.transform.SetParent(panel.transform, false);

                var btnRect = (RectTransform)btnGo.transform;
                PlaceRect(btnRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(80f + i * 118f, 24f), new Vector2(110f, 40f));

                var img = btnGo.GetComponent<Image>();
                img.sprite = _buttonSprite;
                img.color = new Color(0.45f, 0.13f, 0.13f, 0.95f);

                var btn = btnGo.GetComponent<Button>();
                btn.targetGraphic = img;
                EnemyId captured = id;
                btn.onClick.AddListener(delegate { DebugTriggerJumpscare(captured); });

                Text t = MakeLabel(btnGo.transform, names[i], 17, Color.white,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(110f, 40f));
                t.raycastTarget = false;
            }

            // ── 电量一键设定 ──
            Text powerLabel = MakeLabel(panel.transform, "电量：", 18,
                new Color(0.65f, 0.85f, 0.65f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(4f, -38f), new Vector2(64f, 36f));
            powerLabel.alignment = TextAnchor.MiddleLeft;

            int[] levels = { 100, 75, 50, 25, 5, 0 };
            for (int i = 0; i < levels.Length; i++)
            {
                int lv = levels[i];
                var btnGo = new GameObject("PowerBtn" + i,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                btnGo.transform.SetParent(panel.transform, false);

                var btnRect = (RectTransform)btnGo.transform;
                PlaceRect(btnRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(80f + i * 78f, -38f), new Vector2(70f, 36f));

                var img = btnGo.GetComponent<Image>();
                img.sprite = _buttonSprite;
                // 电量越低越红
                float k = lv / 100f;
                img.color = new Color(0.55f - k * 0.3f, 0.28f + k * 0.35f, 0.24f, 0.95f);

                var btn = btnGo.GetComponent<Button>();
                btn.targetGraphic = img;
                int captured = lv;
                btn.onClick.AddListener(delegate { DebugSetPower(captured); });

                Text t = MakeLabel(btnGo.transform, lv + "%", 16, Color.white,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(70f, 36f));
                t.raycastTarget = false;
            }

            _debugPowerText = MakeLabel(panel.transform, string.Empty, 18,
                new Color(0.85f, 0.85f, 0.6f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(560f, -38f), new Vector2(260f, 36f));
            _debugPowerText.alignment = TextAnchor.MiddleLeft;

            // ── 敌人位置 ──
            // 标签只用两个字 —— 原先写「敌人位置：」宽 90，
            // 和后面 x=56 的 ◀ 按钮压在一起，"位置" 两个字被盖住了。
            Text posLabel = MakeLabel(panel.transform, "位置：", 17,
                new Color(0.6f, 0.8f, 0.95f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(4f, -104f), new Vector2(52f, 36f));
            posLabel.alignment = TextAnchor.MiddleLeft;

            BuildDebugEnemyPositionRows(panel.transform, -104f);
        }

        /// <summary>
        /// 敌人位置控制：四行 × 「← 机位 →」，把敌人一键搬到指定监控。
        ///
        /// 调试时最常做的事就是「把某只摆到某间屋子看效果」，
        /// 靠 AI 自己走要等几十秒，还不一定走得对。
        /// </summary>
        private void BuildDebugEnemyPositionRows(Transform parent, float startY)
        {
            EnemyId[] order =
            {
                EnemyId.Niulai, EnemyId.Daishu, EnemyId.Lulu, EnemyId.Nailong
            };
            string[] names = { "牛来", "袋鼠", "噜噜", "奶龙" };

            _debugEnemyPosText = new Text[order.Length];

            for (int i = 0; i < order.Length; i++)
            {
                EnemyId id = order[i];
                float y = startY - i * 42f;

                // 名字
                Text name = MakeLabel(parent, names[i], 16,
                    new Color(0.75f, 0.82f, 0.9f),
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(4f, y), new Vector2(52f, 36f));
                name.alignment = TextAnchor.MiddleLeft;

                // ← 上一个机位
                EnemyId capturedPrev = id;
                MakeDebugButton(parent, "◀", 56f, y, 34f, 34f,
                    delegate { DebugMoveEnemy(capturedPrev, -1); });

                // 当前机位名（只读显示）
                Text pos = MakeLabel(parent, "-", 15, Color.white,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(94f, y), new Vector2(210f, 34f));
                pos.alignment = TextAnchor.MiddleCenter;
                _debugEnemyPosText[i] = pos;

                // → 下一个机位
                EnemyId capturedNext = id;
                MakeDebugButton(parent, "▶", 310f, y, 34f, 34f,
                    delegate { DebugMoveEnemy(capturedNext, +1); });
            }
        }

        /// <summary>调试面板上的一个小按钮。</summary>
        private void MakeDebugButton(Transform parent, string label,
            float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("DbgBtn",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            PlaceRect(rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(x, y), new Vector2(w, h));

            var img = go.GetComponent<Image>();
            img.sprite = _buttonSprite;
            img.color = new Color(0.22f, 0.30f, 0.42f, 0.95f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            Text t = MakeLabel(go.transform, label, 18, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(w, h));
            t.raycastTarget = false;
        }

        /// <summary>这只敌人的固定巡逻路径（反射读 _cameraPath，调试专用）。</summary>
        private static int[] CameraPathOf(EnemyId id)
        {
            var dir = _enemyDirectorRef;
            if (dir == null)
            {
                return null;
            }

            var brain = dir.GetBrain(id);
            if (brain == null)
            {
                return null;
            }

            // 沿继承链找 _cameraPath（DoorEnemyBrain 和 LuluBrain 各有一份）
            for (var ty = brain.GetType(); ty != null; ty = ty.BaseType)
            {
                var f = ty.GetField("_cameraPath",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly);

                if (f != null)
                {
                    return f.GetValue(brain) as int[];
                }
            }

            return null;
        }

        /// <summary>_enemyDirector 的静态副本，给静态辅助方法用。</summary>
        private static EnemyDirector _enemyDirectorRef;
        /// <summary>把敌人挪到相邻的上/下一个机位（跳过厨房那种没画面的）。</summary>
        private void DebugMoveEnemy(EnemyId id, int step)
        {
            if (_enemyDirector == null)
            {
                return;
            }

            var brain = _enemyDirector.GetBrain(id);
            if (brain == null)
            {
                return;
            }

            // ⚠️ 每只敌人有**自己的固定路径**（FNAF 原作设定）：
            //   牛来 [0,1,10,6,7]  袋鼠 [0,1,5,3,4]  噜噜 [2,1]  奶龙 [1,6,7]
            // 所以「下一个机位」必须按**它自己的路径**走，
            // 不能按全局 CameraNames 下标自增 —— 那样会跳到它根本不去的地方，
            // DebugTeleport 反查失败就只能原地不动，看起来像按钮没反应。
            int[] path = CameraPathOf(id);
            if (path == null || path.Length == 0)
            {
                return;
            }

            int cur = DebugCameraOf(brain);
            int idx = System.Array.IndexOf(path, cur);

            int next;
            if (idx < 0)
            {
                // 当前不在路径上（比如刚被重置）：从路径头/尾开始
                next = step > 0 ? path[0] : path[path.Length - 1];
            }
            else
            {
                int ni = (idx + step) % path.Length;
                if (ni < 0)
                {
                    ni += path.Length;
                }
                next = path[ni];
            }

            brain.DebugTeleport(next);
        }

        /// <summary>刷新每只敌人的「当前机位」显示。</summary>
        private void UpdateDebugEnemyPositions()
        {
            if (_debugEnemyPosText == null || _enemyDirector == null)
            {
                return;
            }

            EnemyId[] order =
            {
                EnemyId.Niulai, EnemyId.Daishu, EnemyId.Lulu, EnemyId.Nailong
            };

            for (int i = 0; i < order.Length && i < _debugEnemyPosText.Length; i++)
            {
                Text t = _debugEnemyPosText[i];
                if (t == null)
                {
                    continue;
                }

                var brain = _enemyDirector.GetBrain(order[i]);
                int cam = brain != null ? DebugCameraOf(brain) : -1;

                if (cam < 0)
                {
                    t.text = "（门口）";
                    continue;
                }

                // 显示「第几个 / 共几个」+ 机位名 ——
                // 每只敌人只走自己那条路径，光给机位名会让人以为按钮坏了
                int[] path = CameraPathOf(order[i]);
                string camName = CameraNames[cam].Replace("    ", " ");
                if (path != null && path.Length > 0)
                {
                    int pos = System.Array.IndexOf(path, cam) + 1;
                    t.text = "[" + pos + "/" + path.Length + "] " + camName;
                }
                else
                {
                    t.text = camName;
                }
            }
        }
        /// <summary>
        /// 小地图上的敌人头像：调试时四个敌人的当前位置一眼可见。
        /// 独立一层，不依赖监控是否升起 —— 调试就是要随时能看到。
        /// </summary>
        private void BuildDebugMapPins()
        {
            _debugMapPins = new Image[4];

            // 挂在**小地图**下面 —— 头像坐标用的是小地图本地坐标
            var layer = new GameObject("DebugMapPins", typeof(RectTransform));
            layer.transform.SetParent(_monitorMapGroup.transform, false);
            var layerRect = (RectTransform)layer.transform;

            // ⚠️ 必须和 mapGroup 用**同一个 pivot**，否则 spec.Center 会对不齐：
            // mapGroup 的 pivot 是 (1,0)（右下角），UI 的本地原点就在那儿。
            // 默认 StretchToParent 会把 pivot 留在 (0.5,0.5)，头像整体偏半屏。
            var mapRect = (RectTransform)_monitorMapGroup.transform;
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = Vector2.zero;
            layerRect.offsetMax = Vector2.zero;
            layerRect.pivot = mapRect.pivot;
            layerRect.SetAsLastSibling();

            for (int i = 0; i < _debugMapPins.Length; i++)
            {
                var go = new GameObject("Pin" + i,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(layer.transform, false);

                var rect = (RectTransform)go.transform;
                PlaceRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(26f, 26f));

                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                img.color = Color.white;
                _debugMapPins[i] = img;
            }

            layer.SetActive(false);
        }

        private static Color PinColor(EnemyId id)
        {
            switch (id)
            {
                case EnemyId.Niulai:
                    return new Color(1f, 0.55f, 0.15f);   // 橙
                case EnemyId.Daishu:
                    return new Color(1f, 0.92f, 0.25f);   // 黄
                case EnemyId.Lulu:
                    return new Color(0.45f, 0.9f, 0.45f); // 绿
                case EnemyId.Nailong:
                    return new Color(0.95f, 0.35f, 0.85f); // 粉紫
                default:
                    return Color.white;
            }
        }

        private void DebugTriggerJumpscare(EnemyId id)
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            ClearJumpscare();
            gm.MarkCaught();
            OnEnemyAttacked(id);
        }

        private void DebugSetPower(int percent)
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.Power != null)
            {
                gm.Power.SetLevel(percent);
            }
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
