using System.Collections.Generic;
using UnityEngine;
using NailongFNAF.Audio;
using NailongFNAF.Core;
using NailongFNAF.Gameplay;

namespace NailongFNAF.UI
{
    /// <summary>
    /// 自动通关 AI —— 挂在场景里就能自己打完一夜。
    ///
    /// **它只用玩家能获得的信息 + 引擎内部的精确数值**：
    /// 敌人 Stage / IsAttacking / InLastChance 这些是读取真实状态，
    /// 相当于一个「反应完美、但决策规则仍按人类逻辑」的玩家。
    /// 不是作弊秒杀，电量照扣、门灯照耗。
    ///
    /// 用法：
    ///   var ai = gameObject.AddComponent&lt;AutoPlayer&gt;();
    ///   ai.Begin();   // 开始自动打
    ///   ai.Stop();    // 停下交还控制权
    ///
    /// 调试/测试用，不参与正式玩法。
    /// </summary>
    public class AutoPlayer : MonoBehaviour
    {
        // ── 决策参数 ──

        /// <summary>门外有人时，关门提前量（秒）—— 在它动手之前这么多秒关上门。</summary>
        private const float DoorSafetyMargin = 1.2f;

        /// <summary>
        /// 最低保电：低于这个值就进「极限省电」模式 —— 除了保命一律不碰。
        /// </summary>
        private const float LowPowerThreshold = 40f;

        /// <summary>
        /// 监控抬头的间隔（秒）。
        ///
        /// ⚠️ 这个值直接决定省不省电：**监控开着 = 耗电倍率 +1 档**
        /// （见 GameManager.CountUsageLevel）。基础 9.6 秒/1%，
        /// 多开一档就是一倍速度。一夜 510 秒，纯基础要 53% ——
        /// 监控开一半时间就变成 80%，很容易不够用。
        /// 所以这里是「隔一阵才抬头看一眼」，不是一直挂着。
        /// </summary>
        private const float MonitorSweepInterval = 6f;

        /// <summary>抬头之后最多看多久就放下（秒）—— 别赖在监控里。</summary>
        private const float MonitorLookSeconds = 1.2f;

        /// <summary>噜噜还差几下才退场时，才开始闪（提前闪浪费）。</summary>
        private const int LuluFlashThreshold = 4;

        private GameManager _gm;
        private EnemyDirector _director;
        private PrototypeScene _scene;

        /// <summary>
        /// 音效播放器。开关门/灯的声音由 <see cref="DoorLightActions"/> 统一放 ——
        /// 早先这里没有它，AI 的操作全程静音（2026-11-04 报的）。
        /// </summary>
        private AudioManager _audio;

        private bool _running;

        private float _monitorSweepTimer;
        private int _sweepCamera;

        private float _luluFlashTimer;
        private bool _luluFlashing;

        // 反射缓存 —— 每帧都 GetMethod 太慢
        private System.Reflection.MethodInfo _toggleMonitor;
        private System.Reflection.MethodInfo _setFlashlight;
        private System.Reflection.FieldInfo _monitorIntent;

        /// <summary>
        /// PrototypeScene._flashlightHeld —— 游戏内部「手电是不是按着」的状态。
        ///
        /// ⚠️ AI 必须写这个字段，不能只调 gm.SetFlashlight()：
        /// ApplyFlashlight 每帧都用 _flashlightHeld 和真实的鼠标/Ctrl 状态比较，
        /// 不一致就叫 OnFlashlightHold 覆盖回去。只设 gm.FlashlightOn 会被每帧冲掉，
        /// 表现就是「AI 以为在闪，实际手电一直是灭的」。
        /// </summary>
        private System.Reflection.FieldInfo _flashlightHeld;

        // 注：早先这里还有 _pan / _panTarget 两个反射字段，用来每帧把视角钉在正前方。
        // 2026-11-03 用户改成「只有闪手电时才回正」，改走 PrototypeScene.ViewExternalControl
        // 那个正式开关，反射字段就没用了 —— 别再捡回来，直接写 _pan 会被 UpdateView 冲掉。

        private static readonly System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;

