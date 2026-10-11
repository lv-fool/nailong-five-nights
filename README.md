# 奶龙的五夜后宫

一款 FNAF（Five Nights at Freddy's）式的第一人称生存恐怖游戏。Unity 2022.3 LTS + C#。

## 现在到哪一步了

- [x] 工作区与目录结构
- [x] Git 仓库（分支 `main`）
- [x] 项目约定、素材规格文档
- [x] 编辑器初始化工具
- [ ] **Unity 环境安装** ← 卡在这里
- [ ] 游戏设计定稿（数值、敌人、风格）
- [ ] 可玩原型

## 开始之前

Unity Hub 已装好（中国版 3.3.6），编辑器装 **2022.3.6f3c1**，细节见 `docs/01-环境搭建.md`。
用 Hub 打开本目录后，菜单栏会出现「奶龙」，依次跑三个命令即可。

## 文档

| 文件 | 内容 |
|---|---|
| `docs/01-环境搭建.md` | Unity 安装步骤、磁盘占用、中文路径风险 |
| `docs/03-素材规格.md` | 美术/音频要按什么规格交付 |
| `AGENTS.md` | 技术栈、目录规范、命名约定、架构模式（**开工前必读**） |

## 待拍板的设计问题

这些定了才能开始写游戏逻辑：

1. **视角操作**：鼠标移到屏幕边缘就转头（FNAF 原作），还是按键左右转？
2. **夜数结构**：五夜 + 隐藏夜？每夜时长几分钟？
3. **敌人数量与性格**：几个 animatronic？各自的行为模式（有的怕光、有的怕关门、有的只在特定机位出现）
4. **电量规则**：关门/开灯/看监控各耗多少？有没有"断电后撑几秒"的设定
5. **美术风格**：奶龙这个 IP 本身的调性偏可爱，怎么做恐怖？是可爱形象的反差恐怖，还是重绘成暗黑版本？
6. **平台**：只出 Windows 客户端，还是也要网页版

## 目录一览

```
Assets/
  Art/          Backgrounds · Characters · Monitoring · UI
  Audio/        BGM · SFX · Ambient
  Editor/       ProjectSetup.cs（编辑器工具）
  Prefabs/  Resources/  Scenes/  Shaders/
  Scripts/      Core · Gameplay · UI · Audio
docs/
ProjectSettings/
```
