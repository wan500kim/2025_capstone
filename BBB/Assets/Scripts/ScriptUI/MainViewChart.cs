using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

//
// BackgroundSineCandleChart_Reliable.cs (두 번째 요소 삽입 + 고정 픽셀 뷰포트)
// - UIDocument 루트의 "두 번째 자식(index 1)"으로 삽입하여 맨 뒤에 배경 이미지를 둘 수 있음
// - 차트 영역을 1920x1080 기준 픽셀 좌표(top/left/width/height)로 배치
// - 단색 불투명 배경 + 흰 격자 + 좌→우 채움
//
[DefaultExecutionOrder(-500)]
public class BackgroundSineCandleChart : MonoBehaviour
{
    [Header("Attach Target")]
    [Tooltip("부착할 UIDocument. 비워두면 자동으로 탐색합니다.")]
    public UIDocument attachTo;

    [Header("Fallback Panel (attachTo를 못 찾을 때만 사용)")]
    public PanelSettings panelSettings;
    public int sortingOrder = -100;

    [Header("Insert Position")]
    [Tooltip("루트에 삽입할 인덱스. 0=맨앞, 1=두번째. 배경 이미지를 맨뒤(0)로 두고, 본 컴포넌트를 1로 두면 됩니다.")]
    [Min(0)] public int insertIndex = 1;

    [Header("Viewport (base 1920x1080)")]
    [Tooltip("1920x1080 기준 픽셀 좌표")]
    public int viewportLeft   = 540;  // px
    public int viewportTop    = 207;  // px
    public int viewportWidth  = 710;  // px
    public int viewportHeight = 400;  // px

    [Header("Background & Grid")]
    public Color backgroundColor = new Color(0.97f, 0.97f, 0.99f, 1f);
    public Color gridLineColor   = new Color(1f, 1f, 1f, 0.12f);
    public int gridRows = 6;
    public int gridCols = 8;

    [Header("Candle Appearance")]
    public Color upColor   = new Color(0.10f, 0.60f, 1f, 1f);
    public Color downColor = new Color(1f, 0.32f, 0.32f, 1f);
    public Color wickColor = new Color(0.95f, 0.95f, 1f, 0.95f);

    [Header("Layout")]
    [Min(2f)]  public float candleWidth = 12f;
    [Min(0f)]  public float candleGap   = 5f;
    [Range(16, 400)] public int maxVisible = 140;
    [Min(0.01f)] public float yPaddingRatio = 0.05f;

    [Header("Candle Timing")]
    [Min(0.2f)] public float candleSeconds = 3f;
    [Min(0.03f)] public float rebuildInterval = 0.08f;

    [Header("Volatility Controls")]
    public float volatility = 2.0f;
    [Range(0f, 1f)] public float spikeChance = 0.03f;
    [Min(0f)] public float spikeMagnitude = 8f;

    [Header("Sine Price Model (base)")]
    public double startPrice = 300.0;
    public double amplitude = 10.0;
    public double frequency = 0.12;
    public double noiseStd = 0.35;
    public double driftPerSec = 0.02;
    public double phase0 = 0.0;
    public double timeScale = 1.0;

    // ---------- 내부 상태 ----------
    class Candle { public DateTime t0; public double open, high, low, close, volume; }

    GameObject _go;            // fallback용 UIDocument GO
    UIDocument _doc;           // fallback용
    VisualElement _host;       // 실제로 붙일 루트
    VisualElement _root;       // 본 컴포넌트 루트(화면 전체)
    VisualElement _viewport;   // 지정 픽셀 영역(차트 그려질 영역)
    VisualElement _bg, _grid, _candles;

    readonly List<Candle> _list = new();
    Candle _cur;
    System.Random _rng;

    double _phase, _price;
    float  _elapsed, _accumRebuild;

    Coroutine _bindCo;
    bool _bound;

