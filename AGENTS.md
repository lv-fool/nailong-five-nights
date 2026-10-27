# AGENTS.md · 项目约定

> 给 AI 助手和协作者看的开工须知。每次动这个项目之前先读一遍。
> 最后更新：2026-10-27（补安卓分支）
>
> ⚠️ **本文档在 2026-10-11 之后长期没跟上代码，2026-10-18 已按实际代码纠偏。**
> 最大的偏差：画面方案早就不是「2D 静态图 + 热点」，而是**程序渲的 3D 圆柱全景 + 相机真转**。
> 遇到冲突以 **代码 + `.workbuddy-ai/memory/MEMORY.md`** 为准。

---

## 一、这是什么

《奶龙的五夜后宫》—— 一款 FNAF（Five Nights at Freddy's）式的第一人称生存恐怖游戏。

对标 FNAF 1 的核心循环：

- 玩家坐在办公室里，**不能移动**，只能左右转头、切换监控摄像头
- 左右两侧各有一扇门和一盏灯，关门、开灯都耗电；**开着灯才看得见门外站着谁**
- 屏幕底部中间有一个**手电筒**（按住才亮、松手就灭），用来照从正门摸进来的噜噜
- 电量从 100% 持续下降，归零即死
- 每夜从 0 点走到 6 点，约 8–9 分钟
- 多个敌人各有独立 AI，在建筑内游走；逼近门口时构成威胁
- 撑过五夜（含隐藏夜）通关

> ✅ **设计已定稿**，见 `docs/04-设计决策.md` —— 玩法、四个敌人、流程、跳杀方式都在那里。**写代码前先读它。**
> 素材规格见 `docs/03-素材规格.md`。

## 二、技术栈（已锁定，不要擅自更换）

| 项 | 选择 | 说明 |
|---|---|---|
| 引擎 | **Unity 2022.3 LTS**（`2022.3.62f3c1`） | 版本记录在 `ProjectSettings/ProjectVersion.txt` |
| 语言 | C# | 不引入第三方游戏框架 |
| 渲染管线 | **Built-in** | 见下方说明 |
| 画面方案 | **程序渲 3D 圆柱全景 + 相机真转** | 不是静态图切换，见第五节 |
| 交互方案 | 2D 热点（按钮 / 小地图 / 手电筒） | 门与敌人是 3D 世界物体 |
| 美术来源 | 用户自行提供 | 规格见 `docs/03-素材规格.md` |
| 平台 | Windows 桌面 + **Android 触屏版** | 安卓在分支 `android-port`，IL2CPP + ARM64，见 `docs/10-安卓移植.md` |
| 版本控制 | Git，主分支 `main` | 已初始化 |

**为什么用 Built-in 而不是 URP**：FNAF 的恐怖感来自预渲染的静态画面、音效、
闪烁与噪点，不依赖实时光照。Built-in 少一层配置负担，出问题也更好排查。
真需要 2D 光照时再用 Package Manager 引入 URP 不迟。

**为什么是 2022.3 而不是 Unity 6**：本机装的是**中国版 Unity Hub 3.3.6**，
它的版本接口拿不到 Unity 6 的列表（实测只返回 3 条记录），要装 Unity 6 得手动下
3.9 GB 安装包绕过 Hub。而 2022.3 LTS 做「程序渲静态图 + 热点交互」没有任何短板，中文教程也最多。
完整来龙去脉见 `docs/01-环境搭建.md`。

## 三、目录规范