        /// <summary>
        /// 把手电要照的那一段视角收回正前方（**只在闪手电期间**用）。
        ///
        /// 两件事都要做：
        ///   · 让 PrototypeScene 别再按鼠标算转头（ViewExternalControl）——
        ///     否则它在 Update 里每帧重算 _panTarget，外面写的值立刻被冲掉
        ///   · gm.SetView(Front) —— 噜噜认的是这个标记（EnemyContext.IsShiningAtFront）
        ///
        /// ⚠️ 用户 2026-11-03 定：**只有闪手电的时候**才回正，其余时间视角归玩家。
        /// 早先这里每帧无条件钉死正前方，等于把玩家唯一的操作自由也抢了。
        /// </summary>
        private void SetFrontViewHold(bool hold)
        {
            if (_scene != null)
            {
                _scene.ViewExternalControl = hold;
            }

            if (!hold)
            {
                return;
            }

            // CurrentView 由 PrototypeScene.UpdateView 按 _pan 反推，
            // 我们刚把手电接管起来，标记立刻改成 Front —— 手电这一帧就生效，
            // 不用等镜头平滑转回来（转的过程玩家看得见，判定不用等它）。
            if (_gm.CurrentView != ViewSide.Front)
            {
                _gm.SetView(ViewSide.Front);
            }
        }

        /// <summary>
        /// 关这侧门时的转头 —— **已停用**（用户 2026-11-01 定）。
        ///
        /// 早先会朝关的那侧偏 ±0.55（约 38°），想让玩家看见「AI 转头去关门」。
        /// 但手电是**贴在桌面世界点上**的，相机一转它就跟着画面跑出原来位置，
        /// 看起来像「手电不固定了」。
        ///
        /// 2026-11-03 又补了一条更硬的理由：**AI 代打期间视角默认归玩家**，
        /// 关个门就抢镜头属于越权。现在整份代码里唯一允许动视角的地方
        /// 是闪手电（见 SetFrontViewHold）。
        ///
        /// 留着这个空方法是为了调用点不用改，也不容易再被误加回来。
        /// </summary>
        private void PeekAtSide(Side side)
        {
            // 故意留空
        }

        /// <summary>最近一次决策的说明，供调试面板显示。</summary>
        public string LastAction { get; private set; }

        public void Begin()
        {
            _gm = GameManager.Instance;
            if (_gm == null)
            {
                Debug.LogWarning("[AutoPlayer] 找不到 GameManager");
                return;
            }

            _director = FindObjectOfType<EnemyDirector>();
            _scene = FindObjectOfType<PrototypeScene>();

            // 音效是关键反馈 —— 没有它 AI 关门/开灯完全听不出来（2026-11-04 报的）。
            // 拿不到也不算致命错误，静默继续。
            _audio = FindObjectOfType<AudioManager>();

            if (_director == null)
            {
                Debug.LogWarning("[AutoPlayer] 找不到 EnemyDirector");
                return;
            }

            var t = typeof(GameManager);
            _toggleMonitor = t.GetMethod("ToggleMonitor", Flags);
            _setFlashlight = t.GetMethod("SetFlashlight", Flags);

            _monitorIntent = t.GetField("_monitorPlayerIntent", Flags)
                          ?? t.GetField("monitorPlayerIntent", Flags);

            // PrototypeScene 上的手电状态字段
            if (_scene != null)
            {
                _flashlightHeld = typeof(PrototypeScene).GetField("_flashlightHeld", Flags);
            }

            // 接管手电 —— 否则 PrototypeScene 每帧按鼠标状态把手电冲掉
            if (_scene != null)
            {
                _scene.FlashlightExternalControl = true;
            }

            _running = true;
            _monitorSweepTimer = 0f;
            _sweepCamera = 0;
            LastAction = "启动";

            Debug.Log("[AutoPlayer] 自动通关开始");
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            ReleaseEverything();

            if (_scene != null)
            {
                _scene.FlashlightExternalControl = false;
                _scene.ExternalFlashlightOn = false;

                // ⚠️ 视角接管也一定要放掉 —— 漏了这行的话，停止 AI 之后
                // ViewExternalControl 还停在 true，玩家再也没法按鼠标转头（永久锁正前方）。
                _scene.ViewExternalControl = false;
            }
            LastAction = "停止";
            Debug.Log("[AutoPlayer] 自动通关停止");
        }

