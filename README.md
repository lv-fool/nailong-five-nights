# 奶龙的五夜后宫

一款 FNAF（Five Nights at Freddy's）式的第一人称生存恐怖游戏。Unity 2022.3 LTS + C#。

## 现在到哪一步了

- [x] 工作区与目录结构
- [x] Git 仓库（分支 `main`）
- [x] 项目约定、素材规格文档
- [x] 编辑器初始化工具
- [x] Unity 环境安装（2022.3.62f3c1）
- [x] 三个关键设计决策（视角 / 美术 / 平台）
- [x] 核心逻辑：电量系统、夜晚时钟（带单元测试）
- [x] 可玩原型（占位色块，待你验证）
- [ ] 敌人 AI
- [ ] 真素材替换
- [ ] 打包

## 开始之前

Unity Hub（中国版 3.3.6）与编辑器 2022.3.62f3c1 已装好，细节见 `docs/01-环境搭建.md`。

用 Hub 打开本目录后，菜单栏会出现「奶龙」：

| 菜单项 | 作用 |
|---|---|
| 1. 初始化项目设置 | 分辨率、色彩空间、公司 / 产品名 |
| 2. 生成主场景骨架 | 相机 + UI 画布 + 事件系统 |
| 3. 检查并补全目录 | 缺哪个目录补哪个 |
| **4. 搭建可玩原型** | 往当前场景放好原型入口，按 Play 就能玩 |

## 怎么跑测试

电量、时间这些核心逻辑是纯 C# 类，不依赖 Unity，测试在 `Assets/Tests/EditMode/`。

**Window → General → Test Runner → EditMode → Run All**

## 已定的设计

| 问题 | 结论 |
|---|---|
| 视角操作 | 鼠标移到屏幕左右边缘就转头（FNAF 原作做法） |
| 美术调性 | 可爱反差恐怖 —— 保留奶龙的可爱外形，靠氛围、音效、突然逼近吓人 |
| 目标平台 | 先只做 Windows（WebGL 模块已装，以后想加随时能加） |

## 还没定的设计

1. **夜数结构**：五夜 + 隐藏夜？每夜时长？暂按 FNAF 1：一夜 6 小时 ≈ 8 分 30 秒
2. **敌人数量与性格**：几个敌人？各自行为模式（怕光 / 怕关门 / 只在特定机位出现）
3. **电量规则**：暂用 FNAF 1 数值 —— 基础等级每 9.6 秒掉 1%，每多开一项设备升一级，上限 5 级

## 原型怎么玩

菜单「奶龙 → 4. 搭建可玩原型」，然后按 Play：

- 鼠标移到屏幕**左右边缘** → 左右转头
- 底部按钮 → 开关左右门 / 灯，切换监控
- **R 键** → 重开一夜

画面现在是纯色块，只为了验证玩法。真素材按 `docs/03-素材规格.md` 的命名放进 `Assets/Art/` 即可替换。

## 文档

| 文件 | 内容 |
|---|---|
| `docs/01-环境搭建.md` | Unity 安装步骤、磁盘占用、中文路径风险 |
| `docs/02-开发工具.md` | MCP for Unity 的安装与排障 |
| `docs/03-素材规格.md` | 美术 / 音频要按什么规格交付 |
| `AGENTS.md` | 技术栈、目录规范、命名约定、架构模式（**开工前必读**） |

## 目录一览

```
Assets/
  Art/          Backgrounds · Characters · Monitoring · UI
  Audio/        BGM · SFX · Ambient
  Editor/       ProjectSetup.cs · PrototypeSetup.cs（编辑器工具）
  Prefabs/  Resources/  Scenes/  Shaders/
  Scripts/      Core · Gameplay · UI · Audio   ← 程序集 NailongFNAF
  Tests/        EditMode/                      ← 程序集 NailongFNAF.EditModeTests
docs/
ProjectSettings/
```
