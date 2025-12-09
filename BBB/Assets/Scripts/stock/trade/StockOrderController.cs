using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 주문 패널 컨트롤러
/// - 카드 제목에 "회사명 주문하기"
/// - 첫 로딩 시 첫 번째 회사 자동 선택
/// - 시세 이벤트로 현재가와 요약 실시간 갱신
/// - 입력 검증 + 서버 연동
/// </summary>
[RequireComponent(typeof(UIDocument))]
public sealed class StockOrderController : MonoBehaviour
{
    [Header("Dependencies")]
    public UIDocument uiDocument;
    public CompanyListController companyList;

    VisualElement orderPanel;
    Label        cardTitle;          // order_panel 내부의 name="card_title"
    Label        orderPrice;         // class="order_price"
    IntegerField inputQty;           // name="input_qty"
    Label        playerCash;         // class="player_cash"
    Label        orderSummaryQty;    // class="order_summary_qty"
    Label        orderSummaryTotal;  // class="order_summary_total"
    Button       buttonBuy;          // name="button_buy"
    Button       buttonSell;         // name="button_sell"

    PlayerState ps;
    TradeCommandProxy proxy;   // 프리팹에 미리 부착
    CompanyInfo currentCompany;
    double      currentPrice;
    string      currentStockId;

    void Reset() => uiDocument = GetComponent<UIDocument>();

    void OnEnable()
    {
        if (!uiDocument) uiDocument = GetComponent<UIDocument>();
        BindUI();

        StartCoroutine(CoBindLocalPlayer());

        if (companyList != null) companyList.OnCompanySelected += OnCompanySelected;
        if (inputQty != null) inputQty.RegisterValueChangedCallback(_ => RefreshOrderSummary());
        if (buttonBuy  != null) buttonBuy.clicked  += OnBuyButtonClick;
        if (buttonSell != null) buttonSell.clicked += OnSellButtonClick;

        MarketDataClient.OnRealtimeTick += OnRealtimeTick;
        MarketDataClient.OnDailyClose   += OnDailyClose;
        MarketDataClient.OnHistoryBatch += OnHistoryBatch;

        SetTexts("—", "—", 0, 0);
        SetButtonsInteractable(false);

        StartCoroutine(CoSelectFirstCompanyOnceReady());
    }

    void OnDisable()
    {
        if (companyList != null) companyList.OnCompanySelected -= OnCompanySelected;
        if (buttonBuy  != null) buttonBuy.clicked  -= OnBuyButtonClick;
        if (buttonSell != null) buttonSell.clicked -= OnSellButtonClick;

        MarketDataClient.OnRealtimeTick -= OnRealtimeTick;
        MarketDataClient.OnDailyClose   -= OnDailyClose;
        MarketDataClient.OnHistoryBatch -= OnHistoryBatch;
    }

    void BindUI()
    {
        var root = uiDocument.rootVisualElement;
        orderPanel        = root.Q<VisualElement>("order_panel");
        cardTitle         = orderPanel != null ? orderPanel.Q<Label>("card_title") : root.Q<Label>("card_title");
        orderPrice        = root.Q<Label>(className: "order_price");
        inputQty          = root.Q<IntegerField>("input_qty");
        playerCash        = root.Q<Label>(className: "player_cash");
        orderSummaryQty   = root.Q<Label>(className: "order_summary_qty");
        orderSummaryTotal = root.Q<Label>(className: "order_summary_total");
        buttonBuy         = root.Q<Button>("button_buy");
        buttonSell        = root.Q<Button>("button_sell");

        if (cardTitle == null) Debug.LogWarning("[StockOrderController] 'card_title' 라벨을 찾을 수 없습니다.");
        if (inputQty != null) { inputQty.isDelayed = false; inputQty.value = 0; }
    }