        private void ReleaseEverything()
        {
            SetMonitor(false);
            SetFlashlight(false);
            _luluFlashing = false;
        }

        private void Update()
        {
            if (!_running)
            {
                return;
            }

            if (_gm == null || _gm.HasBeenCaught || _gm.HasSurvived || _gm.HasPowerOut)
            {
                if (_gm != null && (_gm.HasBeenCaught || _gm.HasSurvived))
                {
                    _running = false;
                    LastAction = _gm.HasSurvived ? "通关" : "被抓";

                    // ⚠️ 这一夜结束了也必须把两个「外部接管」放掉 ——
                    // 否则下一局开局视角还被锁在正前方，玩家怎么晃鼠标都不转头。
                    // （这里不能调 Stop()：它有 _running 早退，而上面刚把它设成 false。）
                    if (_scene != null)
                    {
                        _scene.FlashlightExternalControl = false;
                        _scene.ExternalFlashlightOn = false;
                        _scene.ViewExternalControl = false;
                    }
                }

                return;
            }

            Think(Time.deltaTime);
        }

        /// <summary>
        /// 手电放在 LateUpdate 写 —— 这是唯一不被覆盖的时机。
        ///
        /// PrototypeScene.ApplyFlashlight（Update 里跑）每帧都会拿
        /// 「鼠标/Ctrl 是否按着」去修正 _flashlightHeld 和 gm.FlashlightOn。
        /// AI 不能动鼠标，所以只要 PrototypeScene 跑在 AI 后面，AI 写的值立刻被冲掉。
        ///
        /// LateUpdate 在所有 Update 之后 —— 写进去的值能活到下一帧的
        /// EnemyDirector.Update（它才读 gm.FlashlightOn 构造 EnemyContext）。
        /// 代价是手电状态晚一帧生效，对 0.4 秒一轮的闪光完全够用。
        /// </summary>
        private void LateUpdate()
        {
            if (!_running)
            {
                return;
            }

            ApplyFlashlightIntent();
        }

        // ---------------------------------------------------------------
        // 决策主流程
        // ---------------------------------------------------------------

        private void Think(float dt)
        {
            // 优先级从高到低：
            //   1. 门口有敌人 → 关门（保命第一）
            //   2. 正门噜噜在做准备 → 开手电闪
            //   3. 没事 → 扫监控看奶龙 / 攒时间
            //   4. 电量低 → 全关省电

            bool lowPower = _gm.Power.Level < LowPowerThreshold;

            // ── 视角：平时完全交给玩家 ──
            //
            // ⚠️ 用户 2026-11-03 定：**只有闪手电时才把视角收回正前方**，
            // 其余时候一律不碰（早先是每帧无条件钉死正前方）。
            //
            // 为什么闪的时候必须回正：噜噜的手电判定是
            // EnemyContext.IsShiningAtFront() = FlashlightOn && !MonitorUp && View == Front，
            // 玩家把镜头歪到左边时，AI 就算把手电打开也照不到正门。
            //
            // 接管交给 PrototypeScene.ViewExternalControl —— 不能直接写 _pan：
            // UpdateView 每帧按鼠标重算 _panTarget，外面写的一律被冲掉（踩过）。
            // 开/关都集中在 HandleLulu（HandleLulu 每帧都会跑，所以「不闪了要放掉」
            // 也在那里统一处理，这里不需要再兜一次）。

            // ── 噜噜到门口时，一律优先处理它 ──
            //
            // ⚠️ **通话期间 CanOperate() 是 false**，SetView / SetFlashlight /
            // 开关门全都不生效。如果这时候噜噜堵在正门，AI 会「以为在闪，
            // 实际手电压根打不开」，然后被它扑掉（踩过）。
            // 所以只要它站在门口，电话的事往后放。
            bool luluUrgent = LuluNeedsFlash();

            if (luluUrgent)
            {
                // 正在通话就先挂断 —— 它比奶包重要
                if (_gm.NaibaoTalking)
                {
                    _gm.EndNaibaoCall();
                }
            }
            else
            {
                // ── 0. 压制奶包（她动手会直接坑人，但不急过门口的噜噜）──
                HandleNaibao(dt);
            }

            // ── 1. 侧门 ──
            bool leftThreat = HandleSide(Side.Left, lowPower);
            bool rightThreat = HandleSide(Side.Right, lowPower);

            // ── 2. 正门噜噜 ──
            // ⚠️ 手电只在「监控放下 + 视角朝正前方」时才算数，
            // 所以在处理噜噜之前必须先放下监控，否则闪半天 FlashCount 还是 0。
            bool luluNeedsAttention = LuluNeedsFlash();
            if (luluNeedsAttention && _gm.MonitorUp)
            {
                SetMonitor(false);
            }

            HandleLulu(dt);

            // 处理噜噜期间也别抬头
            if (luluNeedsAttention)
            {
                return;
            }

            // ── 2.5 门口的外卖（袋鼠被挡走后留下的，补电用）──
            //
            // 优先级放在「门/噜噜」之后、「抬头」之前：
            //   · 有威胁时不该为了捡外卖冒险（HANDLE 里会自己判 leftThreat）
            //   · 但要在抬头之前处理 —— 因为**捡外卖要求监控放下**
            //     （HandleDeliveryClick 里 `if (gm.MonitorUp) return`），
            //     一旦先抬了头，这一帧就捡不成了，而外卖只有 20 秒。
            HandleDelivery(leftThreat);

            // ── 3. 监控：只在两侧都安全时抬头 ──
            if (!leftThreat && !rightThreat)
            {
                HandleMonitor(dt);
            }
            else
            {
                // 有威胁时一定放下监控（否则看不见门口，也放不下门）
                SetMonitor(false);
                _monitorSweepTimer = 0f;
            }
        }

