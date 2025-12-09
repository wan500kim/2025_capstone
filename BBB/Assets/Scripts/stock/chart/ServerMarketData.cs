using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class ServerMarketData : NetworkBehaviour
{
    public static ServerMarketData Instance { get; private set; }

    [SerializeField] int initialHistoryDays = 60;
    [SerializeField] double basePrice = 1000.0;
    [SerializeField] double sentimentDriftMultiplier = 2.0; // 감정에 따른 drift 배수

    // 내부 상태: 회사별 최신 캔들
    readonly Dictionary<string, Candle> currentCandles = new();
    readonly Dictionary<string, double> yesterdayClose = new();
    readonly Dictionary<string, List<Candle>> history = new();

    // 회사 정보 캐시
    List<CompanyInfo> companies = new();

    // ===== 뉴스 감정 영향 =====
    readonly Dictionary<string, SentimentState> companySentiment = new();

    [Serializable]
    public class SentimentState
    {
        public string sentiment = "neutral"; // positive, neutral, negative
        public double impactStrength = 1.0; // 1.0 = 중립, >1.0 = 긍정, <1.0 = 부정
        public DateTime lastUpdate = DateTime.MinValue;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("[ServerMarketData] 중복 인스턴스 감지");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        
        LoadCompanies();
        InitializeAllCompanies();
        
        // TimeManager 이벤트 구독
        TimeManager.OnServerNewDay += OnServerNewDay;

        // WebSocketHub 구독 (비동기로 대기)
        _ = TrySubscribeToWebSocketHub();
    }

    async System.Threading.Tasks.Task TrySubscribeToWebSocketHub()
    {
        float timeout = 5f;
        while (WebSocketHub.I == null && timeout > 0f)
        {
            await System.Threading.Tasks.Task.Yield();
            timeout -= Time.unscaledDeltaTime;
        }

        if (WebSocketHub.I != null)
        {
            WebSocketHub.OnServerNews += OnReceiveNews;
            Debug.Log("[ServerMarketData] ✓ WebSocketHub.OnServerNews 구독 성공");
        }
        else
        {
            Debug.LogError("[ServerMarketData] ✗ WebSocketHub 타임아웃: 연결 실패");
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        TimeManager.OnServerNewDay -= OnServerNewDay;
        if (WebSocketHub.I != null)
        {
            WebSocketHub.OnServerNews -= OnReceiveNews;
        }
    }

    void LoadCompanies()
    {
        companies.Clear();
        var ta = Resources.Load<TextAsset>("companies");
        if (ta == null)
        {
            Debug.LogError("[ServerMarketData] Resources/companies.json을 찾지 못했습니다.");
            return;
        }
        try
        {
            var parsed = JsonUtility.FromJson<CompaniesRoot>(ta.text);
            if (parsed?.companies != null) companies.AddRange(parsed.companies);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ServerMarketData] companies.json 파싱 실패: {e.Message}");
        }
    }

    void InitializeAllCompanies()
    {
        var asOf = TimeManager.CurrentDate ?? DateTime.Today;

        foreach (var company in companies)
        {
            // 초기 히스토리 생성
            var historyCandles = GenerateHistory(company.id, asOf, initialHistoryDays, basePrice);
            history[company.id] = historyCandles;

            // 감정 상태 초기화
            if (!companySentiment.ContainsKey(company.id))
            {
                companySentiment[company.id] = new SentimentState { sentiment = "neutral", impactStrength = 1.0 };
            }

            if (historyCandles.Count > 0)
            {
                var last = historyCandles[^1];
                currentCandles[company.id] = last;
                if (historyCandles.Count >= 2)
                    yesterdayClose[company.id] = historyCandles[^2].close;
                else
                    yesterdayClose[company.id] = last.open;
            }

            RpcPushHistory(company.id, historyCandles);
        }
    }

    // ===== 뉴스 감정 수신 및 처리 =====

    void OnReceiveNews(WebSocketHub.NewsItem newsItem)
    {
        if (newsItem == null) 
        {
            Debug.LogWarning("[ServerMarketData] 수신한 뉴스가 null입니다");
            return;
        }

        Debug.Log($"[ServerMarketData] 📰 뉴스 수신: {newsItem.company_id} - 감정:{newsItem.sentiment}");

        // sentiment 필드만 사용 (WebSocketHub에서 origin_sentiment로 설정됨)
        string sentiment = newsItem.sentiment ?? "neutral";
        double impactStrength = GetSentimentImpactStrength(sentiment);

        // GLOBAL 뉴스: 모든 회사에 영향
        if (newsItem.company_id.Equals("GLOBAL", System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[ServerMarketData] 🌍 GLOBAL 뉴스 감지 - 모든 회사에 적용");
            foreach (var company in companies)
            {
                ApplySentimentToCompany(company.id, sentiment, impactStrength);
            }
            Debug.Log($"[ServerMarketData] GLOBAL 뉴스 수신 - {sentiment} (강도: {impactStrength}) - 모든 회사에 적용");
        }
        else
        {
            // 특정 회사 뉴스
            ApplySentimentToCompany(newsItem.company_id, sentiment, impactStrength);
            Debug.Log($"[ServerMarketData] 뉴스 수신: {newsItem.company_id} - {sentiment} (강도: {impactStrength})");
        }
    }

    void ApplySentimentToCompany(string companyId, string sentiment, double impactStrength)
    {
        if (string.IsNullOrEmpty(companyId)) return;

        if (!companySentiment.ContainsKey(companyId))
        {
            companySentiment[companyId] = new SentimentState();
        }

        companySentiment[companyId].sentiment = sentiment;
        companySentiment[companyId].impactStrength = impactStrength;
        companySentiment[companyId].lastUpdate = DateTime.UtcNow;

        // 디버그: 감정 적용 확인
        Debug.Log($"[ServerMarketData] ✓ {companyId}에 감정 적용 완료 - 감정:{sentiment}, 강도:{impactStrength:F2}, 갱신시간:{DateTime.UtcNow:HH:mm:ss}");
    }

    /// <summary>
    /// 감정에 따른 영향 강도 계산
    /// positive: > 1.0 (상승 방향)
    /// neutral: = 1.0 (중립)
    /// negative: < 1.0 (하락 방향)
    /// </summary>
    double GetSentimentImpactStrength(string sentiment)
    {
        if (string.IsNullOrEmpty(sentiment)) sentiment = "neutral";

        switch (sentiment.ToLower())
        {
            case "positive":
                return sentimentDriftMultiplier;
            case "negative":
                return 1.0 / sentimentDriftMultiplier;
            case "neutral":
            default:
                return 1.0;
        }
    }

    /// <summary>
    /// 감정 상태를 기반으로 실제 drift 계산
    /// </summary>
    double CalculateSentimentDrift(string companyId, System.Random rng, double baseDrift)
    {
        if (!companySentiment.TryGetValue(companyId, out var state))
            return baseDrift;

        // 최근 뉴스라면 영향 적용, 아니면 중립으로 복귀
        if ((DateTime.UtcNow - state.lastUpdate).TotalMinutes < 5)
        {
            return baseDrift * state.impactStrength;
        }

        return baseDrift;
    }

    // ====== 서버 이벤트 핸들러 ======

    void OnServerNewDay(DateTime newDate, int round, int dayIndex)
    {
        Debug.Log($"[ServerMarketData] OnServerNewDay 호출: {newDate:yyyy-MM-dd}");
        
        foreach (var company in companies)
        {
            double prevClose = currentCandles.TryGetValue(company.id, out var c) ? c.close : basePrice;
            var newCandle = GenerateDailyCandle(company.id, newDate, prevClose);

            // 상태 업데이트
            if (currentCandles.TryGetValue(company.id, out var current))
                yesterdayClose[company.id] = current.close;
            currentCandles[company.id] = newCandle;

            // 히스토리에 추가
            if (!history.TryGetValue(company.id, out var hist))
            {
                hist = new List<Candle>();
                history[company.id] = hist;
            }
            hist.Add(newCandle);

            // 등락율 계산
            double y = yesterdayClose.TryGetValue(company.id, out var yy) ? yy : newCandle.open;
            double diffAbs = newCandle.close - y;
            double diffPct = y != 0 ? diffAbs / y * 100.0 : 0.0;

            RpcPushDailyClose(company.id, newCandle, diffAbs, diffPct);
        }
    }

    // ====== RPC: 모든 클라이언트에 데이터 푸시 ======

    [ClientRpc]
    void RpcPushHistory(string companyId, List<Candle> candles)
    {
        MarketDataClient.ReceiveHistoryBatch(companyId, candles);
    }

    [ClientRpc]
    void RpcPushDailyClose(string companyId, Candle closed, double diffAbs, double diffPct)
    {
        MarketDataClient.ReceiveDailyClose(companyId, closed, diffAbs, diffPct);
    }

    [ClientRpc]
    void RpcPushRealtimeTick(string companyId, double priceNow)
    {
        MarketDataClient.ReceiveRealtimeTick(companyId, priceNow);
    }

    // ====== 서버 전용: 주가 데이터 생성 ======

    List<Candle> GenerateHistory(string companyId, DateTime asOfDate, int days, double startPrice)
    {
        var rng = new System.Random(Seed(companyId));
        var list = new List<Candle>(days);
        var cur = asOfDate.Date.AddDays(-days + 1);

        double prev = startPrice + (companyId.GetHashCode() % 50);
        for (int i = 0; i < days; i++)
        {
            double baseDrift = (rng.NextDouble() - 0.5) * 4.0;
            double drift = CalculateSentimentDrift(companyId, rng, baseDrift);
            
            double open = prev;
            double close = open + drift;
            double high = Math.Max(open, close) + rng.NextDouble() * 1.5;
            double low = Math.Min(open, close) - rng.NextDouble() * 1.5;
            double vol = 50000 + rng.Next(0, 120000);

            list.Add(new Candle
            {
                time = cur.ToString("yyyy-MM-dd"),
                open = open,
                high = high,
                low = low,
                close = close,
                volume = vol
            });

            prev = close;
            cur = cur.AddDays(1);
        }

        return list;
    }

    Candle GenerateDailyCandle(string companyId, DateTime date, double lastClosePrice)
    {
        var rng = new System.Random(Seed(companyId, date));
        double baseDrift = (rng.NextDouble() - 0.5) * 4.0;
        double drift = CalculateSentimentDrift(companyId, rng, baseDrift);

        // 디버그: 감정 영향 확인
        if (companySentiment.TryGetValue(companyId, out var state))
        {
            bool isRecentNews = (DateTime.UtcNow - state.lastUpdate).TotalMinutes < 5;
            if (isRecentNews)
            {
                var company = companies.Find(c => c.id == companyId);
                string companyName = company != null ? company.kor_name : "Unknown";
                Debug.Log($"[ServerMarketData] {date:yyyy-MM-dd} | {companyId} ({companyName}) | 감정: {state.sentiment}");
            }
        }

        double open = lastClosePrice;
        double close = open + drift;
        double high = Math.Max(open, close) + rng.NextDouble() * 1.2;
        double low = Math.Min(open, close) - rng.NextDouble() * 1.2;
        double vol = 50000 + rng.Next(0, 120000);

        return new Candle
        {
            time = date.ToString("yyyy-MM-dd"),
            open = open,
            high = high,
            low = low,
            close = close,
            volume = vol
        };
    }

    // ====== 유틸 ======

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

    public static double GetCurrentPrice(string companyId)
    {
        if (Instance == null) return 100.0;
        return Instance.currentCandles.TryGetValue(companyId, out var c) ? c.close : 100.0;
    }

    public static List<Candle> GetHistory(string companyId)
    {
        if (Instance == null) return new List<Candle>();
        return Instance.history.TryGetValue(companyId, out var h) ? new List<Candle>(h) : new List<Candle>();
    }
}