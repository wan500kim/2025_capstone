// Assets/Scripts/etc/TimeHudBinder.cs
// 서버 권위 TimeManager(라운드/결과/준비턴 상태머신) 호환 HUD 바인더
// 요구사항: 라운드 중에만 동작. 준비/결과턴에서는 표시 유지만 하고 갱신하지 않음.

using System;
using UnityEngine;
using UnityEngine.UIElements;

public class TimeHudBinder : MonoBehaviour
{
    [Header("UI")]
    public UIDocument ui;

    [Header("표시 기준 연도(예: 2026 → 1년차)")]
    public int baseYear = 2026;

    // ---- cached UI refs ----
    VisualElement root;
    Label timerLabel;                 // name="timer"
    VisualElement timerFill;          // name="timer_fill"
    Label dateLabel;                  // class="dashboard_date"
    Label termLabel;                  // class="dashboard_term"

    // 라운드 외 구간에서 마지막으로 표시했던 날짜 보존
    DateTime? _lastShownDate;

    void OnEnable()
    {
        if (ui == null) ui = GetComponent<UIDocument>();
        root = ui != null ? ui.rootVisualElement : null;
        if (root != null)
        {
            timerLabel = root.Q<Label>("timer");
            timerFill  = root.Q<VisualElement>("timer_fill");
            dateLabel  = root.Q<Label>(className: "dashboard_date");
            termLabel  = root.Q<Label>(className: "dashboard_term");
        }

        // 이벤트 구독: 라운드 틱 + 날짜 전환 + 페이즈 변경
        TimeManager.OnClientTick += OnClientTick;
        TimeManager.OnClientNewDay += OnNewDay;
        TimeManager.OnClientPhaseChanged += OnPhaseChanged;

        ForceRefreshAll();
    }

    void OnDisable()
    {
        TimeManager.OnClientTick -= OnClientTick;
        TimeManager.OnClientNewDay -= OnNewDay;
        TimeManager.OnClientPhaseChanged -= OnPhaseChanged;
    }

    // ---------- 이벤트 핸들러 ----------
    void OnPhaseChanged(GamePhase phase, int round)
    {
        // 라운드 시작/종료 시점에만 화면 갱신. 준비/결과턴에서는 값 유지.
        var d = GetCurrentDateNullable();
        UpdateDate(d, phase);
        UpdateTerm(d, phase, round);
        if (phase == GamePhase.Round)
            UpdateRoundTimer();
    }

    void OnNewDay(DateTime newDate, int round, int dayIndex)
    {
        if (TimeManager.Instance == null) return;
        if (TimeManager.Instance.currentPhase != GamePhase.Round) return;
        
        _lastShownDate = newDate.Date;
        UpdateDate(_lastShownDate, GamePhase.Round);
        UpdateTerm(_lastShownDate, GamePhase.Round, round);
        UpdateRoundTimer();
    }

    void OnClientTick(float remainingSec, float totalSec)
    {
        if (TimeManager.Instance == null) return;
        if (TimeManager.Instance.currentPhase != GamePhase.Round) return; // 준비/결과턴엔 미동작
        
        UpdateRoundTimer();
    }

    // ---------- 표시 갱신 ----------
    void ForceRefreshAll()
    {
        if (TimeManager.Instance == null) return;

        var d = GetCurrentDateNullable();
        var phase = TimeManager.Instance.currentPhase;
        var round = TimeManager.Instance.currentRound;

        UpdateDate(d, phase);
        UpdateTerm(d, phase, round);
        if (phase == GamePhase.Round)
            UpdateRoundTimer();
    }

    void UpdateDate(DateTime? d, GamePhase phase)
    {
        if (dateLabel == null) return;
        if (phase == GamePhase.Round && d.HasValue)
        {
            _lastShownDate = d.Value;
            dateLabel.text = d.Value.ToString("yyyy. MM. dd.");
        }
        else
        {
            // 준비/결과턴: 마지막 표시값 유지
            dateLabel.text = _lastShownDate.HasValue ? _lastShownDate.Value.ToString("yyyy. MM. dd.") : string.Empty;
        }
    }