        /// <summary>
        /// 捡门口的外卖（2026-11-13 加）。
        ///
        /// ## 外卖是什么
        /// 袋鼠敲完门被挡走之后，门口会留一份外卖，捡起来补 1 / 2 / 5 点电
        /// （见 <see cref="NailongFNAF.Gameplay.DeliveryDrop"/>）。**只放 20 秒**，
        /// 到点就凉。
        ///
        /// ## 为什么以前 AI 捡不到
        /// 拾取入口是 UI 层的鼠标点击（`PrototypeScene.HandleDeliveryClick`），
        /// AI 不模拟鼠标，所以从来没捡过。但底下其实有个干净的公开接口
        /// `EnemyDirector.TryCollectDelivery()` —— 直接调它就行，不用碰 UI。
        ///
        /// ## 三个前提（全部来自 UI 层的实际判据，不能拍脑袋）
        ///   1. `Delivery.Available` —— 有货
        ///   2. **左门必须开着** —— 外卖摆在门外地上，门关着袋子会被藏起来
        ///      （`ApplyDeliveryBag`：`show = available && doorOpen && gm.Operable`）。
        ///      门关着去调 TryCollectDelivery 虽然也能成功，但那是「隔空取物」，
        ///      和玩家看到的不一样，不诚实。
        ///   3. **监控必须放下** —— 玩家是「看着门外地上点它」，
        ///      抬头时根本看不到门外（`HandleDeliveryClick` 里有 `MonitorUp` 早退）。
        ///
        /// ## 什么情况下放弃
        ///   · 左边有威胁：命比几度电重要，让 <paramref name="leftThreat"/> 决定
        ///   · 门开着但门外站着人（威胁中）→ 同上，先关门
        /// 其余情况（包括电量满）都捡 —— 反正不花电，捡了不亏。
        /// </summary>
        private void HandleDelivery(bool leftThreat)
        {
            if (_director == null || _director.Delivery == null)
            {
                return;
            }

            // 有威胁时别为几度电冒险 —— 关门保命优先
            if (leftThreat)
            {
                return;
            }

            if (!_director.Delivery.Available)
            {
                return;
            }

            // 条件 2：左门要开着（否则就是隔空取物）
            if (_gm.LeftDoorClosed)
            {
                return;
            }

            // 条件 3：监控要放下
            if (_gm.MonitorUp)
            {
                SetMonitor(false);
                return;   // 这一帧先放监控，下一帧再捡（放下有动画，不能立即生效）
            }

            // 玩家此刻必须能动（通话中 CanOperate() 是 false，UI 那边也点不动）
            if (!_gm.Operable)
            {
                return;
            }

            // ⚠️ **先存下收益再捡** —— TryCollectDelivery() 成功后
            // `Delivery.Available` 就是 false 了，`Gain` 跟着变 0，
            // 捡完再读会是「+0 电」（踩过）。
            float gain = _director.Delivery.Gain;

            if (_director.TryCollectDelivery())
            {
                LastAction = "捡外卖 +" + Mathf.RoundToInt(gain) + " 电";
            }
        }