| 路径 | 放什么 |
|---|---|
| `Assets/Art/Backgrounds/` | 办公室各视角的静态背景图 |
| `Assets/Art/Characters/` | 角色立绘、贴脸惊吓图 |
| `Assets/Art/Monitoring/` | 监控画面素材（每个机位一张） |
| `Assets/Art/UI/` | 按钮、图标、电量条等界面元素 |
| `Assets/Audio/BGM/` | 背景音乐 |
| `Assets/Audio/SFX/` | 音效（关门、开灯、脚步、尖叫） |
| `Assets/Audio/Ambient/` | 环境音（风扇、电流嗡鸣） |
| `Assets/Video/Jumpscares/` | 跳杀视频（每个敌人一条 mp4） |
| `Assets/Editor/` | 编辑器扩展脚本（打包后不进入游戏） |
| `Assets/Tests/EditMode/` | 纯逻辑的单元测试，在 Test Runner 里跑 |
| `Assets/Prefabs/` | 预制体 |
| `Assets/Resources/` | 运行时按字符串加载的资源（会全量打进包体）：`office_pano.png`、`PanoMaterial.mat`、`Fonts/SourceHanSansCN-Normal.ttf` |
| `Assets/Scenes/` | 场景文件 |
| `Assets/Scripts/Core/` | 游戏状态机、时间推进、存档、全局管理器 |
| `Assets/Scripts/Gameplay/` | 电量、门、监控、敌人 AI |
| `Assets/Scripts/UI/` | HUD、菜单、界面交互 |
| `Assets/Scripts/Audio/` | 音频调度 |
| `Assets/Shaders/` | `UnlitTexture.shader`（内置 `Unlit/Texture` 的项目内副本，防发行版被剔除）+ 以后的噪点/暗角 |
| `docs/` | 设计文档、规格说明 |

**新文件一律归位，不要往 `Assets/` 根目录随手丢。**

## 四、命名约定

- 类 / 方法 / 属性：`PascalCase`
- 私有字段：`_camelCase`
- 常量：`PascalCase`
- **文件名必须等于类名**（Unity 的硬性要求，否则脚本挂不上）
- 命名空间：`NailongFNAF.<模块>`，例如 `NailongFNAF.Gameplay`
- 场景：`Assets/Scenes/` 下，PascalCase，如 `Main.unity`
- 美术素材：`snake_case` + 类型前缀
  - `office_pano.png`（办公室背景，一条圆柱全景；放 `Assets/Resources/`，运行时 `Resources.Load`。
    **导入必须 `maxTextureSize = 8192`** —— 默认 2048 会把它压回 2048 而糊掉）
  - `mon_1a.png`（监控画面，编号照官方 CAM：`mon_1a` ~ `mon_7`）
  - `char_nailong_idle.png`
  - `ui_btn_door_close.png`
- 音频：`sfx_door_close.ogg` / `amb_fan_loop.ogg` / `bgm_menu.ogg`
- 视频：`char_<角色名>_jumpscare.mp4`（如 `char_nailong_jumpscare.mp4`）
- 字体：`Fonts/SourceHanSansCN-Normal.ttf`（**思源黑体**，SIL OFL 1.1，可随包分发）。
  代码里只认 `PrototypeScene.FontResourcePath` 这一个名字，换字体丢同名文件进去即可
- **四个角色的代号固定为**：`nailong`（奶龙）/ `niulai`（牛来）/ `daishu`（袋鼠）/ `lulu`（噜噜）。
  文件名和代码里的枚举名都用这套，别用别的拼法

## 五、架构模式：圆柱全景 + 3D 世界物体 + UGUI 热点

⚠️ **这一节 2026-10-18 重写过。** 旧版写的是「2D 静态场景 + 热点交互」，
代码早在 2026-10-14 就改成了程序渲 3D —— 照旧版理解会走错路。

**办公室（背景）**
- 一条 5120×1296 的**圆柱全景** `Assets/Resources/office_pano.png`（`tools/render_pano.py` 渲的），
  Unity 侧由 `Assets/Scripts/UI/OfficeRoom.cs` 程序化生成圆柱内壁贴上它
- **转头 = 相机真的绕中轴转**（`MaxYaw = 70°`），连续且有视差。
  **不是**若干张视角帧交叉淡化 —— 那版中间帧是复制的，看着就是转场，已作废
- 材质走 `Resources.Load<Material>("PanoMaterial")`（**不是** `Shader.Find`）：
  发行版会把没人引用的内置 shader 剔除掉，`Shader.Find` 返回 null 会让办公室整个崩
