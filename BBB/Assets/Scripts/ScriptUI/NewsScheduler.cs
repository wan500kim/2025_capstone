using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Mirror;

/// <summary>
/// WebSocketHub.OnNews 수신 → 리스트 렌더.
/// - 라운드 외에는 수신 자체를 무시한다(2차 안전장치).
/// - title 사용, news_date는 TimeManager.CurrentDate.
/// </summary>
public class NewsScheduler : MonoBehaviour
{
    [Header("UI")]
    public UIDocument ui;

    ScrollView list;
    bool subscribed;

    void OnEnable()
    {
        // 서버 전용 모드에서는 UI 비활성
        if (NetworkServer.active && !NetworkClient.active) { enabled = false; return; }

        if (ui == null) ui = GetComponent<UIDocument>();
        var root = ui != null ? ui.rootVisualElement : null;
        list = root != null ? root.Q<ScrollView>("news_list") : null;
        if (list == null) { Debug.LogError("[NewsScheduler] ScrollView 'news_list' 없음"); enabled = false; return; }

        _ = WaitAndSubscribe();
    }

    void OnDisable()
    {
        if (subscribed && WebSocketHub.I != null)
            WebSocketHub.I.OnNews -= OnNews;
        subscribed = false;
    }

    async Task WaitAndSubscribe()
    {
        float timeout = 5f;
        while (WebSocketHub.I == null && timeout > 0f)
        {
            await Task.Yield();
            timeout -= Time.unscaledDeltaTime;
        }
        if (WebSocketHub.I == null) { Debug.LogError("[NewsScheduler] WebSocketHub 미존재"); enabled = false; return; }

        WebSocketHub.I.OnNews += OnNews;
        subscribed = true;
    }

    void OnNews(WebSocketHub.NewsItem n)
    {
        // 라운드 외에는 렌더링하지 않음
        if (TimeManager.Phase != GamePhase.Round) return;
        if (!isActiveAndEnabled || list == null || n == null) return;

        string title = !string.IsNullOrEmpty(n.title) ? n.title :
                       (!string.IsNullOrEmpty(n.company_name) ? n.company_name : "뉴스");

        // 본문 비어있으면 라벨 생략
        bool hasBody = !string.IsNullOrWhiteSpace(n.content);

        string dateStr = TimeManager.CurrentDate.HasValue
            ? TimeManager.CurrentDate.Value.ToString("yyyy.MM.dd.")
            : DateTime.UtcNow.ToString("yyyy.MM.dd.");

        var ve  = new VisualElement(); ve.AddToClassList("news_item");
        var t   = new Label(title);   t.AddToClassList("news_title");
        var d   = new Label(dateStr); d.AddToClassList("news_date");
        ve.Add(t);
        ve.Add(d);

        if (hasBody)
        {
            var b = new Label(n.content);
            b.AddToClassList("news_body");
            b.AddToClassList("font-regular");
            ve.Add(b);
            var div = new VisualElement();
            div.AddToClassList("divider");
            ve.Add(div);
        }

        list.Insert(0, ve);
        GameAudio.Play("news_popup");
        _ = AnimateBg(ve, new Color(1f, .95f, .6f, 1f), 0.8f);
    }

    async Task AnimateBg(VisualElement ve, Color highlight, float dur)
    {
        float t = 0f;
        ve.style.backgroundColor = highlight;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            var u = Mathf.Clamp01(t / dur);
            u = (u < 0.5f) ? 2f * u * u : 1f - Mathf.Pow(-2f * u + 2f, 2f) / 2f;

            var c = highlight; c.a = Mathf.Lerp(1f, 0f, u);
            ve.style.backgroundColor = new StyleColor(c);
            await Task.Yield();
        }
        ve.style.backgroundColor = StyleKeyword.Null;
    }
}
