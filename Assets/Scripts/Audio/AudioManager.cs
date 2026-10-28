using UnityEngine;

namespace NailongFNAF.Audio
{
    /// <summary>
    /// 音频调度：环境底噪循环 + 操作音效。
    ///
    /// 素材全部留空也能跑（静音），不影响玩法。
    /// 素材由菜单「奶龙 → 5. 接入素材」自动接上，之后换素材重跑一次菜单即可。
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Header("环境音（循环）")]
        [SerializeField] private AudioClip _ambFan;
        [SerializeField] private AudioClip _ambHum;

        /// <summary>
        /// 偶尔响一下的远处环境声（金属碰撞、管道……）——**不循环**。
        ///
        /// 为什么不做成第三条循环轨：三条底噪叠在一起会变成嗡嗡一片，
        /// 反而听不出「远处有东西在动」。间歇响一下才吓人 ——
        /// 玩家会下意识转头去看，而那正好是这游戏想让你做的事。
        /// </summary>
        [SerializeField] private AudioClip _ambDistant;

        /// <summary>两次远处声之间隔多久（秒），随机取区间。</summary>
        private const float DistantMinGap = 25f;
        private const float DistantMaxGap = 70f;

        /// <summary>下一次该响的时刻（Time.time）。</summary>
        private float _nextDistantAt;

        /// <summary>远处声的独立轨道 —— 用 PlayOneShot，不跟主音效抢通道。</summary>
        private AudioSource _ambientC;

        [Header("音效")]
        [SerializeField] private AudioClip _sfxDoorClose;
        [SerializeField] private AudioClip _sfxDoorOpen;
        [SerializeField] private AudioClip _sfxLightOn;
        [SerializeField] private AudioClip _sfxLightOff;
        [SerializeField] private AudioClip _sfxCameraSwitch;
        [SerializeField] private AudioClip _sfxFootstep;

        /// <summary>
        /// 左右脚各一条 —— 交替播才有「有人在走过来」的方位感。
        /// 单声道一条重复放会像节拍器，玩家很快听腻。
        /// </summary>
        [SerializeField] private AudioClip _sfxFootstepLeft;
        [SerializeField] private AudioClip _sfxFootstepRight;

        /// <summary>
        /// 牛叫 —— 牛来进出厨房时响。
        ///
        /// 厨房（CAM 6）是官方那个**只有声音没有画面**的机位，
        /// 所以「他在里面」这件事只能靠听。进厨房原调、出厨房**降调**，
        /// 玩家不用看画面也能分辨他是在靠近还是走远了。
        /// </summary>
        [SerializeField] private AudioClip _sfxMoo;

        /// <summary>
        /// **出厨房**专用的那条牛叫。
        ///
        /// 早先是同一条素材降调（pitch 0.78）扮演「走远了」，
        /// 但降调会把音色一起扭曲，听起来像换了只怪物而不是同一只走远了。
        /// 用户另给了一条专门的「往外走」版本 —— 用它自然得多。
        /// </summary>
        [SerializeField] private AudioClip _sfxMooLeave;
        [SerializeField] private AudioClip _sfxPowerOut;

        /// <summary>
        /// 开灯那一刻**照到门口站着人**时的提示音。
        ///
        /// ⚠️ 用的素材是**截过只留一段的版本**（sfx_light_reveal_once.wav）——
        /// 原始 mp3 里有 8 组连续报警循环（周期 0.48 秒），
        /// 直接播会响完一整串，听着像「持续报警」而不是「一下提示」。
        /// 截取时末尾加了 15ms 淡出，避免截断处的咔哒声。
        ///
        /// 和普通「开灯」音效分开：普通开灯不管有没有人都响，
        /// 这个只在真的照到敌人的**上升沿**响一次（关着灯看不见人，开灯才发现）。
        ///
        /// ⚠️ 走 <see cref="Resources.Load{T}(string)"/> 而不是序列化字段 ——
        /// AudioManager 是运行时 AddComponent 造出来的（Main.unity 里没有它），
        /// 序列化字段永远是 null。素材放 Assets/Resources/Audio/sfx_light_reveal.mp3。
        /// </summary>
        private AudioClip _sfxLightReveal;

        /// <summary>提示音的 Resources 路径（不带扩展名）。</summary>
        private const string LightRevealClipPath = "Audio/sfx_light_reveal_once";

        /// <summary>开灯电音素材路径（Resources 下，不带扩展名）。</summary>
        private const string LightBuzzClipPath = "Audio/amb_light_buzz";

        /// <summary>
        /// 门 / 灯**被破坏后**点按钮时用的「默认音效」。
        ///
        /// 用 sfx_power_out（断电那个机械闷响）—— 它本来就在项目里、
        /// 干涩短促，很适合表达「按了但没反应」，也不必新增素材。
        /// </summary>
        private AudioClip DefaultDoorClip
        {
            get
            {
                if (_defaultDoorClip == null)
                {
                    _defaultDoorClip = Resources.Load<AudioClip>("Audio/sfx_power_out");
                }

                return _defaultDoorClip;
            }
        }

        private AudioClip _defaultDoorClip;

        // 开关门音效 —— 来自用户给的「关门.mp3」，一段音频里两个波：
        //   第一波（前）= 开门，第二波（后）= 关门，之后是无声音的尾巴，已切掉。
        // 切好的两个 wav 放在 Resources/Door/ 下。
        private const string DoorOpenClipPath = "Door/sfx_door_open";
        private const string DoorCloseClipPath = "Door/sfx_door_close";

        [Header("断电音乐（音乐盒，单独一条轨）")]
        [SerializeField] private AudioClip _powerOutMusic;

        [Header("音量")]
        [Range(0f, 1f)]
        [SerializeField] private float _ambientVolume = 0.30f;

        [Range(0f, 1f)]
        [SerializeField] private float _sfxVolume = 0.75f;

        [Range(0f, 1f)]
        [SerializeField] private float _musicVolume = 0.62f;

        private AudioSource _ambientA;
        private AudioSource _ambientB;
        private AudioSource _sfx;
        private AudioSource _music;

        /// <summary>
        /// 脚步专用通道 —— 和主音效分开。
        /// 脚步声一局要响几百次，而且必须能和门 / 灯 / 提示音**同时**响，
        /// 共用主通道会互相掐断。
        /// </summary>
        private AudioSource _footstepSource;

        /// <summary>牛叫专用通道 —— 它需要单独设 pitch，不能和别的音效共用。</summary>
        private AudioSource _mooSource;

        // ── 开灯电音 ──
        //
        // 灯亮着的时候要持续响，关灯立刻停 —— 所以是**循环音源**，
        // 不能走 PlayOneShot（那个只响一次）。
        //
        // 素材是程序生成的 50Hz 交流嗡鸣 + 8kHz 镇流器啸叫 + 电流沙沙，
        // 4 秒、首尾淡入淡出，循环无接缝。和常驻底噪（amb_electric_hum）
        // 是两回事 —— 那个一直在响，这个只在开灯时加进来。

        /// <summary>门音效专属通道（开门要二倍速，需要独立的 pitch）。</summary>
        private AudioSource _doorSource;

        /// <summary>开灯循环音源。</summary>
        private AudioSource _lightLoopSource;

        /// <summary>开灯电音的素材。</summary>
        private AudioClip _lightBuzz;

        /// <summary>开灯电音的音量（比底噪明显，但不盖过语音和音效）。</summary>
        private const float LightBuzzVolume = 0.34f;

        /// <summary>开灯电音的淡入 / 淡出时长（秒）—— 硬切会"啪"一声。</summary>
        private const float LightBuzzFadeSeconds = 0.22f;

        /// <summary>当前电音通道的目标音量（0 = 关灯）。</summary>
        private float _lightBuzzTarget;

        private void Awake()
        {
            _ambientA = CreateSource("AmbientA", true, _ambientVolume);
            _ambientB = CreateSource("AmbientB", true, _ambientVolume);

            // 远处声单独一条轨：**不循环**，音量比底噪略低一点，
            // 让它听起来确实「在远处」。
            _ambientC = CreateSource("AmbientC", false, _ambientVolume * 0.8f);

            _sfx = CreateSource("Sfx", false, _sfxVolume);
            _music = CreateSource("PowerOutMusic", false, _musicVolume);
            _footstepSource = CreateSource("Footstep", false, _sfxVolume);
            _mooSource = CreateSource("Moo", false, _sfxVolume);

            // 开关门音效：**优先用 Resources 里的新素材**。
            //
            // ⚠️ 不能写成「场景没绑才兜底」—— Main.unity 里绑的是旧的
            // sfx_door_open.ogg / sfx_door_close.ogg 的 GUID，那样永远走不到新素材。
            // 换素材时这里不动场景也能生效。
            var newOpen = Resources.Load<AudioClip>(DoorOpenClipPath);
            if (newOpen != null)
            {
                _sfxDoorOpen = newOpen;
            }

            var newClose = Resources.Load<AudioClip>(DoorCloseClipPath);
            if (newClose != null)
            {
                _sfxDoorClose = newClose;
            }

            // 门音效独立通道：开门要二倍速，neng pitch 只影响这一路
            _doorSource = CreateSource("DoorSfx", false, _sfxVolume);

            _lightLoopSource = CreateSource("LightBuzz", true, 0f);
            _lightBuzz = Resources.Load<AudioClip>(LightBuzzClipPath);

            if (_lightBuzz != null)
            {
                _lightLoopSource.clip = _lightBuzz;
                _lightLoopSource.Play();   // 一直"播"着，音量 0 → 开灯时推上去
            }

            PlayLoop(_ambientA, _ambFan);
            PlayLoop(_ambientB, _ambHum);

            ScheduleNextDistant();
        }

        private AudioSource CreateSource(string sourceName, bool loop, float volume)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.loop = loop;
            source.volume = volume;
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            return source;
        }

        private static void PlayLoop(AudioSource source, AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            source.clip = clip;
            source.Play();
        }

        /// <summary>
        /// 远处环境声的调度：到了点就响一下，然后重新排下一次。
        ///
        /// 用 <see cref="AudioSource.PlayOneShot"/> 而不是主通道 ——
        /// 它不该打断门 / 灯的音效，也不需要被谁 Stop。
        /// </summary>
        private void Update()
        {
            // 电音淡入淡出必须最先跑 —— 下面几处 return 会把它跳过
            UpdateLightBuzz();

            if (_ambDistant == null || _ambientC == null)
            {
                return;
            }

            if (Time.time < _nextDistantAt)
            {
                return;
            }

            _ambientC.PlayOneShot(_ambDistant, _ambientC.volume);
            ScheduleNextDistant();
        }

        /// <summary>排下一次远处声的时刻：25~70 秒之间随机。</summary>
        private void ScheduleNextDistant()
        {
            _nextDistantAt = Time.time + UnityEngine.Random.Range(DistantMinGap, DistantMaxGap);
        }

        /// <summary>
        /// 开关门的音效。
        ///
        /// <paramref name="disabled"/> = 这一侧已经被突破（门和灯都废了）。
        /// 那时按钮按下去没有实际作用，但**还是要有反馈**，
        /// 所以退回默认的机械音（<see cref="DefaultDoorClip"/>），
        /// 而不是新录的那两段「很像真的开了/关了」的素材 ——
        /// 明确告诉玩家「这扇门现在不归你管了」。
        /// </summary>
        public void PlayDoor(bool closed, bool disabled = false)
        {
            if (disabled)
            {
                Play(DefaultDoorClip);
                return;
            }

            if (closed)
            {
                PlayDoorClip(_sfxDoorClose, 1f);
            }
            else
            {
                // 开门用二倍速（见 DoorOpenPitch）
                PlayDoorClip(_sfxDoorOpen, DoorOpenPitch);
            }
        }

        /// <summary>
        /// 开关「开灯电音」的循环。
        ///
        /// ⚠️ 由调用方**汇总**后再传**（任意一侧亮着就 on）**，不要每侧各调一次 ——
        /// 左右灯是分开调用的，各调一次会互相覆盖（开左灯 → on，开右灯 → 又 on，
        /// 关左灯 → off，但右灯还亮着，声音就断了）。
        /// </summary>
        public void SetLightBuzz(bool on)
        {
            _lightBuzzTarget = on ? LightBuzzVolume : 0f;
        }

        /// <summary>每帧推进电音的淡入淡出。</summary>
        private void UpdateLightBuzz()
        {
            if (_lightLoopSource == null || _lightBuzz == null)
            {
                return;
            }

            float current = _lightLoopSource.volume;

            if (Mathf.Approximately(current, _lightBuzzTarget))
            {
                return;
            }

            float speed = LightBuzzVolume / Mathf.Max(0.01f, LightBuzzFadeSeconds);

            _lightLoopSource.volume = Mathf.MoveTowards(
                current, _lightBuzzTarget, speed * Time.unscaledDeltaTime);
        }

        /// <summary>
        /// 开关灯的音效。
        ///
        /// <paramref name="disabled"/> = 这一侧已经被突破（门和灯都废了）。
        /// 和 <see cref="PlayDoor"/> 一样退回默认机械音 ——
        /// 按下去没反应，但有反馈，玩家能立刻明白「这侧不归我管了」。
        /// </summary>
        public void PlayLight(bool on, bool disabled = false)
        {
            if (disabled)
            {
                Play(DefaultDoorClip);
                return;
            }

            Play(on ? _sfxLightOn : _sfxLightOff);
        }

        /// <summary>
        /// 开灯照到门口有人时的提示音。**只响一次**（不循环）。
        ///
        /// 素材缺失时静默 —— 边缘红脉冲的视觉反馈仍然生效，不会因为少个音频就失效。
        /// </summary>
        public void PlayLightReveal()
        {
            if (_sfxLightReveal == null)
            {
                // 懒加载一次，之后缓存（和跳杀音效同一套路）
                _sfxLightReveal = Resources.Load<AudioClip>(LightRevealClipPath);
                if (_sfxLightReveal == null)
                {
                    return;
                }
            }

            if (_sfx == null)
            {
                return;
            }

            // ⚠️ 这里**不用 PlayOneShot**（那是 Play() 的默认做法）——
            // PlayOneShot 会叠着播，而且 Stop() 打不断它。
            // 报警音有 3.9 秒，如果玩家在这期间关灯再开，就会两层音叠在一起。
            // 走主通道 + Stop 先清场，保证任何时候只有一条在响。
            _sfx.Stop();
            _sfx.clip = _sfxLightReveal;
            _sfx.loop = false;
            _sfx.volume = _sfxVolume;
            _sfx.Play();
        }

        public void PlayCameraSwitch()
        {
            Play(_sfxCameraSwitch);
        }

        public void PlayFootstep()
        {
            Play(_sfxFootstep);
        }

        /// <summary>
        /// 播一声牛叫。<paramref name="leaving"/> = true 时用**出厨房**那条素材。
        ///
        /// 走独立通道 + PlayOneShot：牛叫不该打断门 / 灯 / 脚步。
        ///
        /// ⚠️ 不再靠降调（pitch）来区分进出 —— 降调会把音色一起扭曲，
        /// 听起来像换了只怪物。两条独立素材各司其职，pitch 保持 1。
        /// </summary>
        public void PlayMoo(bool leaving, float volumeScale = 1f)
        {
            if (_mooSource == null)
            {
                return;
            }

            AudioClip clip = leaving ? _sfxMooLeave : _sfxMoo;

            // 出厨房那条缺失时退回进厨房那条，至少还有声音
            if (clip == null)
            {
                clip = _sfxMoo;
            }

            if (clip == null)
            {
                return;
            }

            if (volumeScale < 0f) { volumeScale = 0f; }
            if (volumeScale > 1.5f) { volumeScale = 1.5f; }

            _mooSource.pitch = 1f;
            _mooSource.PlayOneShot(clip, _sfxVolume * volumeScale);
        }

        /// <summary>
        /// 播一声脚步。<paramref name="rightFoot"/> 决定左右脚，
        /// <paramref name="volumeScale"/> 按距离缩放（1 = 就在门口）。
        ///
        /// 走**独立通道** + PlayOneShot：脚步声要能和门 / 灯 / 提示音叠在一起，
        /// 共用主通道会把正在响的音效掐掉。
        /// </summary>
        public void PlayFootstep(bool rightFoot, float volumeScale)
        {
            AudioClip clip = rightFoot ? _sfxFootstepRight : _sfxFootstepLeft;

            // 新素材缺失时退回原来那条单声道，不至于完全没声
            if (clip == null)
            {
                clip = _sfxFootstep;
            }

            if (clip == null || _footstepSource == null)
            {
                return;
            }

            if (volumeScale < 0f) { volumeScale = 0f; }
            if (volumeScale > 1f) { volumeScale = 1f; }

            _footstepSource.PlayOneShot(clip, _sfxVolume * volumeScale);
        }

        public void PlayPowerOut()
        {
            Play(_sfxPowerOut);
        }

        /// <summary>断电演出进入音乐段时调用。音乐盒不循环，播完自然停。</summary>
        public void PlayPowerOutMusic()
        {
            if (_powerOutMusic == null || _music == null)
            {
                return;
            }

            // 先 Stop 再 Play：同一个 source 复用时必须停下来，
            // 否则会从上次的位置接着放（调试时重进断电段就露馅了）
            _music.Stop();
            _music.clip = _powerOutMusic;
            _music.Play();
        }

        /// <summary>音乐段结束（或提前跳杀）时把音乐掐掉。</summary>
        public void StopPowerOutMusic()
        {
            if (_music == null)
            {
                return;
            }

            _music.Stop();
        }

        /// <summary>音乐盒素材的长度（秒）。没素材时返回 0。</summary>
        public float PowerOutMusicLength
        {
            get { return _powerOutMusic != null ? _powerOutMusic.length : 0f; }
        }

        /// <summary>
        /// 开门音效的播放速度倍数（1 = 原速，2 = 二倍速）。
        /// 用 pitch 而不是重采样素材：素材保持原样，这个数随时能改。
        /// </summary>
        private const float DoorOpenPitch = 2f;

        private void Play(AudioClip clip)
        {
            if (clip == null || _sfx == null)
            {
                return;
            }

            _sfx.PlayOneShot(clip, _sfxVolume);
        }

        /// <summary>
        /// 用门音效专属通道播放，可带音高。
        ///
        /// ⚠️ 为什么不用 <c>_sfx</c> 临时改 pitch：那是共享通道，
        /// 改 pitch 会连带影响同一帧播的其他音效（脚步 / 开关灯 / 提示音）。
        /// 单独开一条通道，pitch 只作用在门声上，互不干扰。
        /// </summary>
        private void PlayDoorClip(AudioClip clip, float pitch)
        {
            if (clip == null || _doorSource == null)
            {
                return;
            }

            _doorSource.pitch = pitch;
            _doorSource.PlayOneShot(clip, _sfxVolume);
        }

        /// <summary>由编辑器菜单调用，一次性注入全部素材。</summary>
        /// <param name="ambDistant">
        /// 偶尔响一下的远处环境声（不循环）。传 null 就是不启用。
        /// </param>
        public void SetClips(AudioClip ambFan, AudioClip ambHum,
            AudioClip doorClose, AudioClip doorOpen,
            AudioClip lightOn, AudioClip lightOff,
            AudioClip cameraSwitch, AudioClip footstep, AudioClip powerOut,
            AudioClip powerOutMusic, AudioClip ambDistant = null,
            AudioClip footstepLeft = null, AudioClip footstepRight = null,
            AudioClip moo = null, AudioClip mooLeave = null)
        {
            _ambFan = ambFan;
            _ambHum = ambHum;
            _ambDistant = ambDistant;
            _sfxFootstepLeft = footstepLeft;
            _sfxFootstepRight = footstepRight;
            _sfxMoo = moo;
            _sfxMooLeave = mooLeave;
            _sfxDoorClose = doorClose;
            _sfxDoorOpen = doorOpen;
            _sfxLightOn = lightOn;
            _sfxLightOff = lightOff;
            _sfxCameraSwitch = cameraSwitch;
            _sfxFootstep = footstep;
            _sfxPowerOut = powerOut;
            _powerOutMusic = powerOutMusic;
        }
    }
}
