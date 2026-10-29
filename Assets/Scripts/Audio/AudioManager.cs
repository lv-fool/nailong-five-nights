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

        public void PlayDoor(bool closed)
        {
            Play(closed ? _sfxDoorClose : _sfxDoorOpen);
        }

        public void PlayLight(bool on)
        {
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

        private void Play(AudioClip clip)
        {
            if (clip == null || _sfx == null)
            {
                return;
            }

            _sfx.PlayOneShot(clip, _sfxVolume);
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
