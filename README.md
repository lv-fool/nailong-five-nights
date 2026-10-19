# 奶龙的五夜后宫

**Nailong: Five Nights** —— 一款 FNAF（Five Nights at Freddy's）式的第一人称生存恐怖游戏。
Unity 2022.3 LTS + C#，只做 Windows，中文界面。

> A FNAF-style first-person survival horror game built with Unity 2022.3 LTS and C#.
> Windows only, Chinese UI. You cannot move: you survive by turning your head, watching
> the camera feeds, closing doors and flashing a torch at whatever is creeping up on you.

![标题页](docs/images/title.png)

## 玩法

玩家坐在一家披萨店的保安室里，**不能移动**。每夜从 12 点撑到 6 点（约 8 分 30 秒），
期间电量从 99% 持续下降，归零即死。

- **转头** —— 鼠标在屏幕哪儿镜头就朝哪儿，推到两侧边缘（各约 30%）满转 **70°**
- **门 / 灯** —— 点两侧墙上的 **DOOR** / **LIGHT** 开关。**开着灯才看得见门外站着谁**，
  门关着也看不见。门是点一下切换
- **监控** —— 鼠标移到屏幕底部边缘升起；点右侧小地图的 CAM 框切机位。
  照官方平面图做满 **11 个**机位，厨房（CAM 6）只有声音、没有画面
- **手电筒** —— 按住屏幕底部中间的 **FLASHLIGHT** 才亮（松手就灭），
  用来照从**正门**摸进来的噜噜

界面按官方摆放：时钟在右上角，电量与 Usage 五格条在左下角。

![办公室](docs/images/office.png)

## 四种模式

标题页五个按钮：**故事模式 / 自定义夜 / 永夜模式 / 成就 / 退出**。

| 模式 | 是什么 | 解锁条件 |
|---|---|---|
| **故事模式** | 第 1~5 夜 + 隐藏夜（第 6 夜），一夜 12 点到 6 点，撑过即通关；解锁进度存在 `nailong.unlockedNight` | 一直开着 |
| **自定义夜** | 照 FNAF 1 的第 7 夜：四只敌人的活动度各自由玩家调 **0 ~ 20**，设置会记住 | 通关**隐藏夜**后解锁 |
| **永夜模式** | 撑不到天亮，敌人一轮轮来；靠操作换电，时间与动静一起把难度顶上去（细则见下） | 一直开着 |
| **成就** | 9 条成就的清单，进度存在 `nailong.achv.*` | 一直开着 |

成就清单（`Assets/Scripts/Gameplay/AchievementCatalog.cs`）：
初来乍到（通关第 1 夜）、渐入佳境（第 3 夜）、五夜老兵（第 5 夜）、隐藏夜征服者、
省电达人（通关时剩余电量 ≥ 30%）、黑暗中的幸运儿（断电后仍撑到 6 点）、
闪光灯大师（单夜用手电筒逼退噜噜 3 次）、永夜常客（永夜一局撑过 12 小时）、
终极挑战（自定义夜四只全 20 通关）。

## 四个敌人

各管一种手段，边路照 FNAF 1 原版分配：

| 敌人 | 从哪来 | 怎么挡 | 对标 |
|---|---|---|---|
| 袋鼠 `daishu` | 左门 | 关门 | 邦尼 Bonnie |
| 牛来 `niulai` | 右门 | 关门 | 奇卡 Chica |
| 奶龙 `nailong` | 右门（与牛来共用） | 关门；偷电时切监控看它一眼即止 | 弗莱迪 Freddy（仅攻击路线） |
| 噜噜 `lulu` | **正门** | **手电筒照它**，闪够 4 次逼退 | 2 代狐狸 Withered Foxy |

**奶龙是唯一会两头使坏的**：它蹲在机位上偷电，每偷到一次电就转化成门类敌人、沿右门那条路推进一步；
被门挡回来又回去偷电，如此循环。不管它，它迟早自己站到门口。

**断电之后它还会来「收电费」** —— 照原版分三段：2 秒纯黑 → 它从左门现形、
八音盒的托雷多进行曲响起、脸跟着拍子一亮一暗 → 音乐停下、彻底黑掉 → 动手。整段 18 秒。

## 永夜模式

一句需求：撑不到天亮，但别让玩家干等着 —— 补给得跟操作挂钩，难度得一直往上走。

![永夜模式](docs/images/eternal-night.png)

**补给：固定节奏结算**

- 每过一个游戏小时（85 秒）结算一次，不到点不动账
- 这次补多少 = `(4 + 本小时记点 × 0.75) × 0.9^第几次结算`，夹在 `[1%, 24%]`
- 记点权重：门 1.0 > 灯 0.9 > 监控 0.7 > 手电筒 0.6；
  **同一小时里同类操作越点越不值钱**（第 n 次 ÷ `1 + (n-1) × 0.5`）
- 两层递减叠在一起：这一小时干得多就补得多，但每小时能换到的电越来越少 —— 这一局总有尽头

**难度：时间 + 动静**

