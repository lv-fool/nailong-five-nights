using System.Collections.Generic;
using UnityEngine;
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

        /// <summary>最低保电：低于这个值就开始省电，不再为了「看」而开灯。</summary>
        private const float LowPowerThreshold = 25f;

        /// <summary>远程扫监控的间隔（秒）—— 太频繁会漏掉门口。</summary>
        private const float MonitorSweepInterval = 1.6f;

        /// <summary>噜噜还差几下才退场时，才开始闪（提前闪浪费）。</summary>
        private const int LuluFlashThreshold = 4;

        private GameManager _gm;
        private EnemyDirector _director;
        private PrototypeScene _scene;

        private bool _running;

        private float _monitorSweepTimer;
        private int _sweepCamera;

        private float _luluFlashTimer;
        private bool _luluFlashing;

        // 反射缓存 —— 每帧都 GetMethod 太慢
        private System.Reflection.MethodInfo _toggleMonitor;
        private System.Reflection.MethodInfo _setFlashlight;
        private System.Reflection.FieldInfo _monitorIntent;

        private static readonly System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;

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
                }

                return;
            }

            Think(Time.deltaTime);
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
                LastAction = side + " 来人了，关门";
                return true;
            }

            // 门已关：关灯省电（除非电量很足、想看它走没走）
            if (lightOn && lowPower)
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
                return;
            }

            bool atDoor = lulu.Stage >= lulu.MaxStage;
            bool needsFlash = lulu.FlashCount < LuluFlashThreshold;

            if (!atDoor || !needsFlash || lulu.IsAttacking)
            {
                if (_luluFlashing)
                {
                    SetFlashlight(false);
                    _luluFlashing = false;
                }

                return;
            }

            // ⚠️ 手电只在「监控放下 + 视角朝正前方」时才算数
            // （EnemyContext.IsShiningAtFront）。视角不对闪一辈子也没用。
            if (_gm.CurrentView != ViewSide.Front)
            {
                _gm.SetView(ViewSide.Front);
            }

            // 站到门口 + 还没闪够 → 开手电闪它
            // 提前闪没用（要它站到门口才算），所以只在 atDoor 时闪
            _luluFlashTimer += dt;

            // 闪 0.25 秒、灭 0.2 秒，循环。
            // 节奏别太慢：噜噜在门口最多等 OutsideGraceSeconds（活动度满级才 7.5 秒），
            // 要在这段时间里闪够 4 次。0.45 秒一轮 → 4 次约 1.8 秒，够用。
            bool shouldBeOn = (_luluFlashTimer % 0.45f) < 0.25f;

            if (shouldBeOn != _luluFlashing)
            {
                SetFlashlight(shouldBeOn);
                _luluFlashing = shouldBeOn;
                LastAction = "闪噜噜 " + lulu.FlashCount + "/" + LuluFlashThreshold;
            }
        }

        /// <summary>抬头扫监控 —— 主要目的是看住奶龙，别让它一直偷电。</summary>
        private void HandleMonitor(float dt)
        {
            _monitorSweepTimer += dt;

            if (!_gm.MonitorUp)
            {
                // 放下时计时，到点抬头
                if (_monitorSweepTimer >= MonitorSweepInterval)
                {
                    _monitorSweepTimer = 0f;
                    SetMonitor(true);
                    LastAction = "抬头扫监控";
                }

                return;
            }

            // 已抬头：切到下个机位，找奶龙
            _monitorSweepTimer += 0f;

            var nailong = _director.GetBrain(EnemyId.Nailong) as NailongBrain;
            if (nailong != null && nailong.Mode == NailongMode.Stealing)
            {
                // 跳到它所在的机位，看一眼让它停手
                if (_gm.CurrentCameraIndex != nailong.CameraIndex)
                {
                    _gm.SetCamera(nailong.CameraIndex);
                }

                LastAction = "看住奶龙 @CAM" + (nailong.CameraIndex + 1);
            }
            else
            {
                // 没事就看一圈
                if (_monitorSweepTimer >= MonitorSweepInterval * 2f)
                {
                    _monitorSweepTimer = 0f;
                    SetMonitor(false);
                }
            }
        }

        // ---------------------------------------------------------------
        // 底层操作
        // ---------------------------------------------------------------

        private void ToggleDoor(Side side)
        {
            if (side == Side.Left)
            {
                _gm.ToggleLeftDoor();
            }
            else
            {
                _gm.ToggleRightDoor();
            }
        }

        private void ToggleLight(Side side)
        {
            if (side == Side.Left)
            {
                _gm.ToggleLeftLight();
            }
            else
            {
                _gm.ToggleRightLight();
            }
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
            if (_gm == null || _setFlashlight == null)
            {
                return;
            }

            if (_gm.FlashlightOn != _wantFlashlight)
            {
                _setFlashlight.Invoke(_gm, new object[] { _wantFlashlight });
            }
        }
    }
}
