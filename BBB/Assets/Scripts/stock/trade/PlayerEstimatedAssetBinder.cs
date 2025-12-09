using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 실시간 추정자산 계산기
/// - 현금 + Σ(보유수량 * 현재가)
/// - 포트폴리오/현금/시세 이벤트마다 재계산
/// - EstimatedEquityBus로 방송 + 서버에 SyncVar 업데이트
/// </summary>
public sealed class PlayerEstimatedAssetBinder : MonoBehaviour
{
    PlayerState ps;
    PlayerEstimatedAssetSync sync;
    readonly Dictionary<string, double> priceCache = new();

    void OnEnable()
    {
        MarketDataClient.OnRealtimeTick += OnTick;
        MarketDataClient.OnDailyClose   += OnDailyClose;
        MarketDataClient.OnHistoryBatch += OnHistoryBatch;
        StartCoroutine(CoBindLocalPlayer());
    }
    void OnDisable()
    {
        MarketDataClient.OnRealtimeTick -= OnTick;
        MarketDataClient.OnDailyClose   -= OnDailyClose;
        MarketDataClient.OnHistoryBatch -= OnHistoryBatch;
        if (ps != null) { ps.portfolio.OnChange -= OnPortfolioChanged; ps.OnCashChanged -= OnCashChanged; }
    }

    System.Collections.IEnumerator CoBindLocalPlayer()
    {
        while (ps == null)
        {
            var lp = NetworkClient.active ? NetworkClient.localPlayer : null;
            if (lp != null) ps = lp.GetComponent<PlayerState>();
            if (ps != null) break;
            yield return null;
        }
        sync = ps.GetComponent<PlayerEstimatedAssetSync>(); // 프리팹에 미리 부착 권장
        ps.portfolio.OnChange += OnPortfolioChanged;
        ps.OnCashChanged      += OnCashChanged;
        RecomputeAndBroadcast();
    }

    void OnPortfolioChanged(SyncList<OwnedStock>.Operation _, int __, OwnedStock ___) => RecomputeAndBroadcast();
    void OnCashChanged(long _, long __) => RecomputeAndBroadcast();

    void OnTick(string cid, double p) { priceCache[cid] = p; if (Owns(cid)) RecomputeAndBroadcast(); }
    void OnDailyClose(string cid, DateTime _, Candle c) { priceCache[cid] = c.close; if (Owns(cid)) RecomputeAndBroadcast(); }
    void OnHistoryBatch(string cid, List<Candle> cs) { if (cs != null && cs.Count > 0) { priceCache[cid] = cs[^1].close; if (Owns(cid)) RecomputeAndBroadcast(); } }

    bool Owns(string cid)
    {
        if (ps == null) return false;
        foreach (var s in ps.portfolio) if (s.stockId == cid) return true;
        return false;
    }

    void RecomputeAndBroadcast()
    {
        if (ps == null) return;
        long cash = ps.CashCents;
        long stocks = 0;
        foreach (var s in ps.portfolio)
        {
            double price = priceCache.TryGetValue(s.stockId, out var p) ? p : ServerMarketData.GetCurrentPrice(s.stockId);
            stocks += (long)Math.Round(price * s.quantity * 100.0);
        }
        long total = cash + stocks;

        // 네트워크 SyncVar 업데이트(로컬 소유 클라이언트에서만 호출)
        if (sync != null && sync.isOwned) sync.CmdSetEstimatedEquity(total);

        EstimatedEquityBus.LatestCents = total;
        EstimatedEquityBus.Raise(total);
    }
}

public static class EstimatedEquityBus
{
    public static event Action<long> OnUpdated;
    public static long LatestCents { get; set; }
    public static void Raise(long cents) => OnUpdated?.Invoke(cents);
}
