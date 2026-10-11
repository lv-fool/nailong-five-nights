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

        [Header("音量")]
        [Range(0f, 1f)]
        [SerializeField] private float _ambientVolume = 0.30f;

        [Range(0f, 1f)]
        [SerializeField] private float _sfxVolume = 0.75f;

        private AudioSource _ambientA;
        private AudioSource _ambientB;
        private AudioSource _sfx;

        private void Awake()
        {
            _ambientA = CreateSource("AmbientA", true, _ambientVolume);
            _ambientB = CreateSource("AmbientB", true, _ambientVolume);
            _sfx = CreateSource("Sfx", false, _sfxVolume);

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
            AudioClip cameraSwitch, AudioClip footstep, AudioClip powerOut)
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
        }
    }
}
