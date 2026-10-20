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

        [Header("音效")]
        [SerializeField] private AudioClip _sfxDoorClose;
        [SerializeField] private AudioClip _sfxDoorOpen;
        [SerializeField] private AudioClip _sfxLightOn;
        [SerializeField] private AudioClip _sfxLightOff;
        [SerializeField] private AudioClip _sfxCameraSwitch;
        [SerializeField] private AudioClip _sfxFootstep;
        [SerializeField] private AudioClip _sfxPowerOut;

        /// <summary>
        /// 开灯那一刻**照到门口站着人**时的提示音。
        ///
        /// 和普通「开灯」音效分开：普通开灯不管有没有人都响，
        /// 这个只在真的照到敌人的**上升沿**响一次（关着灯看不见人，开灯才发现）。
        /// 音频素材还没做，留空就是静音，不影响后面接。
        /// </summary>
        [SerializeField] private AudioClip _sfxLightReveal;

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

        private void Awake()
        {
            _ambientA = CreateSource("AmbientA", true, _ambientVolume);
            _ambientB = CreateSource("AmbientB", true, _ambientVolume);
            _sfx = CreateSource("Sfx", false, _sfxVolume);
            _music = CreateSource("PowerOutMusic", false, _musicVolume);

            PlayLoop(_ambientA, _ambFan);
            PlayLoop(_ambientB, _ambHum);
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

        public void PlayDoor(bool closed)
        {
            Play(closed ? _sfxDoorClose : _sfxDoorOpen);
        }

        public void PlayLight(bool on)
        {
            Play(on ? _sfxLightOn : _sfxLightOff);
        }

        /// <summary>
        /// 开灯照到门口有人时的提示音。素材缺失时静默 —— 视觉反馈仍然生效。
        /// </summary>
        public void PlayLightReveal()
        {
            Play(_sfxLightReveal);
        }

        public void PlayCameraSwitch()
        {
            Play(_sfxCameraSwitch);
        }

        public void PlayFootstep()
        {
            Play(_sfxFootstep);
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
        public void SetClips(AudioClip ambFan, AudioClip ambHum,
            AudioClip doorClose, AudioClip doorOpen,
            AudioClip lightOn, AudioClip lightOff,
            AudioClip cameraSwitch, AudioClip footstep, AudioClip powerOut,
            AudioClip powerOutMusic)
        {
            _ambFan = ambFan;
            _ambHum = ambHum;
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
