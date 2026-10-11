# AGENTS.md · 项目约定

> 给 AI 助手和协作者看的开工须知。每次动这个项目之前先读一遍。
> 最后更新：2026-10-11

---

## 一、这是什么

《奶龙的五夜后宫》—— 一款 FNAF（Five Nights at Freddy's）式的第一人称生存恐怖游戏。

对标 FNAF 1 的核心循环：

- 玩家坐在办公室里，**不能移动**，只能左右转头、切换监控摄像头
- 左右两侧各有一扇门和一盏灯，关门、开灯都耗电
- 电量从 100% 持续下降，归零即死
- 每夜从 0 点走到 6 点，约 8–9 分钟
- 多个敌人各有独立 AI，在建筑内游走；逼近门口时构成威胁
- 撑过五夜（含隐藏夜）通关

> ⚠️ 具体数值、敌人设定、美术风格**尚未拍板**，待确认清单见 `README.md`。

## 二、技术栈（已锁定，不要擅自更换）

| 项 | 选择 | 说明 |
|---|---|---|
| 引擎 | **Unity 6.3 LTS**（`6000.3.26f1`） | 版本记录在 `ProjectSettings/ProjectVersion.txt` |
| 语言 | C# | 不引入第三方游戏框架 |
| 渲染管线 | **Built-in** | 见下方说明 |
| 画面方案 | **2D 静态场景 + 热点交互** | 最贴近 FNAF 原作 |
| 美术来源 | 用户自行提供 | 规格见 `docs/03-素材规格.md` |
| 版本控制 | Git，主分支 `main` | 已初始化 |

**为什么用 Built-in 而不是 URP**：FNAF 的恐怖感来自预渲染的静态画面、音效、
闪烁与噪点，不依赖实时光照。Built-in 少一层配置负担，出问题也更好排查。
真需要 2D 光照时再用 Package Manager 引入 URP 不迟。

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
| `Assets/Editor/` | 编辑器扩展脚本（打包后不进入游戏） |
| `Assets/Prefabs/` | 预制体 |
| `Assets/Resources/` | 需要运行时按字符串加载的资源（慎用，会全量打进包体） |
| `Assets/Scenes/` | 场景文件 |
| `Assets/Scripts/Core/` | 游戏状态机、时间推进、存档、全局管理器 |
| `Assets/Scripts/Gameplay/` | 电量、门、监控、敌人 AI |
| `Assets/Scripts/UI/` | HUD、菜单、界面交互 |
| `Assets/Scripts/Audio/` | 音频调度 |
| `Assets/Shaders/` | 噪点、暗角、扫描线等视觉效果 |
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
  - `bg_office_front.png` / `bg_office_left.png`
  - `mon_cam1a.png`
  - `char_nailong_idle.png`
  - `ui_btn_door_close.png`
- 音频：`sfx_door_close.ogg` / `amb_fan_loop.ogg` / `bgm_menu.ogg`

## 五、架构模式：静态场景 + 热点

FNAF 不是 3D 游戏，是一组**静态图 + 可点击区域 + 状态切换**。实现上：

- 每个"视角"是一个全屏 `Image`（或 `SpriteRenderer`），切换视角 = 切换显示的图
- 门、灯、监控按钮 = `Button` 或带 `EventTrigger` 的 `Image`
- 视角切换加**渐变/抖动/闪烁**过渡，避免生硬跳变（这是恐怖感的关键）
- 敌人逼近靠**音频先兆**（脚步声、金属摩擦）+ 画面细节变化暗示，而不是直接显示
- 全局状态由一个 `GameManager` 单例持有：当前时间、电量、门/灯开关、各敌人位置

**过渡动画比画面精度更重要。** 同一张图，加上手电筒式的抖动和噪点，
比一张精致但静止的图吓人得多。

## 六、协作工作流（关键，别忘）

**AI 无法运行 Unity 编辑器。** 因此：

1. AI 负责写代码、资产文件、文档
2. 用户在 Unity 里打开、编译、运行、**截图反馈**
3. 凡是需要在编辑器里点很多次的活（拖引用、建 Prefab、连事件），
   优先写成 `Assets/Editor/` 下的菜单工具一键完成，而不是丢给用户手工操作

**每次改动要小而可验证。** 一次改一大坨，出错无法定位。

**没有 .NET SDK，无法在外部做语法检查**，所以代码要写得保守：
不用冷门 API、不炫技、不依赖不确定是否存在的包。

## 七、可用 skill 与使用时机

| skill | 什么时候用 |
|---|---|
| `tdd` | 写核心逻辑（电量计算、AI 状态机、时间推进）时。纯逻辑类可以脱离 Unity 做 EditMode 测试 |
| `research` | 查 Unity API、还原 FNAF 具体机制、找设计参考时。要求带出处 |
| `grilling` | 需求含糊、该先问清楚再动手时 |
| `teach` / `handoff` / `grill-me` | 只能由用户主动唤起，AI 不得自行启动 |

`web-game-bot` 与本项目无关（那是浏览器游戏的自动化机器人）。

## 八、其他

- 项目路径含中文，相关风险与对策见 `docs/01-环境搭建.md` 第五节
- **不要删除 `.workbuddy-ai` 目录**，那里存着项目记忆
- 工程内的临时产物（`Library/`、`Temp/`、`obj/`）已在 `.gitignore` 排除
