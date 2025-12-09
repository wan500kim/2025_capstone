using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

[Serializable]
public class Candle
{
    public string time;
    public double open, high, low, close, volume;
}

public class CandleChart : MonoBehaviour
{
    public UIDocument uiDocument;
    public VisualTreeAsset candleChartUxml;

    [Header("Layout/Style")]
    [SerializeField] float candleWidth = 10f;
    [SerializeField] float candleGap = 4f;
    [SerializeField] int gridRows = 8;
    [SerializeField] int gridCols = 8;
    [SerializeField] int maxVisible = 140;

    [Header("X Padding/Scroll")]
    [SerializeField] float leftPadPx = 8f;
    [SerializeField] int rightPadCandles = 2;

    [Header("Y Range Controls")]
    [SerializeField] float yPaddingRatio = 0.05f;
    [SerializeField] float minYSpan = 10f;

    VisualElement chartContainer, chartRoot, plotArea, volumeArea;
    Label tooltip;
    VisualElement crossV, crossH;
    Label pricePill, timePill;

    readonly List<VisualElement> candleBodies = new();
    readonly List<VisualElement> candleWicks = new();
    readonly List<VisualElement> volBars = new();
    readonly List<VisualElement> hGridLines = new();
    readonly List<VisualElement> vGridLines = new();
    readonly List<Label> yLabels = new();

    List<Candle> data = new();

    int _cachedStart = 0;
    int _cachedVisible = 0;
    float _cachedStep = 0f;

