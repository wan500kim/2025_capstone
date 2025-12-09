using System;
using System.Collections.Generic;
using UnityEngine;

public static class MarketDataClient
{
    // ===== 공개 이벤트 =====
    public static event Action<string, List<Candle>> OnHistoryBatch;
    public static event Action<string, DateTime, Candle> OnInitialHistoryReady;
    public static event Action<string, DateTime, Candle> OnDailyClose;
    public static event Action<string, DateTime, double, double, bool> OnDailyDiff;
    public static event Action<string, double> OnRealtimeTick;

    // ===== 내부 상태 =====
    static readonly Dictionary<string, double> lastClose = new();
    static readonly Dictionary<string, double> yesterdayClose = new();
    static HashSet<string> realtimeTargets;
    static MarketDataRunner runner;
    static float secPerDayCached = 3f;
    static float dayAnchorRealtime = -1f;

    // ===== 서버에서 수신: RPC 진입점 =====
    
    /// <summary>
    /// 서버로부터 초기 히스토리 수신
    /// </summary>
    public static void ReceiveHistoryBatch(string companyId, List<Candle> candles)
    {
        OnHistoryBatch?.Invoke(companyId, candles);
        if (candles.Count > 0)
        {
            var last = candles[^1];
            lastClose[companyId] = last.close;
            if (candles.Count >= 2)
                yesterdayClose[companyId] = candles[^2].close;

            var d = ParseDate(last.time);
            OnInitialHistoryReady?.Invoke(companyId, d, last);
        }
    }

    /// <summary>
    /// 서버로부터 일봉 마감 수신
    /// </summary>
    public static void ReceiveDailyClose(string companyId, Candle closed, double diffAbs, double diffPct)
    {
        var d = ParseDate(closed.time);

        double y = yesterdayClose.TryGetValue(companyId, out var yy) ? yy : closed.open;
        yesterdayClose[companyId] = lastClose.TryGetValue(companyId, out var lc) ? lc : y;
        lastClose[companyId] = closed.close;

        OnDailyClose?.Invoke(companyId, d, closed);
        OnDailyDiff?.Invoke(companyId, d, diffAbs, diffPct, diffAbs >= 0);
    }

    /// <summary>
    /// 서버로부터 실시간 가격 수신
    /// </summary>
    public static void ReceiveRealtimeTick(string companyId, double priceNow)
    {
        OnRealtimeTick?.Invoke(companyId, priceNow);
    }

    // ===== 로컬 실시간 틱 (서버 앵커 기반) =====

    public static void TryStartRealtimeTicksForAll(List<string> companyIds)
    {
        if (runner == null)
        {
            var host = new GameObject("[MarketDataClientRunner]");
            host.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(host);
            runner = host.AddComponent<MarketDataRunner>();

            TimeManager.OnClientNewDay += (date, round, dayIndex) =>
            {
                dayAnchorRealtime = Time.realtimeSinceStartup;
                if (TimeManager.Instance != null)
                    secPerDayCached = TimeManager.Instance.syncedSecondsPerDay;
            };
        }

        if (realtimeTargets == null)
            realtimeTargets = new HashSet<string>();
        foreach (var id in companyIds)
            realtimeTargets.Add(id);

        if (TimeManager.Instance != null)
            secPerDayCached = TimeManager.Instance.syncedSecondsPerDay;

        runner.StartRealtimeLoop(() =>
        {
            if (realtimeTargets == null || realtimeTargets.Count == 0) return;

            if (dayAnchorRealtime < 0f)
                dayAnchorRealtime = Time.realtimeSinceStartup;

            float elapsed = Time.realtimeSinceStartup - dayAnchorRealtime;
            if (secPerDayCached <= 0.001f) secPerDayCached = 3f;
            int daySecond = Mathf.FloorToInt(Mathf.Repeat(elapsed, secPerDayCached));

            foreach (var id in realtimeTargets)
            {
                var price = ComputeRealtimePrice(id, daySecond);
                OnRealtimeTick?.Invoke(id, price);
            }
        });
    }

    private class MarketDataRunner : MonoBehaviour
    {
        bool running;
        Action onTick;

        public void StartRealtimeLoop(Action onTick)
        {
            this.onTick = onTick;
            if (!running) StartCoroutine(TickLoop());
        }

        System.Collections.IEnumerator TickLoop()
        {
            running = true;
            var wait = new WaitForSecondsRealtime(1f);
            while (true)
            {
                onTick?.Invoke();
                yield return wait;
            }
        }
    }

    // ===== 유틸 =====

    static int Seed(string companyId, DateTime? d = null)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + companyId.GetHashCode();
            if (d.HasValue) h = h * 31 + d.Value.Date.GetHashCode();
            return h;
        }
    }

    static DateTime ParseDate(string s)
    {
        if (DateTime.TryParse(s, out var dt)) return dt.Date;
        return DateTime.MinValue;
    }

    static double ComputeRealtimePrice(string companyId, int daySecond)
    {
        double basePrice = lastClose.TryGetValue(companyId, out var p) ? p : 100.0;

        double t = daySecond;
        double wave = Math.Sin((Seed(companyId) % 997 + t) * 0.015) * 0.3
                    + Math.Sin((Seed(companyId) % 431 + t) * 0.007) * 0.2;

        double progress = (secPerDayCached > 0.001f) ? (t / secPerDayCached) : 0.0;
        double trend = (Seed(companyId) % 2 == 0 ? 1 : -1) * (progress - 0.5) * 0.2;

        return basePrice * (1.0 + (wave + trend) * 0.01);
    }
}