        /// <summary>通话压制的冷却计时。</summary>
        private float _naibaoCooldown;

        /// <summary>两通电话之间的间隔（秒）。比她最短的 38 秒略短，保证压得住。</summary>
        private const float NaibaoCallIntervalSeconds = 34f;

        /// <summary>通话期间推进台词的间隔。</summary>
        private float _naibaoAdvanceTimer;

        /// <summary>
        /// 压制奶包 —— 她要 30 秒后开始动手，之后每 38~72 秒一次。
        ///
        /// **通话期间她不动手**（NaibaoDirector.Tick 里 _talking 就直接 return），
        /// 而且接通时会立刻撒手（抢走的监控还回来、偷加的那一档也熄掉）。
        /// 所以压制手段就是：算准节奏打电话。
        ///
        /// 代价：通话中整条线路多走一格电（CountUsageLevel 里 NaibaoTalking +1）。
        /// 所以不能一直打 —— 打完赶紧把话推完挂掉。
        /// </summary>
        private void HandleNaibao(float dt)
        {
            if (_gm.Naibao == null)
            {
                return;
            }

            _naibaoCooldown -= dt;

            if (!_gm.NaibaoTalking)
            {
                if (_naibaoCooldown <= 0f)
                {
                    _gm.StartNaibaoCall(0, false);
                    _naibaoCooldown = NaibaoCallIntervalSeconds;

                    if (_gm.NaibaoTalking)
                    {
                        LastAction = "打电话压制奶包";
                    }
                }

                return;
            }

            // 正在通话：尽快把台词推完挂断（通话中 +1 档电，别拖着）
            _naibaoAdvanceTimer += dt;
            if (_naibaoAdvanceTimer >= 0.8f)
            {
                _naibaoAdvanceTimer = 0f;
                _gm.AdvanceNaibaoCall();
                LastAction = "通话中，赶紧挂断";
            }
        }

        /// <summary>
        /// 处理一侧的门。返回 true = 这一侧有威胁（需要专注）。
        /// </summary>
        private bool HandleSide(Side side, bool lowPower)
        {
            bool closed = side == Side.Left ? _gm.LeftDoorClosed : _gm.RightDoorClosed;
            bool lightOn = side == Side.Left ? _gm.LeftLightOn : _gm.RightLightOn;

            // 被突破的侧没救了，不用管
            if (_gm.IsSideDisabled(side))
            {
                return false;
            }

            DoorEnemyBrain threat = null;
            float threatLevel = 0f;   // 越大越急

            foreach (var brain in _director.Brains)
            {
                var door = brain as DoorEnemyBrain;
                if (door == null || door.Side != side || door.IsAttacking)
                {
                    continue;
                }

                bool atDoor = door.Stage >= door.MaxStage;
                if (!atDoor)
                {
                    continue;
                }

                // 已经在门口：算它还有多久动手
                //   DoorTimer 在攒移动机会，攒满就判门
                float untilDecision;

                if (door.DoorTimer < door.DoorWindow)
                {
                    untilDecision = door.DoorWindow - door.DoorTimer;
                }
                else if (door.InLastChance)
                {
                    untilDecision = door.LastChanceSeconds - door.LastChanceTimer;
                }
                else
                {
                    untilDecision = 0f;   // 随时会动手
                }

                float level = 1f / (untilDecision + 0.1f);   // 越快动手越急
                if (level > threatLevel)
                {
                    threatLevel = level;
                    threat = door;
                }
            }

            if (threat == null)
            {
                // 门口没人 → 门开着省电，灯也关
                if (closed)
                {
                    ToggleDoor(side);
                    LastAction = side + " 无人，开门省电";
                }

                if (lightOn)
                {
                    ToggleLight(side);
                }

                return false;
            }

            // ── 门口有人 ──
            // 先关门（这是唯一能挡住的），关门之后关灯省电
            if (!closed)
            {
                ToggleDoor(side);

                // 关门时朝那侧偏一下头 —— 让玩家看见 AI「转头去关门」了
                PeekAtSide(side);

                LastAction = side + " 来人了，关门";
                return true;
            }

            // 门已关 → 立刻关灯。
            // 灯对「挡人」毫无帮助（门才是挡人的），开着纯烧电：
            // 每多一样开着的设备，耗电倍率就 +1 档。
            if (lightOn)
            {
                ToggleLight(side);
            }

            return true;
        }

