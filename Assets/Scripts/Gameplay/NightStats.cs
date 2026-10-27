namespace NailongFNAF.Gameplay
{
    /// <summary>
    /// 一夜的操作账本：门/灯开关了几次、看没看过监控、机位走了几个、
    /// 在厨房机位上待了多久、和奶龙对了几次眼、死在几点。
    ///
    /// 只服务「彩蛋成就」的判定（见 <see cref="AchievementCatalog"/>）。
    /// 纯 C#、不引用 UnityEngine —— EditMode 里可以直接 new 出来测，
    /// 每夜由 <see cref="NailongFNAF.Core.GameManager"/> 新建一份。
    /// </summary>
    public sealed class NightStats
    {
        /// <summary>
        /// 厨房机位（CAM 6）的下标。**必须与 PrototypeScene 的机位表一致**，
        /// 那边直接引用这个常量，别再写一份数字。
        /// </summary>
        public const int KitchenCameraIndex = 9;

        /// <summary>机位表最多支持多少个（位掩码用 int 存）。</summary>
        public const int MaxCameras = 32;

        /// <summary>门被切换了几次（开→关、关→开都算一次）。</summary>
        public int DoorToggles { get; private set; }

        /// <summary>灯被切换了几次。</summary>
        public int LightToggles { get; private set; }

        /// <summary>监控升起/放下的次数。</summary>
        public int MonitorToggles { get; private set; }

        /// <summary>切机位的次数（不含第一次进入）。</summary>
        public int CameraChanges { get; private set; }

        /// <summary>和奶龙「对上眼」的次数（它从没被看到变成被看到的次数）。</summary>
        public int NailongLooks { get; private set; }

        /// <summary>在厨房机位上待了多少秒（监控升起、切到 CAM 6 才算）。</summary>
        public float KitchenSeconds { get; private set; }

        /// <summary>这一夜结束（天亮或被抓）时的游戏内秒数；没结束是 0。</summary>
        public float EndedAtSeconds { get; private set; }

        /// <summary>这一夜结束在第几个小时（0 起，1.0 = 凌晨 1 点）；没结束是 0。</summary>
        public float EndedAtHour { get; private set; }

        /// <summary>这一夜是否已经结束（判定彩蛋时要它已经结束）。</summary>
        public bool HasEnded { get; private set; }

        /// <summary>这一夜遇见贝利亚几次（贝利亚之夜用得上）。</summary>
        public int BelialMeetings { get; private set; }

        /// <summary>这一夜一共把贝利亚照成「白贝」几下。</summary>
        public int BelialFlashes { get; private set; }

        /// <summary>这一夜是不是死在贝利亚手上。</summary>
        public bool KilledByBelial { get; private set; }

        private int _visitedMask;

        /// <summary>门 + 灯的切换总数（「手指的节奏」用）。</summary>
        public int TotalSwitchToggles
        {
            get { return DoorToggles + LightToggles; }
        }

        /// <summary>看过几个机位。</summary>
        public int VisitedCameraCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < MaxCameras; i++)
                {
                    if ((_visitedMask & (1 << i)) != 0)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void NoteDoorToggle()
        {
            DoorToggles++;
        }

        public void NoteLightToggle()
        {
            LightToggles++;
        }

        public void NoteMonitorToggle()
        {
            MonitorToggles++;
        }

        /// <summary>记一个机位「看过了」。</summary>
        public void NoteCamera(int index)
        {
            if (index < 0 || index >= MaxCameras)
            {
                return;
            }

            _visitedMask |= 1 << index;
            CameraChanges++;
        }

        public void NoteNailongLook()
        {
            NailongLooks++;
        }

        /// <summary>遇见贝利亚一次（他出现在门外的那一刻记一次）。</summary>
        public void NoteBelialMeeting()
        {
            BelialMeetings++;
        }

        /// <summary>把贝利亚照成「白贝」一下。</summary>
        public void NoteBelialFlash()
        {
            BelialFlashes++;
        }

        /// <summary>这一夜是贝利亚收的场。</summary>
        public void NoteBelialDeath()
        {
            KilledByBelial = true;
        }

        /// <summary>某一机位是否看过。</summary>
        public bool VisitedCamera(int index)
        {
            if (index < 0 || index >= MaxCameras)
            {
                return false;
            }

            return (_visitedMask & (1 << index)) != 0;
        }

        /// <summary>前 <paramref name="cameraCount"/> 个机位是不是全看过。</summary>
        public bool HasVisitedAll(int cameraCount)
        {
            if (cameraCount <= 0 || cameraCount > MaxCameras)
            {
                return false;
            }

            for (int i = 0; i < cameraCount; i++)
            {
                if ((_visitedMask & (1 << i)) == 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>每帧推进厨房停留计时（监控没升起、或不在厨房就什么也不做）。</summary>
        public void Tick(float dt, bool monitorUp, int cameraIndex)
        {
            if (!monitorUp || dt <= 0f)
            {
                return;
            }

            if (cameraIndex == KitchenCameraIndex)
            {
                KitchenSeconds += dt;
            }
        }

        /// <summary>
        /// 这一夜结束：记下结束时刻。
        /// <paramref name="secondsPerHour"/> 是游戏内一小时对应多少真实秒
        /// （默认 85 秒，见 NightClock.DefaultSecondsPerHour），不传就当 85。
        /// </summary>
        public void MarkEnded(float elapsedSeconds, float secondsPerHour)
        {
            HasEnded = true;

            float seconds = elapsedSeconds < 0f ? 0f : elapsedSeconds;
            EndedAtSeconds = seconds;

            float perHour = secondsPerHour > 0f ? secondsPerHour : 85f;
            EndedAtHour = seconds / perHour;
        }

        /// <summary>结束：游戏内一小时按默认 85 秒算（同 NightClock.DefaultSecondsPerHour）。</summary>
        public void MarkEnded(float elapsedSeconds)
        {
            MarkEnded(elapsedSeconds, 85f);
        }

        /// <summary>结束时刻是否已经过了第 <paramref name="hour"/> 小时（5.833 = 5:50）。</summary>
        public bool EndedAfterHour(float hour)
        {
            return HasEnded && EndedAtHour >= hour;
        }

        /// <summary>结束时刻是否还没到第 <paramref name="hour"/> 小时。</summary>
        public bool EndedBeforeHour(float hour)
        {
            return HasEnded && EndedAtHour < hour;
        }
    }
}