- 开局就是**第 3 夜**的强度；每撑 **1.5 小时**加一档；**动静**每满 10 点再加一档（最多加 3 档）
- 动静来自操作：门 2 / 灯 1.5 / 监控 1 / 手电筒 0.8，停手后每秒散 0.1
- 爬到第 6 夜还没死？不再换夜，改为给所有敌人**加压**（每档 +25%，上限 +120%）

**暂停与存档**

- 按 **Esc** 开暂停面板：继续游戏 / 保存进度 / 保存并退出 / 放弃本局。
  暂停靠 `Time.timeScale = 0` + `AudioListener.pause`，时间、电量、敌人、音频一起冻住；
  **故事模式不给暂停**，免得把时钟停住刷时间
- 存档一行文本进 `PlayerPrefs` 的 `nailong.eternalSave`：已撑秒数、电量、结算次数、
  本周期的记点、动静、周期进度、累计补电、操作数、小时数
- 只存「时间 + 电量 + 账本」，**不存敌人状态** —— 读档后敌人从当前难度的初始状态重新开始
- 有存档时标题页那颗按钮变成「继续永夜（N 小时）」；死亡自动删档

![暂停](docs/images/pause.png)

## 操作键位

| 操作 | 键 |
|---|---|
| 转头 | 鼠标移动（或推到屏幕边缘） |
| 升起 / 收起监控 | 鼠标移到屏幕底部 / 点 MONITOR 条 |
| 切机位 | 点右侧小地图的 CAM 框，或数字键 |
| 关门 / 开灯 | 点墙上的 DOOR / LIGHT |
| 手电筒 | 按住屏幕底部中间 FLASHLIGHT |
| 暂停（**仅永夜模式**） | **Esc** |

调试快捷键（官方没有，留着调参用）：**1~9 / 0 / -** 切 11 个机位、**R** 重开一夜、
**[ / ]** 换夜、**F1** 显示 / 隐藏调试面板。

## 现在到哪一步了

- [x] 核心逻辑：电量系统、夜晚时钟、永夜模式补电与存档（**123 个 EditMode 单元测试**）
- [x] 四个敌人的 AI（四条独立状态机 + 逐夜难度表）
- [x] **设计定稿**（`docs/04-设计决策.md`）
- [x] 可玩原型：标题页 → 故事 / 自定义夜 / 永夜 / 成就 → 办公室 → 结算
- [x] 界面与操作对齐官方 FNAF 1（墙上门灯按钮、电量 + Usage 条、升起式监控、小地图）
- [x] 断电演出（照原版三段式）
- [x] 四个敌人立绘、10 张监控画面、跳杀视频 4 条（H.264 / 1920×1080）
- [x] 办公室：程序渲的圆柱全景 + 相机真转
- [x] 永夜模式：固定每小时结算、操作换电、动静抬难度、Esc 暂停、存档续跑
- [x] 自定义夜（全 20 挑战）与 9 条成就
- [x] 正式打包（Windows Mono，`Builds/`，263 MB / zip 116 MB，见 `docs/09-打包.md`）
- [ ] 真实音频素材 —— 环境音 / 操作音效仍是开发期占位音
- [ ] IL2CPP 打包（本机只装了 Mono 变体，见 `docs/09-打包.md`）

## 怎么跑起来

需要 **Unity 2022.3.62f3c1**（2022.3 LTS）。

1. 用 Unity Hub 打开本目录
2. 菜单栏会出现 **「奶龙」**，先跑 **「4. 搭建可玩原型」**（往当前场景放好原型入口）
3. 按 **Play**

| 菜单项 | 作用 |
|---|---|
| 1. 初始化项目设置 | 分辨率、色彩空间、公司 / 产品名 |
| 2. 生成主场景骨架 | 相机 + UI 画布 + 事件系统 |
| 3. 检查并补全目录 | 缺哪个目录补哪个 |
| **4. 搭建可玩原型** | 往当前场景放好原型入口，按 Play 就能玩 |
| **5. 接入素材** | 把 `Assets/Art`、`Assets/Audio` 里的素材接到场景上（换素材后重跑一次） |
| 9. 加入运行时 Shader | 把内置 shader 固化进包体（修发行版黑屏） |
| **10. 打包 Windows 版** | 出 `Builds/Windows/`，可直接发人（见 `docs/09-打包.md`） |

## 怎么跑测试

电量、时钟、敌人 AI、永夜记账、自定义夜配置、成就存档这些核心逻辑都是**纯 C# 类**，
不引用 UnityEngine（少数只借 `PlayerPrefs`），所以能脱离 Unity 测。

- **编辑器里**：Window → General → Test Runner → EditMode → Run All
- **没人看着的时候**（改完代码先跑这个）：

  ```powershell
  & "F:\Unity\2022.3.62f3c1\Editor\Unity.exe" -batchmode -nographics `
    -projectPath "F:\nailong f" `
    -runTests -testPlatform EditMode `
    -testResults "F:\nailong f\.setup\editmode-result.xml" `
    -logFile "F:\nailong f\.setup\unity-test.log"
  ```

