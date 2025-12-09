// Assets/Scripts/Admin/PhaseAudioOrchestrator.cs
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-9000)]
public sealed class PhaseAudioOrchestrator : MonoBehaviour
{
    [Header("Crossfade")]
    [Min(0f)] public float crossFadeSeconds = 1f;

    [Header("Clips")]
    [Tooltip("메뉴/로비 공용 배경음")]
    public AudioClip lobbyBGM;

    [Tooltip("게임 페이즈(Round) 배경음")]
    public AudioClip roundBGM;

    [Tooltip("결과 페이즈 진입 시 1회 재생될 효과음")]
    public AudioClip resultEnterSFX;

    [Tooltip("아이템 선택 페이즈(=Prep) 배경음")]
    public AudioClip itemSelectBGM;

    [Header("Volumes")]
    [Range(0f,1f)] public float lobbyVolume = 1f;
    [Range(0f,1f)] public float roundVolume = 1f;
    [Range(0f,1f)] public float itemSelectVolume = 1f;

    [Header("Init Wait")]
    [Tooltip("게임 씬에서 TimeManager를 기다리는 최대 시간(초). 초과 시 로비 모드로 간주")]
    [Min(0f)] public float maxWaitForTimeManager = 2f;

    // 씬 간 연속 재생 상태(정적)
    static AudioClip s_currentMusicClip; // 마지막 재생 요청한 BGM
    static bool      s_isLobbyActive;    // 로비/메뉴 BGM 유지 여부

    GamePhase _lastPhase = GamePhase.Idle;
    Coroutine _initCo;
    Coroutine _pendingCo;

    void OnEnable()
    {
        // 초기화 루틴: TimeManager 준비를 기다린 후 모드 결정
        _initCo = StartCoroutine(CoInitialize());
    }

    void OnDisable()
    {
        if (_initCo != null) { StopCoroutine(_initCo); _initCo = null; }

        if (TimeManager.Instance != null)
        {
            TimeManager.OnClientPhaseChanged -= OnPhaseChanged;
            TimeManager.OnClientPauseChanged -= OnPauseChanged;
        }
        if (_pendingCo != null) { StopCoroutine(_pendingCo); _pendingCo = null; }
    }

    IEnumerator CoInitialize()
    {
        // TimeManager가 생길 때까지 대기. 이미 존재하면 0프레임 대기.
        float t = 0f;
        while (TimeManager.Instance == null && t < maxWaitForTimeManager)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (TimeManager.Instance == null)
        {
            // Main/Robby 씬 취급: 같은 로비 BGM이면 유지하여 끊김 없음
            MaybePlayLobbyContinuous();
            yield break;
        }

        // Game 씬 모드: 이벤트 구독 후 현재 페이즈에 맞춰 즉시 재생
        TimeManager.OnClientPhaseChanged += OnPhaseChanged;
        TimeManager.OnClientPauseChanged += OnPauseChanged;

        _lastPhase = TimeManager.Phase;
        ApplyPhase(TimeManager.Phase);
    }

    /* ===================== Main/Robby: 연속 재생 ===================== */
    void MaybePlayLobbyContinuous()
    {
        if (lobbyBGM == null) return;

        if (s_isLobbyActive && s_currentMusicClip == lobbyBGM) return; // 끊김 방지
        GameAudio.PlayMusic(lobbyBGM, crossFadeSeconds, loop: true, targetVolume: lobbyVolume);
        s_isLobbyActive = true;
        s_currentMusicClip = lobbyBGM;
    }

    /* ===================== Game: 이벤트 핸들러 ===================== */
    void OnPhaseChanged(GamePhase phase, int round)
    {
        if (phase == _lastPhase) return;

        // 결과 진입: BGM 즉시 정지 + SFX 1회
        if (phase == GamePhase.Result)
        {
            GameAudio.StopMusic(crossFadeSeconds);
            if (resultEnterSFX != null) GameAudio.PlayClip(resultEnterSFX);
        }

        _lastPhase = phase;
        ApplyPhase(phase);
    }

    void OnPauseChanged(bool paused)
    {
        if (paused) GameAudio.StopMusic(0.2f);
        else ApplyPhase(TimeManager.Phase);
    }

    /* ===================== 페이즈 적용 로직 ===================== */
    void ApplyPhase(GamePhase phase)
    {
        if (_pendingCo != null) { StopCoroutine(_pendingCo); _pendingCo = null; }

        // 게임 씬에선 로비 상태 해제
        s_isLobbyActive = false;

        switch (phase)
        {
            case GamePhase.Round:
                PlayRoundBGM();
                break;

            case GamePhase.Result:
                // 위에서 Stop+SFX 처리 완료
                break;

            case GamePhase.Prep: // = 아이템 선택 페이즈
                PlayItemSelectBGM();
                break;

            case GamePhase.Finished:
            default:
                GameAudio.StopMusic(crossFadeSeconds);
                s_currentMusicClip = null;
                break;
        }
    }

    void PlayRoundBGM()
    {
        if (roundBGM != null)
        {
            if (s_currentMusicClip == roundBGM) return; // 동일곡이면 재시작 금지
            GameAudio.PlayMusic(roundBGM, crossFadeSeconds, loop: true, targetVolume: roundVolume);
            s_currentMusicClip = roundBGM;
        }
        else
        {
            GameAudio.StopMusic(crossFadeSeconds);
            s_currentMusicClip = null;
        }
    }

    void PlayItemSelectBGM()
    {
        if (itemSelectBGM != null)
        {
            if (s_currentMusicClip == itemSelectBGM) return;
            // 결과 SFX와 겹치지 않도록 아주 짧은 지연 후 시작
            _pendingCo = StartCoroutine(CoDelayPlay(itemSelectBGM, itemSelectVolume, 0.05f));
        }
        else
        {
            GameAudio.StopMusic(crossFadeSeconds);
            s_currentMusicClip = null;
        }
    }

    IEnumerator CoDelayPlay(AudioClip clip, float vol, float delay)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, delay));
        GameAudio.PlayMusic(clip, crossFadeSeconds, loop: true, targetVolume: Mathf.Clamp01(vol));
        s_currentMusicClip = clip;
        _pendingCo = null;
    }
}
