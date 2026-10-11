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
        [Tooltip("偷电间隔（秒）。")]
        [SerializeField] private float _nailongStealIntervalSeconds = NailongBrain.DefaultStealIntervalSeconds;

        [Tooltip("每次偷电扣多少百分点。")]
        [SerializeField] private float _nailongStealPercent = NailongBrain.DefaultStealPercentPerTick;

        /// <summary>某个敌人动手了。参数是敌人代号，用来挑对应的跳杀视频 / 惊吓图。</summary>
        public event Action<EnemyId> Attacked;

        private readonly Dictionary<EnemyId, EnemyBrain> _brains = new Dictionary<EnemyId, EnemyBrain>();

        private NightDifficulty _difficulty;
        private IRandomSource _rng;
        private NailongBrain _nailong;
        private GameManager _game;
        private bool _attackFired;

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

            _brains[EnemyId.Niulai] = new DoorEnemyBrain(EnemyId.Niulai, Side.Left, 0, _rng);
            _brains[EnemyId.Daishu] = new DoorEnemyBrain(EnemyId.Daishu, Side.Right, 0, _rng);
            _brains[EnemyId.Lulu] = new LuluBrain(EnemyId.Lulu, 0, _rng);

            _nailong = new NailongBrain(0, _rng, _cameraCount,
                EnemyBrain.DefaultMoveIntervalSeconds,
                _nailongStealIntervalSeconds,
                _nailongStealPercent);
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

            // 断电、天亮、已经动手 —— 都不再推进
            if (game.HasPowerOut || game.HasSurvived || _attackFired)
            {
                return;
            }

            var ctx = new EnemyContext(
                game.LeftDoorClosed, game.RightDoorClosed,
                game.LeftLightOn, game.RightLightOn,
                game.MonitorUp, game.CurrentView, game.CurrentCameraIndex);

            float dt = Time.deltaTime;

            foreach (var pair in _brains)
            {
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