- ⚠️ 投影约定在 `render_pano.py` 与 `OfficeRoom.cs` **两边必须一致**（θ=0 正前、覆盖 ±145°、
  斜率 ±0.72、相机 (0, 1.30, -1.20)）。改一边不改另一边 = 全景和门洞对不上
- ⚠️ 圆柱内壁三角面**绕序必须顺时针**，反了整屏全黑（内置 `Unlit/Texture` 没有 `_Cull` 可救）

**门与敌人（3D 世界物体）**
- 门板、敌人立绘都是挂 `SpriteRenderer` 的**世界物体**，由 `Camera.main` 透视，真实米数（门 1.2×2.05m、敌人约 1.8m）
- ⚠️ `SpriteRenderer` 默认平面是 XY（法线 +z）：贴侧墙（x=±2.6）必须 `rotation = Euler(0, ±90, 0)`，
  否则相机侧看它成一根竖线，门洞面前渲不出东西
- ⚠️ `localScale` 是**倍数**不是米，要给米数得算 `目标 / sprite.bounds.size`

**界面与热点（UGUI）**
- 墙上 DOOR / LIGHT 按钮**贴墙投影**：锚在固定世界点，每帧 `WorldToScreenPoint` 摆过去，
  **必须在 `_room.SetPan(_pan)` 之后调**（相机朝向先定）
- 监控小地图、电量条、手电筒（`HoldButton`，按住才亮）都是 UGUI
- ⚠️ 按钮淡入必须很快满（`WallButtonFadeStart/End = 0.06/0.26`），
  否则鼠标停在按钮上时 `_pan` 只有 0.28、`blocksRaycasts` 永远为假、**按钮点不着**

**全局状态**
- `GameManager`（时间/电量/门灯/断电流程）+ `EnemyDirector`（四个敌人）+ `PrototypeScene`（表现层）
- ⚠️ **`Main.unity` 里只有 `PrototypeScene` 一个组件**，`GameManager` 与 `EnemyDirector`
  是它运行时 `AddComponent` 造出来的 —— 所以这两类上 `[SerializeField]` 的默认值永远生效
- 核心逻辑（`PowerSystem` / `NightClock` / 敌人 AI / `BlackoutSequence`）是**纯 C#、不引用 UnityEngine**，
  为的是能脱离 Unity 做单元测试。新增核心逻辑请保持这个习惯

**过渡与氛围**：恐怖感来自音效 + 抖动/噪点/闪烁 + 突然逼近，不在画面精度。
敌人逼近靠音频先兆和细节变化暗示，不直接显示。

## 六、协作工作流（关键，别忘）

**AI 已经能直接驱动 Unity 编辑器（MCP for Unity，2026-10-18 起）。** 流程变了：

1. ⚠️ **改代码前先退出 Play 模式** —— Play 期间脚本一重编译就 domain reload，
   `GameManager.Instance` 这类静态字段被清空但 `Awake` 不会重跑，会刷一屏 `NullReferenceException`
2. 改完代码 → 跑 **EditMode 测试** → 读 Console 确认 0 报错
3. 要验证画面 → 进 Play、反射操纵游戏状态、截图后**自己看图**
4. 截图存 **`.workbuddy-ai/screenshots/`**（不进版本库）。
   ⚠️ 别用默认输出目录：MCP 截图默认落到 `Assets/Screenshots/`，会污染仓库

**MCP 用法**：资源 `mcpforunity://instances`（连到哪个 Unity）、`mcpforunity://editor/state`（是否 ready）；
工具 `run_tests` + `get_test_job`、`read_console`、`manage_editor`（进出 Play）、
`manage_build`（出构建）、`manage_camera`（截图）、`execute_code`（跑 C#）。
⚠️ **编辑器开着时不要试图命令行 `-batchmode` 构建同一项目**（锁文件挡着），直接走 MCP。

**每次改动要小而可验证。** 一次改一大坨，出错无法定位。