    IEnumerator CoBindLocalPlayer()
    {
        while (ps == null)
        {
            var lp = NetworkClient.active ? NetworkClient.localPlayer : null;
            if (lp != null) ps = lp.GetComponent<PlayerState>();
            if (ps != null) break;
            yield return null;
        }
        proxy = ps != null ? ps.GetComponent<TradeCommandProxy>() : null;
        if (proxy == null) { Debug.LogError("[StockOrderController] TradeCommandProxy가 프리팹에 없습니다."); yield break; }

        proxy.OnClientFeedback -= OnServerFeedback;
        proxy.OnClientFeedback += OnServerFeedback;

        UpdatePlayerCashUI(ps.CashCents);
        ps.OnCashChanged += (_, newVal) => UpdatePlayerCashUI(newVal);
        SetButtonsInteractable(proxy != null && ps != null && !string.IsNullOrEmpty(currentStockId) && currentPrice > 0);
    }

    IEnumerator CoSelectFirstCompanyOnceReady()
    {
        if (companyList == null) yield break;
        yield return null;

        var first = TryGetFirstCompany(companyList);
        if (first != null) OnCompanySelected(first);
    }

    static CompanyInfo TryGetFirstCompany(CompanyListController cl)
    {
        if (cl == null) return null;

        var t = cl.GetType();
        var props = t.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        foreach (var p in props)
        {
            if (typeof(IEnumerable<CompanyInfo>).IsAssignableFrom(p.PropertyType))
            {
                var seq = p.GetValue(cl) as IEnumerable<CompanyInfo>;
                var first = seq?.FirstOrDefault();
                if (first != null) return first;
            }
        }
        var fields = t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        foreach (var f in fields)
        {
            if (typeof(IEnumerable<CompanyInfo>).IsAssignableFrom(f.FieldType))
            {
                var seq = f.GetValue(cl) as IEnumerable<CompanyInfo>;
                var first = seq?.FirstOrDefault();
                if (first != null) return first;
            }
        }
        return null;
    }

    // 외부 초기화 API
    public void UpdateOrderPanel(StockData stock, PlayerState player)
    {
        currentCompany = new CompanyInfo { id = stock.Id, kor_name = stock.Name, ticker = stock.Ticker };
        currentStockId = stock.Id;
        currentPrice   = stock.CurrentPrice;
        if (player != null) ps = player;

        SetTexts($"{stock.Name} 주문하기", FormatPrice(currentPrice), ps != null ? ps.CashCents : 0, 0);
        RefreshOrderSummary();
        SetButtonsInteractable(proxy != null && ps != null && !string.IsNullOrEmpty(currentStockId) && currentPrice > 0);
    }

    void OnCompanySelected(CompanyInfo c)
    {
        if (c == null) return;
        currentCompany = c;
        currentStockId = c.id;
        currentPrice   = ServerMarketData.GetCurrentPrice(c.id);

        SetTexts($"{c.kor_name} 주문하기", FormatPrice(currentPrice), ps != null ? ps.CashCents : 0, 0);
        RefreshOrderSummary();
        SetButtonsInteractable(proxy != null && ps != null && currentPrice > 0);
    }

    /* ===== 시세 반영 ===== */
    void OnRealtimeTick(string companyId, double priceNow)
    {
        if (companyId != currentStockId) return;
        currentPrice = priceNow;
        if (orderPrice != null) orderPrice.text = FormatPrice(currentPrice);
        RefreshOrderSummary();
    }
    void OnDailyClose(string companyId, DateTime date, Candle closed)
    {
        if (companyId != currentStockId) return;
        currentPrice = closed.close;
        if (orderPrice != null) orderPrice.text = FormatPrice(currentPrice);
        RefreshOrderSummary();
    }
    void OnHistoryBatch(string companyId, List<Candle> candles)
    {
        if (companyId != currentStockId || candles == null || candles.Count == 0) return;
        currentPrice = candles[^1].close;
        if (orderPrice != null) orderPrice.text = FormatPrice(currentPrice);
        RefreshOrderSummary();
    }