    void UpdateTerm(DateTime? d, GamePhase phase, int round)
    {
        if (termLabel == null) return;
        int year = d.HasValue ? d.Value.Year : baseYear;
        int yearDiff = Mathf.Max(0, year - baseYear) + 1; // 1년차부터
        int q = Mathf.Clamp(round, 1, 4);
        termLabel.text = $"({yearDiff}년차 {q}분기)";
    }

    // === 분기 전체 남은 시간 타이머(라운드 중에만 작동) ===
    void UpdateRoundTimer()
    {
        if (timerLabel == null || timerFill == null) return;
        if (TimeManager.Instance == null) return;
        if (TimeManager.Instance.currentPhase != GamePhase.Round) return;

        // 현재 날짜
        var currentDate = TimeManager.CurrentDate;
        if (!currentDate.HasValue) return;

        var today = currentDate.Value.Date;
        int spd = Mathf.Max(1, Mathf.RoundToInt(TimeManager.Instance.syncedSecondsPerDay));

        // 분기 범위 계산
        var qr = GetQuarterRange(today);
        int totalDays = (int)(qr.end - qr.start).TotalDays + 1; // 양끝 포함
        if (totalDays <= 0) { SetTimerUI(0, 0); return; }

        // 분기 내 경과 시간 = 경과 일수*spd + 오늘 경과초
        int daysSinceStart = Mathf.Clamp((int)(today - qr.start).TotalDays, 0, totalDays - 1);
        
        // 오늘의 남은 시간 = totalSec - 남은초
        float todayRemain = TimeManager.Instance.phaseRemainingSec;
        if (TimeManager.Instance.isDailyTicking)
        {
            // 라운드 중: dailyTicking이 true이면 오늘 진행 중
            float elapsed = spd - todayRemain;
            float elapsedTotal = daysSinceStart * spd + elapsed;
            float total = totalDays * spd;
            float remain = Mathf.Clamp(total - elapsedTotal, 0f, total);
            float pct = (total > 0f) ? (remain / total) * 100f : 0f;
            SetTimerUI(remain, pct);
        }
        else
        {
            // 라운드 외 구간: 값 표시하되 갱신하지 않음
            float total = totalDays * spd;
            float pct = 0f;
            SetTimerUI(0, pct);
        }
    }

    void SetTimerUI(float remainSec, float percentWidth)
    {
        int mm = Mathf.FloorToInt(remainSec / 60f);
        int ss = Mathf.FloorToInt(remainSec % 60f);
        if (timerLabel != null) timerLabel.text = $"{mm:00}:{ss:00}";
        if (timerFill != null) timerFill.style.width = Length.Percent(Mathf.Clamp(percentWidth, 0f, 100f));
    }

    // ---------- 유틸 ----------
    DateTime? GetCurrentDateNullable()
    {
        return TimeManager.CurrentDate;
    }

    struct QuarterRange { public DateTime start; public DateTime end; }
    
    QuarterRange GetQuarterRange(DateTime d)
    {
        var y = d.Year;
        int q = GetQuarter(d);
        switch (q)
        {
            case 1: 
                return new QuarterRange
                { 
                    start = new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc), 
                    end = new DateTime(y, 3, 31, 0, 0, 0, DateTimeKind.Utc)
                };
            case 2: 
                return new QuarterRange
                { 
                    start = new DateTime(y, 4, 1, 0, 0, 0, DateTimeKind.Utc), 
                    end = new DateTime(y, 6, 30, 0, 0, 0, DateTimeKind.Utc)
                };
            case 3: 
                return new QuarterRange
                { 
                    start = new DateTime(y, 7, 1, 0, 0, 0, DateTimeKind.Utc), 
                    end = new DateTime(y, 9, 30, 0, 0, 0, DateTimeKind.Utc)
                };
            default:
                return new QuarterRange
                { 
                    start = new DateTime(y, 10, 1, 0, 0, 0, DateTimeKind.Utc), 
                    end = new DateTime(y, 12, 31, 0, 0, 0, DateTimeKind.Utc)
                };
        }
    }

    int GetQuarter(DateTime d)
    {
        if (d.Month <= 3) return 1;
        if (d.Month <= 6) return 2;
        if (d.Month <= 9) return 3;
        return 4;
    }
}