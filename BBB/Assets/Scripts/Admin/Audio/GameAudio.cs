using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 전역 SFX/BGM 허브.
/// GameAudio.Play("key")로 효과음, PlayMusic로 BGM 제어.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10000)] // 최대한 먼저 깨어나도록
public class GameAudio : MonoBehaviour
{
    public static GameAudio I { get; private set; }

    [Header("Mixer")]
    public AudioMixerGroup sfxMixer;
    public AudioMixerGroup musicMixer;

    [Header("SFX 기본 설정")]
    [Min(0f)] public float minIntervalSameKey = 0f;
    [Range(0f, 0.5f)] public float randomPitchRange = 0f;

    [Header("라이브러리(키-클립 매핑)")]
    public List<KeyClip> library = new List<KeyClip>();

    [System.Serializable]
    public struct KeyClip
    {
        public string key;
        public AudioClip clip;
    }

    AudioSource _sfxSource;
    AudioSource _musicSourceA;
    AudioSource _musicSourceB;
    Dictionary<string, AudioClip> _map;
    readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
    Coroutine _musicFadeCo;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;
        DontDestroyOnLoad(gameObject);

        BuildMap();
        PrepareAudioSources();
    }

    void BuildMap()
    {
        _map = new Dictionary<string, AudioClip>();
        foreach (var kc in library)
        {
            if (!string.IsNullOrEmpty(kc.key) && kc.clip != null)
                _map[kc.key] = kc.clip;
        }
    }

    void PrepareAudioSources()
    {
        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.playOnAwake = false;
        _sfxSource.loop = false;
        _sfxSource.outputAudioMixerGroup = sfxMixer;

        _musicSourceA = gameObject.AddComponent<AudioSource>();
        _musicSourceA.playOnAwake = false;
        _musicSourceA.loop = true;
        _musicSourceA.outputAudioMixerGroup = musicMixer;

        _musicSourceB = gameObject.AddComponent<AudioSource>();
        _musicSourceB.playOnAwake = false;
        _musicSourceB.loop = true;
        _musicSourceB.outputAudioMixerGroup = musicMixer;
    }

    // Public API: SFX
    public static void Play(string key, float volume = 1f, float pitch = 1f)
    {
        if (!Ensure()) return;
        if (!I.TryGetClip(key, out var clip))
        {
            Debug.LogWarning($"[GameAudio] 키 '{key}' 클립 없음");
            return;
        }
        if (!I.CanPlayNow(key)) return;
        I.PlayOneShot(clip, volume, pitch);
        I._lastPlayed[key] = Time.unscaledTime;
    }

    public static void PlayClip(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (!Ensure() || clip == null) return;
        I.PlayOneShot(clip, volume, pitch);
    }

    public static void PlayAt(string key, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (!Ensure()) return;
        if (!I.TryGetClip(key, out var clip)) return;
        if (!I.CanPlayNow(key)) return;

        var go = new GameObject($"SFX_{key}");
        go.transform.position = position;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.spatialBlend = 1f;
        src.playOnAwake = false;
        src.outputAudioMixerGroup = I.sfxMixer;
        src.volume = volume;
        src.pitch = I.ApplyRandomPitch(pitch);
        src.minDistance = 1f;
        src.maxDistance = 50f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.01f, src.pitch));
        I._lastPlayed[key] = Time.unscaledTime;
    }

    public static void Register(string key, AudioClip clip)
    {
        if (!Ensure() || string.IsNullOrEmpty(key) || clip == null) return;
        I._map[key] = clip;
    }

    // Public API: Music
    public static void PlayMusic(AudioClip clip, float fadeSeconds = 1f, bool loop = true, float targetVolume = 1f)
    {
        if (!Ensure() || clip == null) return;
        I.InternalPlayMusic(clip, fadeSeconds, loop, targetVolume);
    }

    public static void StopMusic(float fadeSeconds = 0.5f)
    {
        if (!Ensure()) return;
        I.InternalStopMusic(fadeSeconds);
    }

    // Internal helpers
    static bool Ensure()
    {
        if (I != null) return true;

        // 생성만 수행. 초기화는 Awake에서 1회만.
        var go = new GameObject("GameAudio");
        go.AddComponent<GameAudio>();
        DontDestroyOnLoad(go);
        return true;
    }

    bool TryGetClip(string key, out AudioClip clip)
    {
        clip = null; // CS0177 방지
        if (_map == null) return false;
        return _map.TryGetValue(key, out clip);
    }

    bool CanPlayNow(string key)
    {
        if (minIntervalSameKey <= 0f) return true;
        if (_lastPlayed.TryGetValue(key, out var last))
            return Time.unscaledTime - last >= minIntervalSameKey;
        return true;
    }

    void PlayOneShot(AudioClip clip, float volume, float pitch)
    {
        if (clip == null) return;
        _sfxSource.pitch = ApplyRandomPitch(pitch);
        _sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        _sfxSource.pitch = 1f;
    }

    float ApplyRandomPitch(float basePitch)
    {
        if (randomPitchRange <= 0f) return basePitch;
        var delta = Random.Range(-randomPitchRange, randomPitchRange);
        return Mathf.Clamp(basePitch + delta, 0.5f, 2f);
    }

    void InternalPlayMusic(AudioClip clip, float fadeSeconds, bool loop, float targetVolume)
    {
        var from = _musicSourceA.isPlaying ? _musicSourceA : _musicSourceB;
        var to = ReferenceEquals(from, _musicSourceA) ? _musicSourceB : _musicSourceA;

        to.clip = clip;
        to.loop = loop;
        to.volume = 0f;
        to.Play();

        if (_musicFadeCo != null) StopCoroutine(_musicFadeCo);
        _musicFadeCo = StartCoroutine(FadeMusic(from, to, Mathf.Max(0.001f, fadeSeconds), targetVolume));
    }

    void InternalStopMusic(float fadeSeconds)
    {
        if (_musicFadeCo != null) StopCoroutine(_musicFadeCo);
        _musicFadeCo = StartCoroutine(FadeOutAllMusic(Mathf.Max(0.001f, fadeSeconds)));
    }

    IEnumerator FadeMusic(AudioSource from, AudioSource to, float seconds, float targetVolume)
    {
        var t = 0f;
        var fromStart = from.volume;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            var k = t / seconds;
            to.volume = Mathf.Lerp(0f, targetVolume, k);
            from.volume = Mathf.Lerp(fromStart, 0f, k);
            yield return null;
        }
        to.volume = targetVolume;
        if (from.isPlaying) from.Stop();
        from.volume = 0f;
        _musicFadeCo = null;
    }

    IEnumerator FadeOutAllMusic(float seconds)
    {
        var t = 0f;
        var aStart = _musicSourceA.volume;
        var bStart = _musicSourceB.volume;

        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            var k = t / seconds;
            _musicSourceA.volume = Mathf.Lerp(aStart, 0f, k);
            _musicSourceB.volume = Mathf.Lerp(bStart, 0f, k);
            yield return null;
        }
        if (_musicSourceA.isPlaying) _musicSourceA.Stop();
        if (_musicSourceB.isPlaying) _musicSourceB.Stop();
        _musicSourceA.volume = 0f;
        _musicSourceB.volume = 0f;
        _musicFadeCo = null;
    }
}
