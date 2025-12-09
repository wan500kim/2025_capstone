using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class CountdownOverlay : MonoBehaviour
{
    [Header("Target & Sprites (5..1)")]
    public Image targetImage;                 // 표시할 Image (없으면 자기 자신 Image 자동 할당)
    public Sprite[] digits;                   // 5,4,3,2,1 순서로 5장

    [Header("Timing")]
    public float perDigitDuration = 0.9f;

    [Header("TimeManager Integration")]
    public bool  autoPlayOnPhaseEnd = true;   // Prep/Round 페이즈 5초 남았을 때 자동 재생
    public float triggerSecondsRemaining = 5f; // 남은 시간이 이 값 이하일 때 트리거

    [Header("Safety")]
    public bool  autoFindImageOnAwake = true; // true면 자기 자신 Image 자동 할당
    public bool  useSetNativeSize = false;    // 켜면 스프라이트 원본 크기로 바꿈(UI 레이아웃 깨질 수 있어 주의)

    [Header("FX: Pop")]
    public bool  usePop = true;
    public float popScale = 1.25f;
    public float popInTime = 0.12f;
    public float popOutTime = 0.18f;

    [Header("FX: Fade")]
    public bool  useFade = true;
    public float fadeDelay = 0.25f;
    public float fadeTime  = 0.45f;

    Coroutine run;
    bool hasTriggeredThisPhase = false;
    DateTime? roundStartDate = null;  // Round 시작 날짜 저장

    void Awake()
    {
        if (!targetImage && autoFindImageOnAwake)
        {
            targetImage = GetComponent<Image>();
            Debug.Log($"[Countdown] autoFindImageOnAwake -> {(targetImage ? "FOUND" : "NULL")}", this);
        }
        if (!targetImage)
            Debug.LogError("[Countdown] targetImage가 비어있음. 보이는 UI Image를 연결하세요.", this);
    }

    void OnEnable()
    { 
        if (autoPlayOnPhaseEnd)
        {
            TimeManager.OnClientPhaseChanged += OnPhaseChanged;
            TimeManager.OnClientNewDay += OnNewDay;
            TimeManager.OnClientTick += OnTimerTick;
        }
    }

    void OnDisable()
    {
        if (autoPlayOnPhaseEnd)
        {
            TimeManager.OnClientPhaseChanged -= OnPhaseChanged;
            TimeManager.OnClientNewDay -= OnNewDay;
            TimeManager.OnClientTick -= OnTimerTick;
        }
    }

    void OnPhaseChanged(GamePhase phase, int round)
    {
        // 페이즈가 변경되면 트리거 플래그 리셋
        hasTriggeredThisPhase = false;
        
        // Round 시작 시 시작 날짜 저장
        if (phase == GamePhase.Round)
        {
            roundStartDate = TimeManager.CurrentDate;
            Debug.Log($"[Countdown] Round {round} started. Start date: {roundStartDate}", this);
        }
        else
        {
            roundStartDate = null;
        }
        
        Debug.Log($"[Countdown] Phase changed to {phase}, round {round}. Reset trigger flag.", this);
    }
    
    void OnNewDay(DateTime date, int round, int dayIndex)
    {
        // Round 페이즈에서 첫 날이면 시작 날짜 저장
        if (TimeManager.Phase == GamePhase.Round && roundStartDate == null)
        {
            roundStartDate = date;
            Debug.Log($"[Countdown] Round start date captured: {date}", this);
        }
    }

    void OnTimerTick(float remainingSec, float totalSec)
    {
        if (hasTriggeredThisPhase) return;
        if (TimeManager.Instance == null) return;

        var phase = TimeManager.Phase;
        
        // Prep 페이즈: 일반 타이머 사용
        if (phase == GamePhase.Prep)
        {
            if (remainingSec <= triggerSecondsRemaining && remainingSec > 0f)
            {
                hasTriggeredThisPhase = true;
                Debug.Log($"[Countdown] Triggered at {remainingSec:F1}s remaining in Prep phase.", this);
                Play();
            }
        }
        // Round 페이즈: 전체 라운드 남은 시간 계산
        else if (phase == GamePhase.Round && TimeManager.IsDailyTicking)
        {
            float totalRemainingSeconds = CalculateRoundRemainingSeconds(remainingSec);
            
            if (totalRemainingSeconds > 0f && totalRemainingSeconds <= triggerSecondsRemaining)
            {
                hasTriggeredThisPhase = true;
                Debug.Log($"[Countdown] Triggered at {totalRemainingSeconds:F1}s remaining in Round phase.", this);
                Play();
            }
        }
    }
    
    // Round 페이즈의 전체 남은 시간 계산
    float CalculateRoundRemainingSeconds(float currentDayRemaining)
    {
        if (TimeManager.Instance == null) return 0f;
        if (!roundStartDate.HasValue) return 0f;
        
        var currentDate = TimeManager.CurrentDate;
        if (!currentDate.HasValue) return 0f;
        
        // 분기 말일 계산
        DateTime qEnd = GetQuarterEndFixed(roundStartDate.Value);
        DateTime cur = currentDate.Value;
        
        // 남은 일수 계산 (오늘 포함)
        int remainingDays = 0;
        while (cur <= qEnd)
        {
            remainingDays++;
            cur = cur.AddDays(1);
        }
        
        // 전체 남은 시간 = (남은 일수 - 1) × 초/일 + 현재 날의 남은 시간
        float totalRemaining = (remainingDays - 1) * TimeManager.Instance.syncedSecondsPerDay + currentDayRemaining;
        
        return totalRemaining;
    }
    
    // TimeManager의 GetQuarterEndFixed 로직 복사
    static DateTime GetQuarterEndFixed(DateTime date)
    {
        int qEndMonth = ((date.Month - 1) / 3 + 1) * 3;
        int day = FixedMonthDays(qEndMonth);
        return new DateTime(date.Year, qEndMonth, day);
    }
    
    static int FixedMonthDays(int month)
    {
        switch (month)
        {
            case 1: case 3: case 5: case 7: case 8: case 10: case 12: return 31;
        }
        if (month == 2) return 28;
        return 30;
    }

    [ContextMenu("Play Now")]
    public void Play()
    {
        if (run != null) StopCoroutine(run);
        run = StartCoroutine(CoPlay());
    }

    IEnumerator CoPlay()
    {
        if (!targetImage) { Debug.LogError("[Countdown] targetImage NULL", this); yield break; }
        if (digits == null || digits.Length == 0)
        { Debug.LogError("[Countdown] digits가 비어있음(5→1 스프라이트 연결 필요)", this); yield break; }

        // 가시성 강제
        var cg = targetImage.GetComponentInParent<CanvasGroup>();
        if (cg && cg.alpha <= 0f) Debug.LogWarning("[Countdown] 부모 CanvasGroup.alpha=0. 화면에 안 보일 수 있음.", this);

        targetImage.enabled = true;
        targetImage.raycastTarget = false; // 이벤트 방해 방지(선택)

        Debug.Log($"[Countdown] START digits={digits.Length}", this);

        for (int i = 0; i < digits.Length; i++)
        {
            var s = digits[i];
            if (!s)
            {
                Debug.LogWarning($"[Countdown] digits[{i}] 가 NULL", this);
                continue;
            }

            // 초기화
            targetImage.sprite = s;
            if (useSetNativeSize) targetImage.SetNativeSize();
            targetImage.color  = new Color(1,1,1,1);
            targetImage.rectTransform.localScale = Vector3.one;

            Debug.Log($"[Countdown] SHOW #{digits.Length - i} (idx {i}) sprite={s.name}", this);

            // 이펙트
            Coroutine popCo  = usePop  ? StartCoroutine(CoPop(targetImage.rectTransform)) : null;
            Coroutine fadeCo = useFade ? StartCoroutine(CoFade(targetImage))              : null;

            // 유지
            float t = 0f;
            while (t < perDigitDuration)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            // 정리
            if (popCo  != null) StopCoroutine(popCo);
            if (fadeCo != null) StopCoroutine(fadeCo);
            targetImage.color = new Color(1,1,1,1);
            targetImage.rectTransform.localScale = Vector3.one;
        }

        targetImage.enabled = false;
        run = null;
        Debug.Log("[Countdown] END", this);
    }

    IEnumerator CoPop(RectTransform rt)
    {
        // up
        float t = 0f;
        while (t < popInTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / popInTime);
            float s = Mathf.Lerp(1f, popScale, EaseOutQuad(k));
            rt.localScale = Vector3.one * s;
            yield return null;
        }
        // down
        t = 0f;
        while (t < popOutTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / popOutTime);
            float s = Mathf.Lerp(popScale, 1f, EaseOutQuad(k));
            rt.localScale = Vector3.one * s;
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    IEnumerator CoFade(Image img)
    {
        float t = 0f;
        while (t < fadeDelay)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeTime);
            float a = Mathf.Lerp(1f, 0f, k);
            var c = img.color; c.a = a; img.color = c;
            yield return null;
        }
        var end = img.color; end.a = 0f; img.color = end;
    }

    static float EaseOutQuad(float x) => 1f - (1f - x) * (1f - x);
}