- **验证画面**（开窗口跑一遍、自动截图、自动退出）：

  ```powershell
  & "F:\Unity\2022.3.62f3c1\Editor\Unity.exe" -projectPath "F:\nailong f" `
    -playSmoke -executeMethod NailongFNAF.EditorTools.PlaySmoke.Run `
    -logFile "F:\nailong f\.setup\play.log"
  ```

  截图落在 `.setup/play/`。两条流程的坑都记在 `docs/08-无头验证.md`。

## 技术要点

- **办公室是程序渲的 3D 房间**：一条 5120×1296 的圆柱全景条 + 相机绕着中轴**真的在转**，
  转头连续、有视差；门和敌人都是世界空间的 3D 物体、真实米数。墙面地板的贴图是 AI 生成的
  手绘绘本风，但**几何全部由程序算**（见 `docs/06-手绘贴图.md`）
- **素材管线是脚本**：`tools/` 下是一套 Python 渲染器 / 生成器，
  办公室全景、监控画面、门板、占位音频都是跑脚本产出的，改参数重跑即可
- **核心逻辑不碰引擎**：电量、时钟、四个敌人的状态机、断电流程、永夜记账、
  自定义夜与成就存档都是纯 C#，所以能做出 **123 个不依赖编辑器的单元测试**
- **程序集划分**：`Assets/Scripts/` 编成 `NailongFNAF`，
  `Assets/Tests/EditMode/` 编成 `NailongFNAF.EditModeTests`（引用前者）
- 中文字体用**思源黑体**（SIL OFL 1.1，可随包分发）

## 目录一览

```
Assets/
  Art/          Backgrounds · Characters · Monitoring · UI
  Audio/        BGM · SFX · Ambient
  Editor/       编辑器菜单工具（奶龙 → 1/2/3/4/5/9/10）+ 无头冒烟入口
  Prefabs/  Resources/  Scenes/  Shaders/
  StreamingAssets/
    Video/Jumpscares/   ← 4 条跳杀视频（运行时按路径读）
  Scripts/      ← 程序集 NailongFNAF
    Core/         GameManager（全局状态 / 暂停 / 存档）、NightClock
    Gameplay/     PowerSystem、EnemyDirector、四个敌人的 AI、难度表、
                  EndlessRecharge / EndlessDifficulty / EternalNightSave、
                  CustomNightConfig、AchievementCatalog / AchievementStore
    UI/           PrototypeScene（+ Menu 分部类）、OfficeRoom、HoldButton
    Audio/        AudioManager
    Diagnostics/  PlaySmokeProbe（只在 -playSmoke 时挂载）
  Tests/        EditMode/   ← 程序集 NailongFNAF.EditModeTests
docs/          设计与规格文档（见下表）
tools/         素材生成 / 渲染脚本（Python）
Builds/        打包产物（不入库）
```

## 文档

| 文件 | 内容 |
|---|---|
| `docs/01-环境搭建.md` | Unity 版本选择与安装步骤 |
| `docs/02-开发工具.md` | MCP for Unity 的安装与排障（让 AI 直接驱动编辑器） |
| `docs/03-素材规格.md` | 美术 / 音频 / 视频要按什么规格交付 |
| `docs/04-设计决策.md` | **设计定稿** —— 玩法、敌人、流程、永夜模式的最终结论 |
| `docs/05-生图提示词.md` | 生图提示词（办公室 / 监控机位 / 角色） |
| `docs/06-手绘贴图.md` | 办公室贴图怎么生成的、怎么换 |
| `docs/07-跳杀视频提示词.md` | 跳杀视频提示词（四条） |
| `docs/08-无头验证.md` | 不开编辑器跑测试、进 Play 自动截图 |
| `docs/09-打包.md` | 打包 Windows 版发给别人的流程、体积与注意事项 |
| `AGENTS.md` | 技术栈、目录规范、命名约定、架构模式（**开工前必读**） |

## 存档在哪儿

全部走 `PlayerPrefs`（Windows 下在 `%USERPROFILE%\AppData\LocalLow\NailongStudio\奶龙的五夜后宫\`）：

| 键 | 存什么 |
|---|---|
| `nailong.unlockedNight` | 故事模式解锁到第几夜（0 起） |
| `nailong.eternalSave` | 永夜模式进度存档（一行 `v1|...` 文本） |
| `nailong.endlessBest` | 永夜模式最好成绩（真实秒数） |
| `nailong.custom.<敌人>` | 自定义夜每只敌人的活动度 |
| `nailong.achv.<成就id>` | 成就解锁标记 |
| `nailong.customUnlocked` | 自定义夜是否已解锁 |

## 素材来源

| 素材 | 来源 |
|---|---|
| ambientCG 贴图（`tools/cc0/`） | ambientCG（CC0 1.0，可商用、无需署名） |
| Kenney 音效 | Kenney（CC0 1.0） |
| 思源黑体 `SourceHanSansCN-Normal.ttf` | SIL OFL 1.1，可随包分发 |
| 其余美术与音频 | 项目作者提供 |