        /// <summary>噜噜是不是正站在门口、还没被闪够。</summary>
        private bool LuluNeedsFlash()
        {
            var lulu = _director != null
                ? _director.GetBrain(EnemyId.Lulu) as LuluBrain
                : null;

            if (lulu == null || lulu.IsAttacking)
            {
                return false;
            }

            return lulu.Stage >= lulu.MaxStage && lulu.FlashCount < LuluFlashThreshold;
        }

        /// <summary>正门噜噜 —— 用手电闪它。</summary>
        private void HandleLulu(float dt)
        {
            var lulu = _director.GetBrain(EnemyId.Lulu) as LuluBrain;
            if (lulu == null)
            {
                SetFlashlight(false);
                SetFrontViewHold(false);
                return;
            }

            bool atDoor = lulu.Stage >= lulu.MaxStage;
            bool needsFlash = lulu.FlashCount < LuluFlashThreshold;

            // ⚠️ 视角接管的唯一判据走 AutoPlayerViewPolicy（有单元测试盯着）：
            //   只有「噜噜堵在正门 + 还没闪够 + 没在扑人」这一段才回正，
            //   其余时候视角完全归玩家（用户 2026-11-03 定）。
            SetFrontViewHold(AutoPlayerViewPolicy.ShouldHoldView(
                atDoor, lulu.FlashCount, LuluFlashThreshold, lulu.IsAttacking));

            if (!atDoor || !needsFlash || lulu.IsAttacking)
            {
                if (_luluFlashing)
                {
                    SetFlashlight(false);
                    _luluFlashing = false;
                }

                return;
            }

            // 手电只在「监控放下 + 视角朝正前方」时才算数
            // （EnemyContext.IsShiningAtFront）—— 视角在闪之前接管回正，
            // 监控在 Think 里 LuluNeedsFlash 那条分支已经放下了。

            // 站到门口 + 还没闪够 → 开手电闪它
            // 提前闪没用（要它站到门口才算），所以只在 atDoor 时闪

            // ⚠️ 用 unscaledTime 而不是自己累加 dt：
            // dt 只有 Update 里给，而闪光要跨很多帧才能被 LuluBrain 看到
            // （它只在「从灭到亮」的那一帧计数）。自己累加容易和实际帧不同步，
            // 改用绝对时钟，相位稳定。
            float now = Time.unscaledTime;

            // 闪 0.22 秒、灭 0.18 秒 —— 一轮 0.4 秒，闪够 4 次约 1.6 秒。
            // 不能更快：LuluBrain 靠「上升沿」计数（_wasShining 从 false 变 true），
            // 一开一关必须各撑过至少一帧，否则它根本看不到这次闪光。
            bool shouldBeOn = (now % 0.4f) < 0.22f;

            if (shouldBeOn != _luluFlashing)
            {
                SetFlashlight(shouldBeOn);
                _luluFlashing = shouldBeOn;
                LastAction = "闪噜噜 " + lulu.FlashCount + "/" + LuluFlashThreshold;
            }
        }