    /* ===== 주문 처리 ===== */
    void OnBuyButtonClick()
    {
        if (!ValidateReady()) return;
        int qty = Mathf.Max(0, inputQty.value);
        if (qty <= 0) { 
            GameAudio.Play("block");
            Feedback("수량을 입력하세요.", false);
            return;
        }

        long totalCents = CalcTotalCents(currentPrice, qty);
        if (ps.CashCents < totalCents) {
            GameAudio.Play("block");
            Feedback("보유 현금이 부족합니다.", false); 
            return; 
        }

        if (!proxy.isOwned)
        {
            GameAudio.Play("block");
            Feedback("권한이 없는 플레이어입니다.", false);
            return;
        }
        
        GameAudio.Play("buy_sound");
        proxy.CmdBuy(currentStockId, qty, ToCents(currentPrice));
    }

    void OnSellButtonClick()
    {
        if (!ValidateReady()) return;
        int qty = Mathf.Max(0, inputQty.value);
        if (qty <= 0) {
            GameAudio.Play("block");
            Feedback("수량을 입력하세요.", false);
            return; 
        }

        int holding = ps.GetHoldingQuantity(currentStockId);
        if (holding < qty) {
            GameAudio.Play("block");
            Feedback("보유 수량이 부족합니다.", false);
            return; 
            }

        if (!proxy.isOwned)
        {
            GameAudio.Play("block");
            Feedback("권한이 없는 플레이어입니다.", false);
            return;
        }
        
        GameAudio.Play("sell_sound");
        proxy.CmdSell(currentStockId, qty, ToCents(currentPrice));
    }

    /* ===== 보조 ===== */
    void RefreshOrderSummary()
    {
        int qty = Mathf.Max(0, inputQty != null ? inputQty.value : 0);
        if (orderSummaryQty   != null) orderSummaryQty.text   = $"주문 수량: {qty}주";
        if (orderSummaryTotal != null) orderSummaryTotal.text = $"주문 총액: {FormatMoney(CalcTotalCents(currentPrice, qty))}";
    }

    void OnServerFeedback(bool ok, string message)
    {
        Feedback(message, ok);
        if (ok && inputQty != null)
        {
            inputQty.value = 0;
            RefreshOrderSummary();
        }
    }

    bool ValidateReady()
    {
        if (ps == null || proxy == null) { Feedback("플레이어 연결이 준비되지 않았습니다.", false); return false; }
        if (string.IsNullOrEmpty(currentStockId)) { Feedback("종목을 먼저 선택하세요.", false); return false; }
        if (currentPrice <= 0) { Feedback("유효한 가격이 없습니다.", false); return false; }
        return true;
    }

    void UpdatePlayerCashUI(long cashCents)
    {
        if (playerCash != null) playerCash.text = $"주문 가능 금액: {FormatMoney(cashCents)}";
    }

    void SetTexts(string title, string priceStr, long cashCents, int qty)
    {
        if (cardTitle  != null) cardTitle.text  = title;
        if (orderPrice != null) orderPrice.text = priceStr;
        UpdatePlayerCashUI(cashCents);
        if (orderSummaryQty   != null) orderSummaryQty.text   = $"주문 수량: {qty}주";
        if (orderSummaryTotal != null) orderSummaryTotal.text = $"주문 총액: {FormatMoney(0)}";
    }

    void SetButtonsInteractable(bool on)
    {
        if (buttonBuy  != null) buttonBuy.SetEnabled(on);
        if (buttonSell != null) buttonSell.SetEnabled(on);
    }

    void Feedback(string msg, bool ok) => Debug.Log($"[Order] {(ok ? "OK" : "FAIL")} - {msg}");

    static long ToCents(double price) => (long)Math.Round(price * 100.0);
    static long CalcTotalCents(double price, int qty) => (long)Math.Round(price * qty * 100.0);
    static string FormatPrice(double p) => $"${p:F2}";
    static string FormatMoney(long cents)
    {
        bool neg = cents < 0;
        long abs = Math.Abs(cents);
        long dollars = abs / 100;
        long remain  = abs % 100;
        return (neg ? "-" : "") + $"${dollars:n0}.{remain:00}";
    }
}

public sealed class StockData
{
    public string Id;
    public string Name;
    public string Ticker;
    public double CurrentPrice;
}