    void OnEnable()
    {
        _rng = new System.Random(7777);
        _phase = phase0; _price = startPrice;
        StartNewCandle(DateTime.UtcNow, _price);

        SceneManager.sceneLoaded += OnSceneLoaded;
        _bindCo = StartCoroutine(BindWhenReady());
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_bindCo != null) StopCoroutine(_bindCo);
        UnmountRoot();
        if (_go != null) DestroyImmediate(_go);
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        _bound = false;
        if (_bindCo != null) StopCoroutine(_bindCo);
        _bindCo = StartCoroutine(BindWhenReady());
    }

    IEnumerator BindWhenReady()
    {
        // 1) attachTo 자동 탐색(최대 2초)
        float t = 0f;
        while (attachTo == null && t < 2f)
        {
            attachTo = FindObjectOfType<UIDocument>();
            if (attachTo != null) break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // 2) 호스트 결정
        if (attachTo != null)
        {
            t = 0f;
            while (attachTo.rootVisualElement == null && t < 2f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            _host = attachTo.rootVisualElement;
        }
        else
        {
            if (_doc == null)
            {
                if (panelSettings == null)
                {
                    Debug.LogError("[BG] attachTo를 찾지 못했고 panelSettings도 없습니다.");
                    yield break;
                }
                _go = new GameObject("BG_SineCandleChart_UI");
                _go.transform.SetParent(transform, false);
                _doc = _go.AddComponent<UIDocument>();
                _doc.panelSettings = panelSettings;
                _doc.sortingOrder  = sortingOrder;
            }
            _host = _doc.rootVisualElement;
        }

        // 3) 루트 구축 및 삽입
        MountRoot();
        _bound = true;
    }

    void MountRoot()
    {
        UnmountRoot();

        // 화면 전체 루트(이 자체는 전체에 깔리지만, 실제 차트는 _viewport 안에만 그림)
        _root = new VisualElement { name = "bg_chart_root", pickingMode = PickingMode.Ignore };
        _root.style.position = Position.Absolute;
        _root.style.left = 0; _root.style.top = 0; _root.style.right = 0; _root.style.bottom = 0;

        // 원하는 인덱스에 삽입
        int idx = Mathf.Clamp(insertIndex, 0, Mathf.Max(0, _host.childCount));
        _host.Insert(idx, _root);

        // 지정 픽셀 뷰포트
        _viewport = new VisualElement { name = "chart_viewport", pickingMode = PickingMode.Ignore };
        _viewport.style.position = Position.Absolute;
        ApplyViewportRect(); // 좌표/크기 적용
        _root.Add(_viewport);

        // 레이어들
        _bg = new VisualElement { name = "solid_bg", pickingMode = PickingMode.Ignore };
        _bg.style.position = Position.Absolute;
        _bg.style.left = 0; _bg.style.top = 0; _bg.style.right = 0; _bg.style.bottom = 0;
        _viewport.Add(_bg);

        _grid = new VisualElement { name = "grid_lines", pickingMode = PickingMode.Ignore };
        _grid.style.position = Position.Absolute;
        _grid.style.left = 0; _grid.style.top = 0; _grid.style.right = 0; _grid.style.bottom = 0;
        _viewport.Add(_grid);

        _candles = new VisualElement { name = "candles_layer", pickingMode = PickingMode.Ignore };
        _candles.style.position = Position.Absolute;
        _candles.style.left = 0; _candles.style.top = 0; _candles.style.right = 0; _candles.style.bottom = 0;
        _viewport.Add(_candles);

        // 첫 빌드
        RebuildBackground();
        RebuildGrid();
        RebuildCandles(full:true);

        // 레이아웃 변동 시: 위치 유지, 재빌드
        _root.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            // 삽입 인덱스 유지
            var p = _root.parent;
            if (p != null)
            {
                int want = Mathf.Clamp(insertIndex, 0, Mathf.Max(0, p.childCount - 1));
                if (p.IndexOf(_root) != want) { p.Remove(_root); p.Insert(want, _root); }
            }
            ApplyViewportRect();
            RebuildBackground();
            RebuildGrid();
            RebuildCandles(full:true);
        });
    }

    void ApplyViewportRect()
    {
        // 1920x1080 기준 절대 픽셀. 해상도 스케일링 없이 고정 배치.
        _viewport.style.left   = viewportLeft;
        _viewport.style.top    = viewportTop;
        _viewport.style.width  = viewportWidth;
        _viewport.style.height = viewportHeight;
    }

    void UnmountRoot()
    {
        if (_root != null)
        {
            if (_root.parent != null) _root.parent.Remove(_root);
            _root = null; _viewport = null; _bg = null; _grid = null; _candles = null;
        }
    }

    void Update()
    {
        if (!_bound || _candles == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // 변동성
        double amp   = amplitude * Math.Max(0.0f, volatility);
        double sigma = noiseStd  * Math.Max(0.0f, volatility);

        // 가격 생성
        _phase += 2.0 * Math.PI * frequency * (double)dt * timeScale;
        double sine  = amp * Math.Sin(_phase);
        double drift = driftPerSec * (double)dt;
        double noise = NextGaussian(_rng) * sigma;

        if (UnityEngine.Random.value < spikeChance * dt)
            noise += NextGaussian(_rng) * sigma * spikeMagnitude;

        _price += sine * (double)dt + drift + noise;

        // 캔들 갱신
        _cur.close = _price;
        if (_price > _cur.high) _cur.high = _price;
        if (_price < _cur.low)  _cur.low  = _price;

        _elapsed += dt;
        _accumRebuild += dt;

        if (_accumRebuild >= rebuildInterval) { _accumRebuild = 0f; RebuildCandles(full:false); }
        if (_elapsed >= candleSeconds) { _elapsed = 0f; CloseAndStartNew(); RebuildCandles(full:true); }
    }

    // ---------- 빌드 ----------
    void RebuildBackground()
    {
        _bg.style.backgroundColor = new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, 1f);
    }

    void RebuildGrid()
    {
        _grid.Clear();
        float w = Mathf.Max(2f, _grid.resolvedStyle.width);
        float h = Mathf.Max(2f, _grid.resolvedStyle.height);
        if (w <= 2 || h <= 2) return;

        for (int r = 0; r <= gridRows; r++)
        {
            float y = Mathf.Round(h * (float)r / Mathf.Max(1, gridRows));
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.style.position = Position.Absolute;
            line.style.left = 0; line.style.right = 0;
            line.style.top = y; line.style.height = 1;
            line.style.backgroundColor = gridLineColor;
            _grid.Add(line);
        }
        for (int c = 0; c <= gridCols; c++)
        {
            float x = Mathf.Round(w * (float)c / Mathf.Max(1, gridCols));
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.style.position = Position.Absolute;
            line.style.top = 0; line.style.bottom = 0;
            line.style.left = x; line.style.width = 1;
            line.style.backgroundColor = gridLineColor;
            _grid.Add(line);
        }
    }

    void RebuildCandles(bool full)
    {
        if (full) _candles.Clear();

        int count = _list.Count + 1;
        int visCount = Mathf.Min(count, maxVisible);
        int first = count - visCount;

        // Y 스케일
        double ymin = double.MaxValue, ymax = double.MinValue;
        for (int i = first; i < _list.Count; i++) { var c = _list[i]; ymin = Math.Min(ymin, c.low); ymax = Math.Max(ymax, c.high); }
        ymin = Math.Min(ymin, _cur.low); ymax = Math.Max(ymax, _cur.high);
        if (ymax <= ymin) ymax = ymin + 1.0;
        double yr = ymax - ymin; ymin -= yr * yPaddingRatio; ymax += yr * yPaddingRatio;

        float W = Mathf.Max(2f, _candles.resolvedStyle.width);
        float H = Mathf.Max(2f, _candles.resolvedStyle.height);

        // 좌→우 채움. 꽉 차면 스크롤
        float slot = candleWidth + candleGap;
        float needed = slot * visCount;
        float x0 = needed <= W ? 0f : W - needed;

        int existing = _candles.childCount;
        int idx = 0;

        for (int i = first; i < _list.Count; i++, idx++)
            DrawOne(idx, _list[i], x0, slot, H, ymin, ymax, isCurrent:false, replace: !full && idx*2+1 < existing);

        DrawOne(idx, _cur, x0, slot, H, ymin, ymax, isCurrent:true, replace: !full && idx*2+1 < existing);
    }

    void DrawOne(int i, Candle c, float x0, float slot, float H, double ymin, double ymax, bool isCurrent, bool replace)
    {
        float X = x0 + i * slot + candleGap * 0.5f;
        Func<double, float> Y = v =>
        {
            double t = (v - ymin) / (ymax - ymin);
            t = 1.0 - Math.Max(0.0, Math.Min(1.0, t));
            return (float)(t * H);
        };

        float yH = Y(c.high), yL = Y(c.low), yO = Y(c.open), yC = Y(c.close);
        bool up = c.close >= c.open;
        Color bodyCol = up ? upColor : downColor;
        Color wickCol = wickColor;

        VisualElement wick, body;
        if (replace)
        {
            wick = _candles[i*2+0];
            body = _candles[i*2+1];
        }
        else
        {
            wick = new VisualElement { pickingMode = PickingMode.Ignore };
            wick.style.position = Position.Absolute;
            body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.style.position = Position.Absolute;
            _candles.Add(wick); _candles.Add(body);
        }

        // Wick
        float cx = Mathf.Round(X + candleWidth * 0.5f);
        wick.style.left = cx; wick.style.width = 1f;
        wick.style.top = Mathf.Min(yH, yL);
        wick.style.height = Mathf.Max(1f, Mathf.Abs(yL - yH));
        wick.style.backgroundColor = wickCol;

        // Body
        float top = Mathf.Min(yO, yC), bot = Mathf.Max(yO, yC);
        body.style.left = Mathf.Round(X);
        body.style.top  = top;
        body.style.width  = Mathf.Max(1f, candleWidth);
        body.style.height = Mathf.Max(1f, bot - top);
        var col = bodyCol; if (isCurrent) col.a *= 0.9f;
        body.style.backgroundColor = col;
        body.style.borderTopLeftRadius = 1; body.style.borderTopRightRadius = 1;
        body.style.borderBottomLeftRadius = 1; body.style.borderBottomRightRadius = 1;
    }

    // ---------- 유틸 ----------
    void StartNewCandle(DateTime t0, double price)
    {
        _cur = new Candle { t0 = t0, open = price, high = price, low = price, close = price, volume = 0 };
    }
    void CloseAndStartNew()
    {
        _list.Add(_cur);
        if (_list.Count > maxVisible) _list.RemoveAt(0);
        StartNewCandle(DateTime.UtcNow, _price);
    }
    static double NextGaussian(System.Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