⚠️ **本机没有 .NET SDK，无法在 Unity 之外做 C# 语法检查** —— 编译错误只能等 Unity 报，
所以代码要写得保守：不用冷门 API、不炫技、不依赖不确定是否存在的包。

**程序集划分（asmdef）**：`Assets/Scripts/` 下有一个 `NailongFNAF.asmdef`，
把游戏代码编成独立程序集 `NailongFNAF`；`Assets/Tests/EditMode/` 下是
`NailongFNAF.EditModeTests`，引用前者 —— 测试要访问游戏代码，必须这样分。
新增脚本丢进 `Assets/Scripts/` 子树就自动归入该程序集，不用改配置。

**怎么跑测试**：Window → General → Test Runner → EditMode → Run All。
电量、时间这类纯逻辑类不引用 UnityEngine，可以脱离 Unity 测，改动后务必跑一遍。
**没人看着的时候**（AI / CI / 不开图形界面）用命令行版：`-batchmode -runTests` 跑测试、
`-playSmoke` 进 Play 自动截图，完整命令与坑（要去掉代理环境变量、参数必须拼成一个带引号的
字符串、跑完进程不退出会占着锁文件）见 `docs/08-无头验证.md`。
Unity 2022.3.62f3c1 已装在本机 `F:\Unity\2022.3.62f3c1\Editor\Unity.exe`。

## 七、可用 skill 与使用时机

视手头工具而定。要点是**别跳过验证**：

| 场景 | 做法 |
|---|---|
| 写核心逻辑（电量 / 时钟 / AI 状态机） | 先写 EditMode 测试再实现 —— 这些类是纯 C#，测试很便宜 |
| 查 Unity API / 还原 FNAF 机制 | 先查证再动手，别凭记忆写 API |
| 需求含糊 | 先问清楚，别猜着写一大坨 |
| 改完 | 跑测试 + 看 Console 有没有报错。**没有验证过的改动不算完成** |

## 八、其他

- ✅ 工程位置（2026-10-27 现状）：桌面原项目在 `F:\nailong f`，安卓移植分支 `android-port`
  克隆在 `F:\nailong-f-android`（即当前工作目录）。都是全英文路径；迁移记录见 `docs/01-环境搭建.md`。
  两个目录的 Unity 别同时开着同一个项目 —— 锁文件会互相挡。
- **不要删除 `.workbuddy-ai/` 目录** —— 项目长期记忆与工作日志在 `memory/`，
  排障与踩坑记录都在里面（`.gitignore` 排除，但绝不能删）
- 已在 `.gitignore` 排除、**不入库**的本地目录：`Library/`、`Temp/`、`Logs/`、`Builds/`、
  `UserSettings/`、`.workbuddy-ai/`、`Packages/MCPForUnity/`（本地嵌入的 MCP 包）、
  `__pycache__/`。换机器要按 `docs/02-开发工具.md` 重建 MCP 包。
- ⚠️ **构建产物在 `Builds/`**（Windows 版约 320 MB、安卓 APK 约 121 MB），别误当源码删了；
  它是可再生的，但重新构建要几分钟。
- ⚠️ **Windows 版只装了 Mono 变体**（`PlaybackEngines\WindowsStandaloneSupport\Variations\` 下无 IL2CPP）。
  打包走 `Assets/Editor/BuildWindows.cs`（菜单「奶龙 / 10. 打包 Windows 版」或
  `-executeMethod NailongFNAF.EditorTools.BuildWindows.Build`），产物在 `Builds/Windows/`，
  约 263 MB，压 zip 约 116 MB。流程与注意事项见 `docs/09-打包.md`；
  要 IL2CPP 得先用 Hub 补装 **Windows Build Support (IL2CPP)** 模块
- **安卓版**：`Assets/Editor/BuildAndroid.cs`（菜单「奶龙 / 11. 打包安卓版」或
  `-executeMethod NailongFNAF.EditorTools.BuildAndroid.Build`），IL2CPP + ARM64，
  产物 `Builds/Android/NailongFiveNights.apk`（约 121 MB）。环境、坑、真机清单见 `docs/10-安卓移植.md`
