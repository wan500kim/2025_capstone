using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class CompanyListController : MonoBehaviour
{
    [Header("UI")]
    public UIDocument uiDocument;

    [Header("Resources")]
    public string companiesJsonPathInResources = "companies";
    public string companyImgFolderInResources = "company_img";

    public event Action<CompanyInfo> OnCompanySelected;

    ScrollView stockList;
    readonly Dictionary<string, VisualElement> rowById = new();
    readonly List<CompanyInfo> companies = new();
    readonly Dictionary<string, double> lastPrice = new();
    readonly Dictionary<string, (double abs, double pct, bool up)> lastDiff = new();

    void Awake()
    {
        if (!uiDocument) uiDocument = GetComponent<UIDocument>();
    }

    void OnEnable()
    {
        var root = uiDocument.rootVisualElement;
        stockList = root.Q<ScrollView>("stock_list");
        if (stockList == null)
        {
            Debug.LogError("[CompanyListController] #stock_list 를 찾을 수 없습니다.");
            return;
        }

        // ★ 서버에서 푸시된 데이터만 수신
        MarketDataClient.OnHistoryBatch += OnHistoryBatch;
        MarketDataClient.OnDailyClose   += OnDailyClose;
        MarketDataClient.OnDailyDiff    += OnDailyDiff;
        MarketDataClient.OnRealtimeTick += OnRealtimeTick;

        LoadCompanies();
        BuildList();
        BootstrapRealtimeTicks();
    }

    void OnDisable()
    {
        MarketDataClient.OnHistoryBatch -= OnHistoryBatch;
        MarketDataClient.OnDailyClose   -= OnDailyClose;
        MarketDataClient.OnDailyDiff    -= OnDailyDiff;
        MarketDataClient.OnRealtimeTick -= OnRealtimeTick;
    }

    void LoadCompanies()
    {
        companies.Clear();
        var ta = Resources.Load<TextAsset>(companiesJsonPathInResources);
        if (ta == null)
        {
            Debug.LogError($"[CompanyListController] Resources/{companiesJsonPathInResources}.json 을 찾지 못했습니다.");
            return;
        }
        try
        {
            var parsed = JsonUtility.FromJson<CompaniesRoot>(ta.text);
            if (parsed?.companies != null) companies.AddRange(parsed.companies);
        }
        catch (Exception e)
        {
            Debug.LogError($"[CompanyListController] companies.json 파싱 실패: {e.Message}");
        }
    }

    void BuildList()
    {
        stockList.Clear();
        rowById.Clear();

        foreach (var c in companies)
        {
            var row = new VisualElement();
            row.AddToClassList("list_item");
            row.RegisterCallback<ClickEvent>(_ => OnCompanySelected?.Invoke(c));

            var img = new VisualElement();
            img.AddToClassList("ticker_list_img");
            var tex = Resources.Load<Texture2D>($"{companyImgFolderInResources}/{c.id}");
            if (tex) img.style.backgroundImage = new StyleBackground(tex);

            var nameKor = new Label(c.kor_name); nameKor.AddToClassList("ticker_list_name_kor");
            var nameEng = new Label(c.ticker);   nameEng.AddToClassList("ticker_list_name_eng");

            var price = new Label("$—");         price.AddToClassList("list_price");
            var diff  = new Label("—");          diff.AddToClassList("diff_value_up");

            row.Add(img); row.Add(nameKor); row.Add(nameEng); row.Add(price); row.Add(diff);
            stockList.Add(row);
            rowById[c.id] = row;
        }

        if (companies.Count > 0) OnCompanySelected?.Invoke(companies[0]);
    }

    void BootstrapRealtimeTicks()
    {
        // ★ 모든 종목의 실시간 틱 시작 (서버에서 푸시됨)
        MarketDataClient.TryStartRealtimeTicksForAll(companies.ConvertAll(x => x.id));
    }

    void OnHistoryBatch(string companyId, List<Candle> candles)
    {
        if (candles == null || candles.Count == 0) return;
        lastPrice[companyId] = candles[^1].close;
        UpdateRowPrice(companyId, candles[^1].close);
    }

    void OnDailyClose(string companyId, DateTime date, Candle closed)
    {
        lastPrice[companyId] = closed.close;
        UpdateRowPrice(companyId, closed.close);
    }

    void OnDailyDiff(string companyId, DateTime date, double abs, double pct, bool isUp)
    {
        lastDiff[companyId] = (abs, pct, isUp);
        UpdateRowDiff(companyId, abs, pct, isUp);
    }

    void OnRealtimeTick(string companyId, double priceNow)
    {
        UpdateRowPrice(companyId, priceNow);
    }

    void UpdateRowPrice(string companyId, double close)
    {
        if (!rowById.TryGetValue(companyId, out var row)) return;
        var price = row.Q<Label>(className: "list_price");
        if (price != null) price.text = $"${close:F2}";
    }

    void UpdateRowDiff(string companyId, double abs, double pct, bool isUp)
    {
        if (!rowById.TryGetValue(companyId, out var row)) return;
        var diff = row.Q<Label>(className: "diff_value_up") ?? row.Q<Label>(className: "diff_value_down");
        if (diff == null) return;

        diff.RemoveFromClassList("diff_value_up");
        diff.RemoveFromClassList("diff_value_down");
        diff.AddToClassList(isUp ? "diff_value_up" : "diff_value_down");

        var sign = isUp ? "+" : "-";
        diff.text = $"{sign}${Mathf.Abs((float)abs):F2}({Mathf.Abs((float)pct):F1}%)";
    }
}

[System.Serializable] public class CompaniesRoot { public List<IndustryInfo> industries; public List<CompanyInfo> companies; }
[System.Serializable] public class IndustryInfo { public string industry_id; public string industry_name; }
[System.Serializable] public class CompanyInfo { public string id; public string kor_name; public string ticker; public string industry_id; }

