using UnityEngine;

[DisallowMultipleComponent]
public class SceneBGM : MonoBehaviour
{
    [Header("BGM 설정")]
    [Tooltip("이 씬에서 재생할 배경음")]
    public AudioClip bgmClip;

    [Tooltip("크로스페이드 시간(초)")]
    [Min(0f)] public float fadeSeconds = 1f;

    [Tooltip("루프 재생")]
    public bool loop = true;

    [Tooltip("목표 볼륨(0~1)")]
    [Range(0f, 1f)] public float targetVolume = 1f;

    [Header("수명 관리")]
    [Tooltip("이 오브젝트가 비활성화될 때 BGM을 페이드아웃할지")]
    public bool stopOnDisable = false;

    [Tooltip("stopOnDisable이 켜져 있을 때 페이드아웃 시간(초)")]
    [Min(0f)] public float stopFadeSeconds = 0.5f;

    void OnEnable()
    {
        if (bgmClip != null)
            GameAudio.PlayMusic(bgmClip, fadeSeconds, loop, targetVolume);
    }

    void OnDisable()
    {
        if (stopOnDisable)
            GameAudio.StopMusic(stopFadeSeconds);
    }
}
