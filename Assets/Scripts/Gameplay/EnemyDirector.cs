using System;
using System.Collections.Generic;
using NailongFNAF.Core;
using UnityEngine;

namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 敌人总调度：把四个 <see cref="EnemyBrain"/> 挂到游戏状态上跑。
    ///
    /// 每帧做三件事：
    ///   1. 从 GameManager 读一份玩家状态快照（门 / 灯 / 监控 / 视角）
    ///   2. 推进四个 AI
    ///   3. 把奶龙的偷电量交给电量系统，把「有人动手了」通过事件抛出去（接跳杀）
    ///
    /// AI 本身是纯逻辑类，这里只负责喂数据与转发结果。
    /// </summary>
    public class EnemyDirector : MonoBehaviour
    {
        [Header("夜")]
        [Tooltip("当前是第几夜（0 起）。0 = 第 1 夜，5 = 隐藏夜。")]
        [SerializeField] private int _nightIndex;

        [Header("监控")]
        [Tooltip("监控机位数量 —— 奶龙会在这几个机位之间换位偷电。"
            + "必须与 PrototypeScene.CameraNames 的长度一致（照官方平面图是 11 个）。")]
        [SerializeField] private int _cameraCount = 11;

        [Tooltip("奶龙**不去**的机位（下标）。厨房（CAM 6，下标 9）只有声音、没有画面，"
            + "奶龙不走那里。留空表示 11 个机位它都去。")]
        [SerializeField] private int[] _nailongSkipCameras = { 9 };

        [Header("门类敌人的机位路径（照 FNAF 1 逐房间推进）")]
        [Tooltip("袋鼠（左门，照邦尼）沿路停留的机位下标。走到路径尽头就是左门口。"
            + "下标照 PrototypeScene.CameraNames：0=1A舞台 1=1B餐厅 5=3储藏室 3=2A西走廊 4=2B西走廊转角。"
            + "**起点放 0（1A 舞台）是有意的** —— 照 FNAF 1，舞台本来就该站着玩偶；"
            + "没出动 / 被挡回来的敌人回舞台待命，玩家看 1A 能知道谁还没出发。")]
        [SerializeField] private int[] _daishuPath = { 0, 1, 5, 3, 4 };

        [Tooltip("牛来（右门，照奇卡）沿路停留的机位下标。走到路径尽头就是右门口。"
            + "下标照 PrototypeScene.CameraNames：0=1A舞台 1=1B餐厅 10=7厕所 6=4A东走廊 7=4B东走廊转角。"
            + "同样从舞台出发 —— 见上。")]
        [SerializeField] private int[] _niulaiPath = { 0, 1, 10, 6, 7 };

        [Tooltip("奶龙（右门，照弗莱迪）沿路停留的机位下标。走到路径尽头就是右门口。"
            + "弗莱迪 FNAF 1 里从舞台 → 餐厅 → 东走廊 → 东转角逼近右门："
            + "下标照 PrototypeScene.CameraNames：1=1B餐厅 6=4A东走廊 7=4B东走廊转角。"
            + "⚠️ 奶龙不是靠「每走一步停留一个机位」的方式偷电的 —— 它有自己的 "
            + "CameraIndex（蹲点偷电）。这条路只是让它的推进在监控里可见："
            + "它沿路径前进时，对应机位能看到它。")]
        [SerializeField] private int[] _nailongPath = { 1, 6, 7 };

        [Header("门类敌人动手前的反应窗口（照原版 FNAF 1 的移动机会间隔）")]
        [Tooltip("袋鼠（照邦尼）到门口后，门开着撑多久就动手。原版邦尼移动机会 4.97s。")]
        [SerializeField] private float _daishuDoorGraceSeconds = DoorEnemyBrain.BonnieDoorWindowSeconds;

        [Tooltip("牛来（照奇卡）到门口后，门开着撑多久就动手。原版奇卡移动机会 4.98s。")]
        [SerializeField] private float _niulaiDoorGraceSeconds = DoorEnemyBrain.ChicaDoorWindowSeconds;

        [Header("噜噜（照 FNAF 2 的狐狸：不停地按手电筒闪它）")]
        [Tooltip("噜噜的出生地与推进路径（下标）。照 FNAF 1 的狐狸住在海盗湾，"
            + "2=1C海盗湾 1=1B餐厅，走完就堵到正门口。"
            + "配了路径之后它推进时会在这些机位现形（用 peek 探头贴图）。")]
        [SerializeField] private int[] _luluPath = { 2, 1 };

        [Tooltip("闪够几次它就退走。**按次数算** —— 按住不放只算一次，得松手再按。")]
        [SerializeField] private int _luluFlashesToRetreat = LuluBrain.DefaultFlashesToRetreat;

        [Tooltip("两次闪光之间最多能隔多久：活动度 0 时（秒）—— 最宽松。活动度越高，这个时间越短。")]
        [SerializeField] private float _luluGraceAtZeroActivity = LuluBrain.GraceAtZeroActivity;

        [Tooltip("两次闪光之间最多能隔多久：活动度满级时（秒）—— 最紧。")]
        [SerializeField] private float _luluGraceAtMaxActivity = LuluBrain.GraceAtMaxActivity;

        [Header("奶龙")]
        [Tooltip("偷电间隔：活动度 0 时（秒）。活动度越高偷得越勤。")]
        [SerializeField] private float _nailongSlowStealSeconds = NailongBrain.DefaultSlowStealIntervalSeconds;

        [Tooltip("偷电间隔：活动度满级时（秒）。")]
        [SerializeField] private float _nailongFastStealSeconds = NailongBrain.DefaultFastStealIntervalSeconds;

        [Tooltip("每次偷电扣多少百分点。")]
        [SerializeField] private float _nailongStealPercent = NailongBrain.DefaultStealPercentPerTick;

        [Tooltip("被监控照到后愣多久才换位（秒）—— 给玩家看清它的时间。")]
        [SerializeField] private float _nailongRevealSeconds = NailongBrain.DefaultRevealSeconds;

        [Tooltip("奶龙摸到右门后，门开着撑多久就动手（秒）。")]
        [SerializeField] private float _nailongDoorGraceSeconds = NailongBrain.FreddyDoorWindowSeconds;

        [Header("断电（照原版分三段走）")]
        [Tooltip("断电到它现身之间的静默（秒）—— 一片漆黑，什么也看不见。")]
        [SerializeField] private float _blackoutSilenceSeconds = BlackoutSequence.DefaultSilenceSeconds;

        [Tooltip("音乐盒的长度（秒）。运行时会按实际音频长度自动覆盖。")]
        [SerializeField] private float _blackoutMusicSeconds = BlackoutSequence.DefaultMusicSeconds;

        [Tooltip("音乐停下之后、动手之前的漆黑（秒）—— 只剩一个勉强能辨认的轮廓。")]
        [SerializeField] private float _blackoutDarkSeconds = BlackoutSequence.DefaultDarkSeconds;

        /// <summary>某个敌人动手了。参数是敌人代号，用来挑对应的跳杀视频 / 惊吓图。</summary>
        public event Action<EnemyId> Attacked;

        private readonly Dictionary<EnemyId, EnemyBrain> _brains = new Dictionary<EnemyId, EnemyBrain>();

        private NightDifficulty _difficulty;
        private IRandomSource _rng;
        private NailongBrain _nailong;
        private BlackoutSequence _blackout;
        private float _blackoutMusicSecondsOverride;
        private GameManager _game;
        private bool _attackFired;

        /// <summary>断电之后的那一段（奶龙收电费）。</summary>
        public BlackoutSequence Blackout
        {
            get { return _blackout; }
        }

        /// <summary>
        /// 按实际音乐素材的长度校准断电演出的音乐段 —— 音乐播完就该进漆黑段。
        /// 由 PrototypeScene 拿到 AudioManager 之后调用；没素材就保持 Inspector 里的值。
        /// 换夜会重建序列，所以这里把值记下来，<see cref="Build"/> 时重新应用。
        /// </summary>
        public void SetBlackoutMusicSeconds(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            _blackoutMusicSecondsOverride = seconds;

            if (_blackout != null)
            {
                _blackout.SetMusicSeconds(seconds);
            }
        }

        public int NightIndex
        {
            get { return _nightIndex; }
        }

        public int CameraCount
        {
            get { return _cameraCount; }
        }

        public NightDifficulty Difficulty
        {
            get { return _difficulty; }
        }

        /// <summary>是否已经有敌人动手（动手后整场停摆，等跳杀流程处理）。</summary>
        public bool AttackFired
        {
            get { return _attackFired; }
        }

        private void Awake()
        {
            Build();
        }

        private void Start()
        {
            _game = GameManager.Instance;
            if (_game != null)
            {
                _game.Restarted += OnGameRestarted;
            }
        }

        private void OnDestroy()
        {
            if (_game != null)
            {
                _game.Restarted -= OnGameRestarted;
            }
        }

        /// <summary>重建四个敌人。首次进入与换夜都会走这里。</summary>
        public void Build()
        {
            _rng = new SystemRandomSource();
            _difficulty = NightDifficulty.CreateDefault();
            _attackFired = false;
            _brains.Clear();

            float musicSeconds = _blackoutMusicSecondsOverride > 0f
                ? _blackoutMusicSecondsOverride
                : _blackoutMusicSeconds;

            _blackout = new BlackoutSequence(_blackoutSilenceSeconds,
                musicSeconds, _blackoutDarkSeconds,
                BlackoutSequence.DefaultBeatSeconds);

            // 边的分配照 FNAF 1：邦尼走左门、奇卡走右门、弗莱迪也从右门来。
            // 对应到我们这四个 —— 袋鼠 = 邦尼（左）、牛来 = 奇卡（右）、奶龙 = 弗莱迪（右）。
            // 照 FNAF 1：邦尼/奇卡不是三档跳，而是沿固定机位路径逐房间推进。
            // 路径在 Inspector 上配（_daishuPath / _niulaiPath），走到尽头就是门口。
            _brains[EnemyId.Daishu] = new DoorEnemyBrain(EnemyId.Daishu, Side.Left, 0, _rng,
                EnemyBrain.DefaultMoveIntervalSeconds,
                _daishuDoorGraceSeconds,
                _daishuPath);
            _brains[EnemyId.Niulai] = new DoorEnemyBrain(EnemyId.Niulai, Side.Right, 0, _rng,
                EnemyBrain.DefaultMoveIntervalSeconds,
                _niulaiDoorGraceSeconds,
                _niulaiPath);

            var lulu = new LuluBrain(EnemyId.Lulu, 0, _rng,
                EnemyBrain.DefaultMoveIntervalSeconds,
                _luluFlashesToRetreat, 0f);
            lulu.SetGraceRange(_luluGraceAtZeroActivity, _luluGraceAtMaxActivity);
            lulu.SetCameraPath(_luluPath);
            _brains[EnemyId.Lulu] = lulu;

            _nailong = new NailongBrain(0, _rng, NailongCameras(),
                _nailongSlowStealSeconds,
                _nailongFastStealSeconds,
                _nailongStealPercent,
                _nailongRevealSeconds,
                _nailongDoorGraceSeconds);
            _nailong.SetCameraPath(_nailongPath);
            _brains[EnemyId.Nailong] = _nailong;

            ApplyDifficulty();
        }

        /// <summary>
        /// 奶龙能去的机位表 —— 从 0 ~ <see cref="_cameraCount"/>-1 里剔掉
        /// <see cref="_nailongSkipCameras"/>。一个都没剔时返回 null，
        /// 让 <see cref="NailongBrain"/> 用默认的「整段机位」。
        /// </summary>
        private int[] NailongCameras()
        {
            if (_nailongSkipCameras == null || _nailongSkipCameras.Length == 0)
            {
                return null;
            }

            var cameras = new List<int>();

            for (int i = 0; i < _cameraCount; i++)
            {
                if (Array.IndexOf(_nailongSkipCameras, i) < 0)
                {
                    cameras.Add(i);
                }
            }

            return cameras.Count > 0 ? cameras.ToArray() : null;
        }

        /// <summary>换夜：重设活动度。</summary>
        public void SetNight(int nightIndex)
        {
            if (nightIndex < 0)
            {
                nightIndex = 0;
            }

            if (nightIndex >= NightDifficulty.NightCount)
            {
                nightIndex = NightDifficulty.NightCount - 1;
            }

            _nightIndex = nightIndex;
            ApplyDifficulty();
        }

        public EnemyBrain GetBrain(EnemyId id)
        {
            EnemyBrain brain;
            return _brains.TryGetValue(id, out brain) ? brain : null;
        }

        public IEnumerable<EnemyBrain> Brains
        {
            get { return _brains.Values; }
        }

        private void ApplyDifficulty()
        {
            foreach (var pair in _brains)
            {
                pair.Value.SetActivityLevel(_difficulty.GetLevel(_nightIndex, pair.Key));
            }
        }

        /// <summary>右门口现在站着谁（没人就 null）。</summary>
        private EnemyId? FindRightDoorHolder()
        {
            foreach (var pair in _brains)
            {
                var door = pair.Value as DoorEnemyBrain;

                if (door != null && door.Side == Side.Right
                    && !door.IsAttacking && door.Stage >= door.MaxStage)
                {
                    return pair.Key;
                }
            }

            return null;
        }

        private void OnGameRestarted()
        {
            Build();
        }

        private void Update()
        {
            var game = GameManager.Instance;
            if (game == null || game.Power == null || game.Clock == null)
            {
                return;
            }

            // 天亮、或已经有人动手 —— 整场停摆
            if (game.HasSurvived || _attackFired)
            {
                return;
            }

            float dt = Time.deltaTime;

            // 断电之后只剩奶龙：它来「收电费」。
            // 期间时钟照走 —— 撑到 6 点就算躲过去（GameManager 会让本帧提前 return）。
            if (game.HasPowerOut)
            {
                if (!_blackout.IsRunning)
                {
                    _blackout.Begin();
                }

                _blackout.Tick(dt);

                if (_blackout.HasFired)
                {
                    _attackFired = true;
                    game.MarkCaught();

                    if (Attacked != null)
                    {
                        Attacked(EnemyId.Nailong);
                    }
                }

                return;
            }

            _blackout.Reset();

            foreach (var pair in _brains)
            {
                // 每个敌人动手前重算一次门口占用 —— 同一帧里先到的先占住，
                // 后到的看到门口站着人就留在走廊等（右门是奶龙与牛来的共用通道）
                var ctx = new EnemyContext(
                    game.LeftDoorClosed, game.RightDoorClosed,
                    game.LeftLightOn, game.RightLightOn,
                    game.MonitorUp, game.CurrentView, game.CurrentCameraIndex,
                    FindRightDoorHolder(), game.FlashlightOn);

                pair.Value.Tick(dt, ctx);
            }

            // 奶龙偷电
            float drain = _nailong.ConsumePendingDrain();
            if (drain > 0f)
            {
                game.Power.DrainAmount(drain);
            }

            // 有人动手 → 冻结本夜，抛事件（一次只抛一个）
            foreach (var pair in _brains)
            {
                if (pair.Value.IsAttacking)
                {
                    _attackFired = true;
                    game.MarkCaught();

                    if (Attacked != null)
                    {
                        Attacked(pair.Key);
                    }

                    break;
                }
            }
        }
    }
}
