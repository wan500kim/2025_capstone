using System;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;
using NativeWebSocket;
using Newtonsoft.Json.Linq;
using Mirror;

public class WebSocketHub : NetworkBehaviour
{
    public static WebSocketHub I { get; private set; }

    [Header("WebSocket")]
    [SerializeField] string url = "ws://3.37.36.81:8000";

    WebSocket ws;
    readonly ConcurrentQueue<string> inbox = new();
    volatile bool shuttingDown;
    volatile bool newsActive;            // 라운드 중에만 true

    // 클라 구독 이벤트
    public event Action<NewsItem> OnNews;
    public event Action<StockTick> OnTick;

    // 서버 내부 브로드캐스트(선택)
    public static event Action<NewsItem> OnServerNews;
    public static event Action<StockTick> OnServerTick;

    // ===== DTO =====
    [Serializable] public class IndustryImpact { public string industry_name, impact_direction; public double impact_score; }
    [Serializable] public class NewsItem {
    public string _id, id, industry_name, company_id, company_name, content, timestamp, title, topic;
    public string sentiment;  // origin_sentiment 값을 여기에 저장
    public IndustryImpact industry_impact;
}
    [Serializable] public class StockTick { public string ticker; public double price; public double change; public string ts; public string topic; }
    // ===============

    void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
    }

    public override async void OnStartServer()
    {
        base.OnStartServer();
        await StartWsServerAsync();

        TimeManager.OnServerPhaseChanged += OnServerPhaseChanged;
        TryApplyPhase(TimeManager.Phase, TimeManager.Round);
    }

    public override async void OnStopServer()
    {
        TimeManager.OnServerPhaseChanged -= OnServerPhaseChanged;
        await StopAsync();
        base.OnStopServer();
    }

    async Task StartWsServerAsync()
    {
        if (!isServer) return;

        ws = new WebSocket(url);
        ws.OnOpen += () =>
        {
            if (shuttingDown) return;
            Debug.Log("[WS][Server] connected");
            TryApplyPhase(TimeManager.Phase, TimeManager.Round);
        };
        ws.OnMessage += (bytes) =>
        {
            if (shuttingDown) return;
            inbox.Enqueue(Encoding.UTF8.GetString(bytes));
        };
        ws.OnError += (e) => { if (!shuttingDown) Debug.LogError($"[WS][Server] {e}"); };
        ws.OnClose += (code) => { if (!shuttingDown) Debug.Log($"[WS][Server] closed: {code}"); };

        try { await ws.Connect(); }
        catch (Exception e) { Debug.LogError($"[WS][Server] connect failed: {e.Message}"); }
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        if (isServer) ws?.DispatchMessageQueue();
#endif
        if (!isServer || shuttingDown) return;

        while (inbox.TryDequeue(out var json))
            ServerDispatchAndBroadcast(json);
    }

    void ServerDispatchAndBroadcast(string json)
    {
        try
        {
            var jo = JObject.Parse(json);
            var topic = jo.Value<string>("topic");

            // 라운드 외에는 뉴스/틱을 모두 드랍
            if (!newsActive)
            {
                return;
            }

            if (topic == "news" || jo.ContainsKey("content") || jo.ContainsKey("title"))
            {
                var n = jo.ToObject<NewsItem>();
                
                // origin_sentiment를 sentiment 필드에 저장
                if (string.IsNullOrEmpty(n.sentiment))
                {
                    n.sentiment = jo.Value<string>("origin_sentiment") ?? "neutral";
                }
                
                OnServerNews?.Invoke(n);
                RpcNews(json);
            }
            else if (topic == "tick" || jo.ContainsKey("price"))
            {
                var t = jo.ToObject<StockTick>();
                OnServerTick?.Invoke(t);
                RpcTick(json);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WS][Server] dispatch error: {e}\n{json}");
        }
    }


    [ClientRpc(channel = Channels.Reliable)]
    void RpcNews(string json) { SafeDispatchClient(json); }

    [ClientRpc(channel = Channels.Reliable)]
    void RpcTick(string json) { SafeDispatchClient(json); }

    void SafeDispatchClient(string json)
    {
        if (!isClient) return;
        try
        {
            var jo = JObject.Parse(json);
            var topic = jo.Value<string>("topic");

            if (topic == "news" || jo.ContainsKey("content") || jo.ContainsKey("title"))
            {
                var n = jo.ToObject<NewsItem>();
                
                // origin_sentiment를 sentiment 필드에 저장
                if (string.IsNullOrEmpty(n.sentiment))
                {
                    n.sentiment = jo.Value<string>("origin_sentiment") ?? "neutral";
                }
                
                OnNews?.Invoke(n);
            }
            else if (topic == "tick" || jo.ContainsKey("price"))
                OnTick?.Invoke(jo.ToObject<StockTick>());
        }
        catch (Exception e) { Debug.LogError($"[WS][Client] parse error: {e}\n{json}"); }
    }

    // ===== TimeManager 연동 =====
    void OnServerPhaseChanged(GamePhase phase, int round) => TryApplyPhase(phase, round);

    void TryApplyPhase(GamePhase phase, int round)
    {
        if (!isServer) return;

        if (ws == null || ws.State != WebSocketState.Open)
        {
            newsActive = false;
            return;
        }

        if (phase == GamePhase.Round)
        {
            float spd = TimeManager.Instance != null ? TimeManager.Instance.syncedSecondsPerDay : 3f;
            int intervalSec = Mathf.Max(1, Mathf.RoundToInt(spd * 2f)); // 이틀마다
            newsActive = true;
            _ = SendWsTextSafe($"start,{intervalSec}");
            Debug.Log($"[WS][Server] start,{intervalSec} (Round {round})");
        }
        else
        {
            newsActive = false;
            _ = SendWsTextSafe("stop");
            // 큐에 남은 메시지 제거
            while (inbox.TryDequeue(out _)) { }
            Debug.Log($"[WS][Server] stop (Phase {phase})");
        }
    }

    async Task SendWsTextSafe(string msg)
    {
        try
        {
            if (ws != null && ws.State == WebSocketState.Open)
                await ws.SendText(msg);
        }
        catch (Exception e) { Debug.LogError($"[WS] send failed: {e.Message}"); }
    }

    public async Task StopAsync()
    {
        if (!isServer || shuttingDown) return;
        shuttingDown = true;

        try { if (ws != null && ws.State == WebSocketState.Open) await ws.SendText("stop"); } catch { }
        try { if (ws != null) await ws.Close(); } catch { }
        while (inbox.TryDequeue(out _)) { }
    }
}
