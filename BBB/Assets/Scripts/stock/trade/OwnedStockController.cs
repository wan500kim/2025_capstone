using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public sealed class OwnedStockController : MonoBehaviour
{
    [Header("UI")]
    public UIDocument uiDocument;

    VisualElement ownedStockPanel;
    ScrollView    ownedList;

    PlayerState ps;
    readonly Dictionary<string, double> priceCache = new();
    readonly Dictionary<string, (string nameKor, string ticker)> companyCache = new();

    void Reset() => uiDocument = GetComponent<UIDocument>();

    void OnEnable()
    {
        if (!uiDocument) uiDocument = GetComponent<UIDocument>();
        BindUI();

        MarketDataClient.OnHistoryBatch += OnHistoryBatch;
        MarketDataClient.OnDailyClose   += OnDailyClose;
        MarketDataClient.OnRealtimeTick += OnRealtimeTick;

        LoadCompaniesMeta();
        StartCoroutine(CoBindLocalPlayer());
    }

    void OnDisable()
    {
        MarketDataClient.OnHistoryBatch -= OnHistoryBatch;
        MarketDataClient.OnDailyClose   -= OnDailyClose;
        MarketDataClient.OnRealtimeTick -= OnRealtimeTick;
        UnsubscribePortfolio();
    }

    void BindUI()
    {
        var root = uiDocument.rootVisualElement;
        ownedStockPanel = root.Q<VisualElement>("owned_stock_panel");
        ownedList       = root.Q<ScrollView>("owned_list");
        if (ownedList == null)
            Debug.LogError("[OwnedStockController] #owned_list 를 찾을 수 없습니다.");
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
        SubscribePortfolio();
        ForceRefresh();
    }

    void SubscribePortfolio()
    {
        if (ps == null) return;
        ps.portfolio.OnChange += OnPortfolioChanged;
    }
    void UnsubscribePortfolio()
    {
        if (ps == null) return;
        ps.portfolio.OnChange -= OnPortfolioChanged;
    }

    void OnPortfolioChanged(SyncList<OwnedStock>.Operation op, int index, OwnedStock item) => ForceRefresh();

    // 외부 API(명세 유지용)
    public sealed class PlayerPortfolio { public List<OwnedStock> stocks = new(); }
    public void UpdateOwnedStockList(PlayerPortfolio portfolio)
    {
        if (ownedList == null) return;
        ownedList.Clear();

        foreach (var owned in portfolio.stocks)
        {
            var stockId = owned.stockId;
            var price   = GetLatestPrice(stockId);
            var (nameKor, ticker) = GetCompanyLabels(stockId);

            double profit = (price - owned.averagePurchasePrice) * owned.quantity;
            double rate   = owned.averagePurchasePrice > 0.0 ? ((price / owned.averagePurchasePrice) - 1.0) * 100.0 : 0.0;

            var item = CreateListItem(
                stockId,
                nameKor,
                ticker,
                $"{owned.quantity}주",
                $"${price:F2}",
                profit,
                rate
            );

            ownedList.Add(item);
        }
    }

    void ForceRefresh()
    {
        if (ps == null || ownedList == null) return;
        var pp = new PlayerPortfolio();
        foreach (var s in ps.portfolio) pp.stocks.Add(s);
        UpdateOwnedStockList(pp);
    }

    /* ===== 아이템 생성(회사 로고 포함) ===== */
    VisualElement CreateListItem(string stockId, string nameKor, string ticker, string qtyText, string priceText, double profit, double rate)
    {
        var root = new VisualElement();
        root.AddToClassList("list_item");

        var img = new VisualElement();
        img.AddToClassList("ticker_list_img");
        // 2) 회사 로고 로드: Resources/company_img/{stockId}
        var tex = LoadCompanyLogo(stockId);
        if (tex != null)
        {
            img.style.backgroundImage = new StyleBackground(tex);
            img.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
        }
        root.Add(img);

        var nameKorLabel = new Label(nameKor);
        nameKorLabel.AddToClassList("ticker_list_name_kor");
        root.Add(nameKorLabel);

        var nameEngLabel = new Label(ticker);
        nameEngLabel.AddToClassList("ticker_list_name_eng");
        root.Add(nameEngLabel);

        var qtyLabel = new Label(qtyText);
        qtyLabel.AddToClassList("ticker_amount");
        root.Add(qtyLabel);
        
        var priceLabel = new Label(priceText);
        priceLabel.AddToClassList("list_price");
        root.Add(priceLabel);

        var diffLabel = new Label();
        var sign = profit >= 0 ? "+" : "-";
        diffLabel.text = $"{sign}${Mathf.Abs((float)profit):F2}({Mathf.Abs((float)rate):F1}%)";
        diffLabel.AddToClassList(profit >= 0 ? "diff_value_up" : "diff_value_down");
        root.Add(diffLabel);

        return root;
    }

    Texture2D LoadCompanyLogo(string stockId)
    {
        // 우선 png, jpg 순서로 시도
        var tex = Resources.Load<Texture2D>($"company_img/{stockId}");
        if (tex != null) return tex;
        // fallback: ticker 기반 시도
        if (companyCache.TryGetValue(stockId, out var meta))
        {
            tex = Resources.Load<Texture2D>($"company_img/{meta.ticker}");
            if (tex != null) return tex;
        }
        // 기본 아이콘
        return Resources.Load<Texture2D>("company_img/default_icon");
    }

    /* ===== 가격 캐시 ===== */
    void OnHistoryBatch(string companyId, List<Candle> candles)
    {
        if (candles != null && candles.Count > 0) priceCache[companyId] = candles[^1].close;
        if (Owns(companyId)) ForceRefresh();
    }
    void OnDailyClose(string companyId, DateTime date, Candle closed)
    {
        priceCache[companyId] = closed.close;
        if (Owns(companyId)) ForceRefresh();
    }
    void OnRealtimeTick(string companyId, double priceNow)
    {
        priceCache[companyId] = priceNow;
        if (Owns(companyId)) ForceRefresh();
    }
    bool Owns(string companyId)
    {
        if (ps == null) return false;
        foreach (var s in ps.portfolio) if (s.stockId == companyId) return true;
        return false;
    }
    double GetLatestPrice(string stockId)
    {
        if (priceCache.TryGetValue(stockId, out var p)) return p;
        return ServerMarketData.GetCurrentPrice(stockId);
    }

    /* ===== 종목 메타 캐시 ===== */
    void LoadCompaniesMeta()
    {
        companyCache.Clear();
        var ta = Resources.Load<TextAsset>("companies");
        if (ta == null) return;
        try
        {
            var root = JsonUtility.FromJson<CompaniesRoot>(ta.text);
            if (root?.companies == null) return;
            foreach (var c in root.companies)
                companyCache[c.id] = (c.kor_name, c.ticker);
        }
        catch { }
    }
    (string nameKor, string ticker) GetCompanyLabels(string stockId)
    {
        if (companyCache.TryGetValue(stockId, out var v)) return v;
        return (stockId, stockId);
    }
}
