using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class TimeSyncedCandleFeed : MonoBehaviour
{
    [Header("Refs")]
    public CandleChart chart;
    public CompanyListController companyList;
    public UIDocument uiDocument;

    string selectedCompanyId;
    CompanyInfo selectedInfo;
    Candle lastClosed;

    // 헤더 UI
    VisualElement tickerImg;
    Label labelKo, labelEng, labelPrice, labelDiff;
    Label labelDiffDate; // ← "9월 20일보다 " 를 출력하는 라벨(클래스: stock_diff_label)

    // 회사별 서버 소스 캐시
    readonly Dictionary<string, List<Candle>> cacheByCompany = new();

    void Awake()
    {
        if (chart == null) chart = GetComponent<CandleChart>();
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
    }

    void OnEnable()
    {
        if (companyList == null) companyList = FindObjectOfType<CompanyListController>();
        if (companyList != null) companyList.OnCompanySelected += OnCompanySelected;

        MarketDataClient.OnHistoryBatch += OnHistoryBatch;
        MarketDataClient.OnDailyClose   += OnDailyClose;
        MarketDataClient.OnRealtimeTick += OnRealtimeTick;
        MarketDataClient.OnDailyDiff    += OnDailyDiff;

        BindHeaderRefs();
    }

    void OnDisable()
    {
        if (companyList != null) companyList.OnCompanySelected -= OnCompanySelected;

        MarketDataClient.OnHistoryBatch -= OnHistoryBatch;
        MarketDataClient.OnDailyClose   -= OnDailyClose;
        MarketDataClient.OnRealtimeTick -= OnRealtimeTick;
        MarketDataClient.OnDailyDiff    -= OnDailyDiff;
    }

    // ========== 선택 전환 ==========
    void OnCompanySelected(CompanyInfo info)
    {
        selectedCompanyId = info.id;
        selectedInfo = info;
        lastClosed = null;
        UpdateHeaderStatic(info);

        if (cacheByCompany.TryGetValue(info.id, out var cached) && cached != null && cached.Count > 0)
        {
            var copy = new List<Candle>(cached);      // 차트에는 복사본만 전달
            chart.SetData(copy);
            lastClosed = copy[^1];
            if (labelPrice != null) labelPrice.text = $"${lastClosed.close:F2}";
            UpdatePrevDateLabel(ParseDate(lastClosed.time).AddDays(-1)); // 전일 날짜 표시
        }
        else
        {
            chart.SetData(new List<Candle>());
            if (labelPrice != null) labelPrice.text = "$—.—";
            if (labelDiff  != null) labelDiff.text  = "";
            UpdatePrevDateLabel(null); // 표시 비움
        }
    }

    // ========== 서버 수신 ==========
    void OnHistoryBatch(string companyId, List<Candle> candles)
    {
        if (candles == null || candles.Count == 0) return;

        // 캐시는 항상 자기 복사로 보관
        cacheByCompany[companyId] = new List<Candle>(candles);

        if (companyId == selectedCompanyId)
        {
            var copy = new List<Candle>(cacheByCompany[companyId]);
            chart.SetData(copy);
            lastClosed = copy[^1];
            if (labelPrice != null) labelPrice.text = $"${lastClosed.close:F2}";
            UpdatePrevDateLabel(ParseDate(lastClosed.time).AddDays(-1));
        }
    }

    void OnDailyClose(string companyId, DateTime date, Candle closed)
    {
        // 캐시 보장 및 중복 방지
        if (!cacheByCompany.TryGetValue(companyId, out var list) || list == null)
        {
            list = new List<Candle>();
            cacheByCompany[companyId] = list;
        }
        bool dup = list.Count > 0 && list[^1].time == closed.time;
        if (dup) list[^1] = closed; else list.Add(closed);

        if (companyId == selectedCompanyId)
        {
            if (dup) chart.UpdateRealtimeCandle(closed);
            else     chart.AppendClosedCandle(closed);

            lastClosed = closed;
            if (labelPrice != null) labelPrice.text = $"${closed.close:F2}";
            UpdatePrevDateLabel(date.AddDays(-1));
        }
    }

    void OnRealtimeTick(string companyId, double priceNow)
    {
        if (companyId != selectedCompanyId) return;
        if (lastClosed == null || lastClosed.time == null) return;

        // 진행 중 봉은 차트만 업데이트(캐시는 건드리지 않음)
        var rt = new Candle
        {
            time   = lastClosed.time,
            open   = lastClosed.open,
            high   = Math.Max(lastClosed.high, priceNow),
            low    = Math.Min(lastClosed.low,  priceNow),
            close  = priceNow,
            volume = lastClosed.volume
        };
        chart.UpdateRealtimeCandle(rt);
        if (labelPrice != null) labelPrice.text = $"${priceNow:F2}";
        // 전일 날짜 라벨은 장중에 바뀌지 않음
    }

    void OnDailyDiff(string companyId, DateTime date, double abs, double pct, bool isUp)
    {
        if (companyId != selectedCompanyId) return;
        if (labelDiff == null) return;

        labelDiff.RemoveFromClassList("diff_value_up");
        labelDiff.RemoveFromClassList("diff_value_down");
        labelDiff.AddToClassList(isUp ? "diff_value_up" : "diff_value_down");

        var sign = isUp ? "+" : "-";
        labelDiff.text = $"{sign}${Mathf.Abs((float)abs):F2}({Mathf.Abs((float)pct):F1}%)";
        // 전일 날짜는 OnDailyClose에서 이미 갱신됨
    }

    // ========== 헤더 ==========
    void BindHeaderRefs()
    {
        var root = uiDocument != null ? uiDocument.rootVisualElement : null;
        if (root == null) return;

        var info = root.Q<VisualElement>("ticker_info");
        if (info == null) return;

        tickerImg = info.Q<VisualElement>(className: "ticker_image");
        var ticker = info.Q<VisualElement>("ticker");
        var name   = ticker?.Q<VisualElement>("ticker_name");

        labelKo    = name?.Q<Label>(className: "ticker_ko");
        labelEng   = name?.Q<Label>(className: "ticker_eng");
        labelPrice = ticker?.Q<Label>(className: "price");

        var diffWrap = info.Q<VisualElement>(className: "stock_diff");
        // 값 라벨
        labelDiff = diffWrap?.Q<Label>(className: "stock_diff_value")
                ??  diffWrap?.Q<Label>(className: "diff_value_up")
                ??  diffWrap?.Q<Label>(className: "diff_value_down");
        // 전일 날짜 라벨(사용자 UXML에 class="stock_diff_label")
        labelDiffDate = diffWrap?.Q<Label>(className: "stock_diff_label");
    }

    void UpdateHeaderStatic(CompanyInfo info)
    {
        if (tickerImg != null)
        {
            var tex = Resources.Load<Texture2D>($"company_img/{info.id}");
            if (tex != null) tickerImg.style.backgroundImage = new StyleBackground(tex);
        }
        if (labelKo  != null) labelKo.text  = info.kor_name;
        if (labelEng != null) labelEng.text = info.ticker;
    }

    // "9월 20일보다 " 형태로 전일 날짜를 표시. null이면 공백 처리.
    void UpdatePrevDateLabel(DateTime? prevDate)
    {
        if (labelDiffDate == null) return;

        if (prevDate.HasValue)
        {
            var d = prevDate.Value;
            labelDiffDate.text = $"{d.Month}월 {d.Day}일보다 ";
        }
        else
        {
            labelDiffDate.text = "";
        }
    }

    static DateTime ParseDate(string s)
    {
        if (DateTime.TryParse(s, out var dt)) return dt;
        return DateTime.UtcNow.Date;
    }
}