    void Awake()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
    }

    void OnEnable()
    {
        Debug.Log($"[CandleChart] OnEnable 호출 (data.Count={data?.Count ?? 0})");
        var root = uiDocument.rootVisualElement;

        chartContainer = root.Q<VisualElement>("chart_container");
        if (chartContainer == null)
        {
            Debug.LogError("chart_container not found in gameView.uxml");
            return;
        }

        // ★ 기존 차트 UI 완전 제거
        ClearChartUI();

        // 차트 내부 UXML 인스턴스 삽입
        if (candleChartUxml != null)
        {
            var inst = candleChartUxml.CloneTree();
            chartContainer.Add(inst);
        }

        chartRoot = chartContainer.Q<VisualElement>("chart_root");
        plotArea = chartContainer.Q<VisualElement>("plot_area");
        volumeArea = chartContainer.Q<VisualElement>("volume_area");
        tooltip = chartContainer.Q<Label>("tooltip");

        // 십자선/라벨
        crossV = new VisualElement();
        crossV.AddToClassList("cross_v");
        plotArea.Add(crossV);

        crossH = new VisualElement();
        crossH.AddToClassList("cross_h");
        plotArea.Add(crossH);

        pricePill = new Label();
        pricePill.AddToClassList("pill");
        pricePill.AddToClassList("price_pill");
        plotArea.Add(pricePill);

        timePill = new Label();
        timePill.AddToClassList("pill");
        timePill.AddToClassList("time_pill");
        plotArea.Add(timePill);

        plotArea.RegisterCallback<GeometryChangedEvent>(_ => Redraw());
        if (volumeArea != null) volumeArea.RegisterCallback<GeometryChangedEvent>(_ => Redraw());
        plotArea.RegisterCallback<MouseMoveEvent>(OnMouseMove);
        plotArea.RegisterCallback<MouseLeaveEvent>(_ => HideOverlay());

        crossV.pickingMode = PickingMode.Ignore;
        crossH.pickingMode = PickingMode.Ignore;
        pricePill.pickingMode = PickingMode.Ignore;
        timePill.pickingMode = PickingMode.Ignore;
        tooltip.pickingMode = PickingMode.Ignore;
    }

    // ★ 차트 UI 완전 초기화
    void ClearChartUI()
    {
        // 모든 풀 비우기
        foreach (var c in candleBodies) c.RemoveFromHierarchy();
        candleBodies.Clear();

        foreach (var w in candleWicks) w.RemoveFromHierarchy();
        candleWicks.Clear();

        foreach (var v in volBars) v.RemoveFromHierarchy();
        volBars.Clear();

        foreach (var h in hGridLines) h.RemoveFromHierarchy();
        hGridLines.Clear();

        foreach (var v in vGridLines) v.RemoveFromHierarchy();
        vGridLines.Clear();

        foreach (var l in yLabels) l.RemoveFromHierarchy();
        yLabels.Clear();

        // 데이터 초기화
        data.Clear();
        _cachedStart = 0;
        _cachedVisible = 0;
        _cachedStep = 0f;

        // 컨테이너 하위 요소 제거 (기존 차트 UI)
        if (chartContainer != null)
        {
            var childrenToRemove = new List<VisualElement>();
            for (int i = 0; i < chartContainer.childCount; i++)
            {
                childrenToRemove.Add(chartContainer[i]);
            }
            foreach (var child in childrenToRemove)
            {
                child.RemoveFromHierarchy();
            }
        }
    }

    public void SetData(List<Candle> candles)
    {
        data = candles ?? new List<Candle>();
        Redraw();
    }

    public void SetDataFromJson(string jsonArray)
    {
        var wrapped = JsonUtility.FromJson<CandleArray>("{\"items\":" + jsonArray + "}");
        data = new List<Candle>(wrapped.items ?? Array.Empty<Candle>());
        Redraw();
    }

    public void AppendClosedCandle(Candle closed)
    {
        data.Add(closed);
        Redraw();
    }

    public void UpdateRealtimeCandle(Candle partial)
    {
        if (data.Count == 0) data.Add(partial);
        else data[^1] = partial;
        Redraw();
    }

    [Serializable] class CandleArray { public Candle[] items; }

    void Redraw()
    {
        Debug.Log($"[CandleChart] Redraw 호출 (data.Count={data?.Count})");
        if (plotArea == null || data == null || data.Count == 0) return;

        var pRect = plotArea.contentRect;
        var vRect = volumeArea != null ? volumeArea.contentRect : Rect.zero;
        if (pRect.width <= 0 || pRect.height <= 0) return;

        float step = candleWidth + candleGap;
        float rightEmptyPx = Mathf.Max(0, rightPadCandles) * step;
        int fitByWidth = Mathf.Max(1, Mathf.FloorToInt((pRect.width - leftPadPx - rightEmptyPx) / step));
        int visibleCount = Mathf.Min(data.Count, maxVisible, fitByWidth);
        int s = Mathf.Max(0, data.Count - visibleCount);

        double minPrice = double.MaxValue, maxPrice = double.MinValue, maxVol = 0;
        for (int i = s; i < s + visibleCount; i++)
        {
            var c = data[i];
            minPrice = Math.Min(minPrice, c.low);
            maxPrice = Math.Max(maxPrice, c.high);
            maxVol = Math.Max(maxVol, c.volume);
        }
        if (maxPrice <= minPrice) maxPrice = minPrice + 1;

        double span = maxPrice - minPrice;
        if (span < minYSpan)
        {
            double mid = (minPrice + maxPrice) * 0.5;
            minPrice = mid - minYSpan * 0.5;
            maxPrice = mid + minYSpan * 0.5;
            span = maxPrice - minPrice;
        }

        double pad = span * yPaddingRatio;
        minPrice -= pad;
        maxPrice += pad;
        span = maxPrice - minPrice;

        double niceStep = NiceStep(span / Math.Max(1, gridRows - 1));
        minPrice = Math.Floor(minPrice / niceStep) * niceStep;
        maxPrice = Math.Ceiling(maxPrice / niceStep) * niceStep;

        EnsureGrid(plotArea, hGridLines, gridRows, true);
        EnsureGrid(plotArea, vGridLines, gridCols, false);
        PlaceGridLines(pRect, hGridLines, vGridLines);
        PlaceYLabels(pRect, minPrice, maxPrice);

        EnsurePoolSize(candleBodies, visibleCount, "candle_body", plotArea);
        EnsurePoolSize(candleWicks, visibleCount, "candle_wick", plotArea);
        if (volumeArea != null) EnsurePoolSize(volBars, visibleCount, "vol_bar", volumeArea);

        float x = leftPadPx;

        for (int vi = 0; vi < visibleCount; vi++)
        {
            int i = s + vi;
            var c = data[i];
            bool up = c.close >= c.open;

            float yHigh = YMap(c.high, minPrice, maxPrice, pRect.height);
            float yLow = YMap(c.low, minPrice, maxPrice, pRect.height);
            float yOpen = YMap(c.open, minPrice, maxPrice, pRect.height);
            float yClose = YMap(c.close, minPrice, maxPrice, pRect.height);

            var wick = candleWicks[vi];
            wick.EnableInClassList("up", up);
            wick.EnableInClassList("down", !up);
            wick.style.left = x + candleWidth * 0.5f - 1f;
            wick.style.top = yHigh + 8f;
            wick.style.height = Mathf.Max(1f, yLow - yHigh);

            var body = candleBodies[vi];
            body.EnableInClassList("up", up);
            body.EnableInClassList("down", !up);
            float top = Mathf.Min(yOpen, yClose);
            float h = Mathf.Max(1f, Mathf.Abs(yClose - yOpen));
            body.style.left = x;
            body.style.top = top + 8f;
            body.style.width = candleWidth;
            body.style.height = h;

            if (volumeArea != null && maxVol > 0)
            {
                var vb = volBars[vi];
                float volW = Mathf.Clamp(candleWidth - 2f, 2f, 8f);
                vb.EnableInClassList("up", up);
                vb.EnableInClassList("down", !up);
                float vh = (float)(c.volume / maxVol) * (vRect.height - 8f);
                vb.style.left = x + (candleWidth - volW) * 0.5f;
                vb.style.bottom = 4f;
                vb.style.height = Mathf.Max(1f, vh);
                vb.style.width = volW;
            }

            x += step;
        }

        _cachedStart = s;
        _cachedVisible = visibleCount;
        _cachedStep = step;
    }

    float YMap(double price, double min, double max, float height)
    {
        float t = (float)((price - min) / (max - min));
        return height - 8f - t * (height - 8f - 8f);
    }

    void EnsurePoolSize(List<VisualElement> pool, int count, string className, VisualElement parent)
    {
        while (pool.Count < count)
        {
            var ve = new VisualElement();
            ve.AddToClassList(className);
            ve.pickingMode = PickingMode.Ignore;
            parent.Add(ve);
            pool.Add(ve);
        }
        for (int i = 0; i < pool.Count; i++)
            pool[i].style.display = (i < count) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void EnsureGrid(VisualElement parent, List<VisualElement> pool, int count, bool horizontal)
    {
        while (pool.Count < count)
        {
            var g = new VisualElement();
            g.AddToClassList(horizontal ? "grid_h" : "grid_v");
            g.pickingMode = PickingMode.Ignore;
            parent.Add(g);
            pool.Add(g);
        }
        for (int i = 0; i < pool.Count; i++)
            pool[i].style.display = (i < count) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void PlaceGridLines(Rect rect, List<VisualElement> hLines, List<VisualElement> vLines)
    {
        for (int r = 0; r < hLines.Count; r++)
        {
            float t = (hLines.Count == 1) ? 0 : r / (float)(hLines.Count - 1);
            float y = Mathf.Lerp(8f, rect.height - 8f, t);
            var gl = hLines[r];
            gl.style.left = 0;
            gl.style.top = y;
            gl.style.height = 1;
            gl.style.width = rect.width;
        }
        for (int c = 0; c < vLines.Count; c++)
        {
            float t = (vLines.Count == 1) ? 0 : c / (float)(vLines.Count - 1);
            float x = Mathf.Lerp(0, rect.width, t);
            var gl = vLines[c];
            gl.style.left = x;
            gl.style.top = 8f;
            gl.style.width = 1;
            gl.style.height = rect.height - 16f;
        }
    }

    void PlaceYLabels(Rect rect, double min, double max)
    {
        foreach (var l in yLabels) l.RemoveFromHierarchy();
        yLabels.Clear();

        for (int i = 0; i < gridRows; i++)
        {
            float t = (gridRows == 1) ? 0 : i / (float)(gridRows - 1);
            double p = min + (max - min) * (1 - t);
            float y = Mathf.Lerp(8f, rect.height - 8f, t);

            var lab = new Label(p.ToString("F2", CultureInfo.InvariantCulture));
            lab.AddToClassList("axis_label");
            lab.AddToClassList("axis_label--right");
            lab.style.top = y - 8;
            plotArea.Add(lab);
            yLabels.Add(lab);
        }
    }

    void OnMouseMove(MouseMoveEvent e)
    {
        if (data == null || data.Count == 0 || _cachedVisible <= 0) return;

        var rect = plotArea.contentRect;
        Vector2 local = e.localMousePosition;

        float xLocal = Mathf.Max(0f, local.x - leftPadPx);
        int idx = Mathf.Clamp(Mathf.RoundToInt(xLocal / _cachedStep), 0, _cachedVisible - 1);
        int dataIndex = _cachedStart + idx;
        var c = data[dataIndex];

        crossV.style.display = DisplayStyle.Flex;
        crossH.style.display = DisplayStyle.Flex;
        crossV.style.left = leftPadPx + idx * _cachedStep + candleWidth * .5f;
        crossH.style.top = Mathf.Clamp(local.y, 0, rect.height);

        pricePill.text = c.close.ToString("F2");
        timePill.text = c.time;
        pricePill.style.top = Mathf.Clamp(local.y - 10, 0, rect.height - 18);
        timePill.style.left = Mathf.Clamp(leftPadPx + idx * _cachedStep + candleWidth * .5f - 20, 0, rect.width - 40);
        pricePill.AddToClassList("show");
        timePill.AddToClassList("show");

        tooltip.text = $"{c.time}\nO:{c.open:F2}  H:{c.high:F2}\nL:{c.low:F2}  C:{c.close:F2}";
        tooltip.style.left = Mathf.Clamp(local.x + 12, 8, rect.width - 140);
        tooltip.style.top = Mathf.Clamp(local.y + 12, 8, rect.height - 60);
        tooltip.AddToClassList("show");
    }

    void HideOverlay()
    {
        crossV.style.display = DisplayStyle.None;
        crossH.style.display = DisplayStyle.None;
        pricePill.RemoveFromClassList("show");
        timePill.RemoveFromClassList("show");
        tooltip.RemoveFromClassList("show");
    }

    double NiceStep(double raw)
    {
        double exp = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double f = raw / exp;
        double nf = (f < 1.5) ? 1 :
                    (f < 2.5) ? 2 :
                    (f < 3.5) ? 2.5 :
                    (f < 7.5) ? 5 : 10;
        return nf * exp;
    }
}