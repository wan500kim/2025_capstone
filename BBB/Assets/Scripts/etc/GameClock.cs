using System;
using UnityEngine;

public interface IGameClock
{
    DateTime GameDateUtc { get; }
    int QuarterIndex { get; }                 // 0~3
    int DaysIntoQuarter { get; }              // 0..daysPerQuarter-1
    int DaysPerQuarter { get; }
    float RealSecondsPerGameDay { get; }
    float SecondsIntoCurrentGameDay { get; }
    float QuarterProgress01 { get; }          // 1→0, 막대형 타이머용
    event Action<float> OnRealSecond;         // 현실 초 틱
    event Action<DateTime> OnGameDay;         // 게임 날짜 1일 증가 시
    event Action<int,int> OnQuarterTick;      // (quarterIdx, daysRemaining)
    event Action<int> OnQuarterEnd;           // quarterIdx
    void ResetClock(DateTime startDateUtc, int startQuarterIndex = 0);
    void Pause(); void Resume();
    float GetQuarterRemainingSecondsReal();   // 상단 mm:ss
}

public class GameClock : MonoBehaviour, IGameClock
{
    [Header("Time Scale")]
    [SerializeField] float realSecondsPerGameDay = 5f;   // 요구: 5초=1일
    [SerializeField] int daysPerQuarter = 90;            // 1분기=90일 가정

    public DateTime GameDateUtc { get; private set; }
    public int QuarterIndex { get; private set; }
    public int DaysIntoQuarter { get; private set; }
    public int DaysPerQuarter => daysPerQuarter;
    public float RealSecondsPerGameDay => realSecondsPerGameDay;
    public float SecondsIntoCurrentGameDay => _accumReal;
    public float QuarterProgress01 => 1f - (DaysIntoQuarter / (daysPerQuarter - 1f));

    public event Action<float> OnRealSecond;
    public event Action<DateTime> OnGameDay;
    public event Action<int,int> OnQuarterTick;
    public event Action<int> OnQuarterEnd;

    float _accumReal;   // 하루 진행 누적 현실초
    float _accumSecond; // 매 초 방송용
    bool _paused;

    void Awake()
    {
        if (GameDateUtc == default) GameDateUtc = DateTime.UtcNow.Date;
    }

    void Update()
    {
        if (_paused) return;
        float dt = Time.unscaledDeltaTime; // 현실 시간
        _accumReal += dt;
        _accumSecond += dt;

        if (_accumSecond >= 1f)
        {
            OnRealSecond?.Invoke(_accumSecond);
            _accumSecond = 0f;
        }

        while (_accumReal >= realSecondsPerGameDay)
        {
            _accumReal -= realSecondsPerGameDay;
            AdvanceOneGameDay();
        }
    }

    void AdvanceOneGameDay()
    {
        GameDateUtc = GameDateUtc.AddDays(1);
        DaysIntoQuarter++;
        OnGameDay?.Invoke(GameDateUtc);
        OnQuarterTick?.Invoke(QuarterIndex, daysPerQuarter - 1 - DaysIntoQuarter);

        if (DaysIntoQuarter >= daysPerQuarter)
        {
            OnQuarterEnd?.Invoke(QuarterIndex);
            QuarterIndex = (QuarterIndex + 1) % 4;
            DaysIntoQuarter = 0;
        }
    }

    public float GetQuarterRemainingSecondsReal()
    {
        int daysLeftExclToday = daysPerQuarter - 1 - DaysIntoQuarter;
        float todayLeft = Mathf.Max(0f, realSecondsPerGameDay - _accumReal);
        return daysLeftExclToday * realSecondsPerGameDay + todayLeft;
    }

    public void ResetClock(DateTime startDateUtc, int startQuarterIndex = 0)
    {
        GameDateUtc = startDateUtc;
        QuarterIndex = Mathf.Clamp(startQuarterIndex, 0, 3);
        DaysIntoQuarter = 0;
        _accumReal = 0f; _accumSecond = 0f;
    }

    public void Pause()  => _paused = true;
    public void Resume() => _paused = false;
}
