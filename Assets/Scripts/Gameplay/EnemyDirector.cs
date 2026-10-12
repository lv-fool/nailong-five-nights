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
        [Tooltip("监控机位数量。默认 3：大厅 / 左走廊 / 右走廊。")]
        [SerializeField] private int _cameraCount = 3;

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
        [SerializeField] private float _nailongDoorGraceSeconds = NailongBrain.DefaultDoorGraceSeconds;

        [Header("断电")]
        [Tooltip("断电后奶龙来「收电费」，给玩家这么多秒。期间到 6 点就算撑过去。")]
        [SerializeField] private float _blackoutGraceSeconds = BlackoutSequence.DefaultGraceSeconds;

        /// <summary>某个敌人动手了。参数是敌人代号，用来挑对应的跳杀视频 / 惊吓图。</summary>
        public event Action<EnemyId> Attacked;

        private readonly Dictionary<EnemyId, EnemyBrain> _brains = new Dictionary<EnemyId, EnemyBrain>();

        private NightDifficulty _difficulty;
        private IRandomSource _rng;
        private NailongBrain _nailong;
        private BlackoutSequence _blackout;
        private GameManager _game;
        private bool _attackFired;

        /// <summary>断电之后的那一段（奶龙收电费）。</summary>
        public BlackoutSequence Blackout
        {
            get { return _blackout; }
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

            _blackout = new BlackoutSequence(_blackoutGraceSeconds);

            _brains[EnemyId.Niulai] = new DoorEnemyBrain(EnemyId.Niulai, Side.Left, 0, _rng);
            _brains[EnemyId.Daishu] = new DoorEnemyBrain(EnemyId.Daishu, Side.Right, 0, _rng);
            _brains[EnemyId.Lulu] = new LuluBrain(EnemyId.Lulu, 0, _rng);

            _nailong = new NailongBrain(0, _rng, _cameraCount,
                _nailongSlowStealSeconds,
                _nailongFastStealSeconds,
                _nailongStealPercent,
                _nailongRevealSeconds,
                _nailongDoorGraceSeconds);
            _brains[EnemyId.Nailong] = _nailong;

            ApplyDifficulty();
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
                // 后到的看到门口站着人就留在走廊等（右门是奶龙与袋鼠的共用通道）
                var ctx = new EnemyContext(
                    game.LeftDoorClosed, game.RightDoorClosed,
                    game.LeftLightOn, game.RightLightOn,
                    game.MonitorUp, game.CurrentView, game.CurrentCameraIndex,
                    FindRightDoorHolder());

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