        /// <summary>抬头扫监控 —— 主要目的是看住奶龙，别让它一直偷电。</summary>
        /// <summary>
        /// 监控节奏：**隔一阵抬头看一眼，看完就放下**。
        ///
        /// 为什么不能一直挂着：监控开着就是耗电倍率 +1 档
        /// （GameManager.CountUsageLevel 数「有几样设备开着」）。
        /// 一直挂着一夜要 80%+ 电，撑不到天亮。
        ///
        /// 抬头的唯一目的是「打断奶龙偷电」—— 它偷一次掉 1% 电，
        /// 而且被看到才停手。所以：到点了 → 抬头 → 跳到它所在机位 → 看一眼 → 立刻放下。
        /// </summary>
        private void HandleMonitor(float dt)
        {
            var nailong = _director.GetBrain(EnemyId.Nailong) as NailongBrain;

            // ── 判断「现在开监控还有没有意义」──
            //
            // 奶龙只有**偷电态**才需要盯：被看到才停手换位。
            // 一旦转成**进攻态**，它就不再偷电了 —— 这时开监控纯属白烧电
            // （监控每开着 = 耗电倍率 +1 档），而且它在走右门、看监控也帮不上忙。
            //
            // 所以：进攻态 / 已经到门口 / 已经在扑 → 一律不开监控。
            bool nailongStealing = nailong != null
                && nailong.Mode == NailongMode.Stealing
                && !nailong.IsAttacking;

            if (!nailongStealing)
            {
                // 没有需要盯的目标 → 收起监控，一直保持最省电
                if (_gm.MonitorUp)
                {
                    SetMonitor(false);
                    LastAction = "奶龙已进攻，收起监控";
                }
                else
                {
                    LastAction = "无需监控，保持省电";
                }

                _monitorSweepTimer = 0f;
                _lookTimer = 0f;
                return;
            }

            if (!_gm.MonitorUp)
            {
                _monitorSweepTimer += dt;

                if (_monitorSweepTimer >= MonitorSweepInterval)
                {
                    _monitorSweepTimer = 0f;
                    _lookTimer = 0f;
                    SetMonitor(true);
                    LastAction = "抬头看奶龙";
                }

                return;
            }

            // ── 已抬头：把看的时间单独计时，到点就放下 ──
            _lookTimer += dt;

            // 跳到它所在的机位 —— 被看到它才停手换位
            if (_gm.CurrentCameraIndex != nailong.CameraIndex)
            {
                _gm.SetCamera(nailong.CameraIndex);
            }

            LastAction = "盯住奶龙 CAM" + (nailong.CameraIndex + 1);

            if (_lookTimer >= MonitorLookSeconds)
            {
                // 看完立刻放下，省电
                SetMonitor(false);
                _lookTimer = 0f;
                LastAction = "放下监控省电";
            }
        }

        /// <summary>这一轮抬头已经看了多久。</summary>
        private float _lookTimer;

        // ---------------------------------------------------------------
        // 底层操作
        // ---------------------------------------------------------------

        // ⚠️ 必须走 DoorLightActions，不能直接调 GameManager.ToggleXxx ——
        // 那个是纯状态切换，不带音效。早先这里直接调 GameManager，
        // 结果 AI 关门/开灯全程静音（2026-11-04 报的）。
        // 音效由 DoorLightActions 统一放，玩家点按钮走的也是同一条路。

        private void ToggleDoor(Side side)
        {
            DoorLightActions.ToggleDoor(_gm, side, _audio);
        }

        private void ToggleLight(Side side)
        {
            DoorLightActions.ToggleLight(_gm, side, _audio);
        }

        private void SetMonitor(bool up)
        {
            if (_monitorIntent != null)
            {
                _monitorIntent.SetValue(_gm, up);
            }
            else if (_toggleMonitor != null && _gm.MonitorUp != up)
            {
                _toggleMonitor.Invoke(_gm, null);
            }
        }

        /// <summary>手电的当前意图 —— 每帧都要重新下达，见下面的注释。</summary>
        private bool _wantFlashlight;

        private void SetFlashlight(bool on)
        {
            _wantFlashlight = on;
        }

        /// <summary>
        /// 把「手电意图」真的写进去。
        ///
        /// ⚠️ **必须每帧调**：PrototypeScene.ApplyFlashlight 每帧都会用
        /// 「鼠标 / Ctrl 是否按住」覆盖 gm.FlashlightOn ——
        /// 外面设一次 true，下一帧就被它改回 false。所以 AI 只能每帧抢着写，
        /// 和玩家按住时那条路径并行。（AI 不碰鼠标，所以不会互相打架多次。）
        /// </summary>
        private void ApplyFlashlightIntent()
        {
            if (_gm == null)
            {
                return;
            }

            // 一口气写进「AI 想要的手电状态」——
            // PrototypeScene.ApplyFlashlight 在接管模式下会照它去设 gm.FlashlightOn，
            // 不再被鼠标状态覆盖。
            if (_scene != null)
            {
                _scene.ExternalFlashlightOn = _wantFlashlight;
            }
        }
    }
}
