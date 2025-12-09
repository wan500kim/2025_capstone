using System;
using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit 대시보드 바인더
/// - 실시간 추정자산(EstimatedEquityBus) 사용
/// - 전일 대비 증감 표시
/// - [추가] 플레이어가 선택한 아이템 아이콘 3칸 표시
/// </summary>
[RequireComponent(typeof(UIDocument))]
public sealed class PlayerDashboard : MonoBehaviour
{
    public UIDocument uiDocument;

    Label lblDate;
    Label lblTerm;
    VisualElement veImage;
    Label lblName;
    Label lblAsset;         // player_asset_value
    Label lblDiffLabel;     // player_asset_diff_label
    Label lblDiffValue;     // player_asset_diff_value

    // [추가] 아이템 아이콘 슬롯
    VisualElement veItemRoot;
    VisualElement veItem1;
    VisualElement veItem2;
    VisualElement veItem3;
    int nextItemSlot = 1;

    PlayerState ps;

    long?    yesterdayEquityCents;
    DateTime? yesterdayDate;

    const int BASE_YEAR = 2026;

    void Reset() => uiDocument = GetComponent<UIDocument>();

    void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        BindElements();

        TimeManager.OnClientNewDay += OnClientNewDay;
        EstimatedEquityBus.OnUpdated += OnEstimatedUpdated;

        StartCoroutine(WaitAndBindLocalPlayer());

        SetDateTerm(null);
        SetPlayerName("Player");
        SetAsset(0);
        SetDiff(null, 0, 0f);

        // [추가] 아이템 슬롯 초기화
        ResetItemIcons();
    }

    void OnDisable()
    {
        TimeManager.OnClientNewDay -= OnClientNewDay;
        EstimatedEquityBus.OnUpdated -= OnEstimatedUpdated;
        UnhookPlayerEvents();
    }

    // 이름(name) 우선, 실패 시 클래스(class)로 조회
    static T QByNameOrClass<T>(VisualElement root, string nameOrClass) where T : VisualElement
    {
        if (root == null || string.IsNullOrEmpty(nameOrClass)) return null;
        var byName = root.Q<T>(name: nameOrClass);
        if (byName != null) return byName;
        return root.Q<T>(className: nameOrClass);
    }

    void BindElements()
    {
        var root = uiDocument.rootVisualElement;

        lblDate = QByNameOrClass<Label>(root, "dashboard_date");
        lblTerm = QByNameOrClass<Label>(root, "dashboard_term");

        var lowRow  = QByNameOrClass<VisualElement>(root, "dashboard_low_row");
        var vePlayer = QByNameOrClass<VisualElement>(lowRow, "player_info");

        veImage = QByNameOrClass<VisualElement>(vePlayer, "player_image");
        lblName = QByNameOrClass<Label>(vePlayer, "player_name");

        var veAsset = QByNameOrClass<VisualElement>(lowRow, "player_asset");
        lblAsset = QByNameOrClass<Label>(veAsset, "player_asset_value");

        var veDiff = QByNameOrClass<VisualElement>(lowRow, "player_asset_diff");
        lblDiffLabel = QByNameOrClass<Label>(veDiff, "player_asset_diff_label");
        lblDiffValue = QByNameOrClass<Label>(veDiff, "player_asset_diff_value");

        // [추가] 아이템 슬롯 바인딩
        veItemRoot = QByNameOrClass<VisualElement>(root, "player_item");
        veItem1 = QByNameOrClass<VisualElement>(veItemRoot, "player_item_1");
        veItem2 = QByNameOrClass<VisualElement>(veItemRoot, "player_item_2");
        veItem3 = QByNameOrClass<VisualElement>(veItemRoot, "player_item_3");
    }

    IEnumerator WaitAndBindLocalPlayer()
    {
        while (ps == null)
        {
            var lp = NetworkClient.active ? NetworkClient.localPlayer : null;
            if (lp != null) ps = lp.GetComponent<PlayerState>();
            if (ps != null) break;
            yield return null;
        }

        HookPlayerEvents();

        SetPlayerName(string.IsNullOrEmpty(ps.playerName) ? "Player" : ps.playerName);

        // ★ UPDATED: 동물 키 우선 반영
        ApplyPlayerImageByAnimal(ps.avatarAnimal);
        if (string.IsNullOrEmpty(ps.avatarAnimal))
            ApplyPlayerImage(ps.playerImageIndex);

        // 초기 추정자산 반영
        OnEstimatedUpdated(EstimatedEquityBus.LatestCents);
    }

    void HookPlayerEvents()
    {
        if (ps == null) return;
        ps.OnImageIndexChanged += OnImageIndexChanged;
        ps.OnAvatarAnimalChangedEvent += OnAvatarAnimalChangedEvent;
    }

    void UnhookPlayerEvents()
    {
        if (ps == null) return;
        ps.OnImageIndexChanged -= OnImageIndexChanged;
        ps.OnAvatarAnimalChangedEvent -= OnAvatarAnimalChangedEvent;
    }

    // 일자 변경 시 전일 대비 계산
    void OnClientNewDay(DateTime date, int round, int dayIdx)
    {
        long cur = EstimatedEquityBus.LatestCents;

        if (yesterdayEquityCents.HasValue && yesterdayDate.HasValue)
        {
            long prev = yesterdayEquityCents.Value;
            long diff = cur - prev;
            float pct = prev > 0 ? (diff / (float)prev) * 100f : 0f;
            SetDiff(yesterdayDate, diff, pct);
            SetAsset(cur);
        }
        else
        {
            SetDiff(date.AddDays(-1), 0, 0f);
            SetAsset(cur);
        }

        SetDateTerm(date);
        yesterdayEquityCents = cur;
        yesterdayDate = date;
    }

    // 실시간 추정자산 갱신
    void OnEstimatedUpdated(long cents)
    {
        SetAsset(cents);
        // 당일 중에는 diff는 고정, 일자 바뀔 때 재계산
    }

    void OnImageIndexChanged(int oldIdx, int newIdx)
    {
        if (!string.IsNullOrEmpty(ps.avatarAnimal)) return; // 동물 키가 있으면 인덱스 무시
        ApplyPlayerImage(newIdx);
    }

    void OnAvatarAnimalChangedEvent(string oldKey, string newKey)
    {
        ApplyPlayerImageByAnimal(newKey);
    }

    // UI 유틸
    void SetDateTerm(DateTime? date)
    {
        if (lblDate == null || lblTerm == null) return;
        if (date.HasValue)
        {
            var d = date.Value;
            lblDate.text = $"{d:yyyy}. {d:MM}. {d:dd}.";
            int yearDiff = Mathf.Max(0, d.Year - BASE_YEAR);
            int quarter = ((d.Month - 1) / 3) + 1;
            lblTerm.text = $"({yearDiff}년차 {quarter}분기)";
        }
        else
        {
            lblDate.text = "—";
            lblTerm.text = "(—)";
        }
    }

    void SetPlayerName(string name)
    {
        if (lblName == null) return;
        lblName.text = string.IsNullOrEmpty(name) ? "Player" : name;
    }

    void SetAsset(long cents)
    {
        if (lblAsset == null) return;
        lblAsset.text = FormatMoney(cents);
    }

    void SetDiff(DateTime? prevDate, long diffCents, float pct)
    {
        if (lblDiffLabel == null || lblDiffValue == null) return;

        if (prevDate.HasValue)
        {
            var pd = prevDate.Value;
            lblDiffLabel.text = $"{pd.Month}월 {pd.Day}일보다 ";
        }
        else
        {
            lblDiffLabel.text = "전일 대비 ";
        }

        string sign = diffCents >= 0 ? "+" : "";
        lblDiffValue.text = $"{sign}{FormatMoney(diffCents)}({pct:+0.0;-0.0;0.0}%)";

        lblDiffValue.RemoveFromClassList("diff_value_up");
        lblDiffValue.RemoveFromClassList("diff_value_down");
        if (diffCents > 0) lblDiffValue.AddToClassList("diff_value_up");
        else if (diffCents < 0) lblDiffValue.AddToClassList("diff_value_down");
    }

    // ★ UPDATED: 동물 키 우선 적용
    void ApplyPlayerImageByAnimal(string animalKey)
    {
        if (veImage == null) return;

        Texture2D tex = null;
        if (!string.IsNullOrWhiteSpace(animalKey))
        {
            tex = Resources.Load<Texture2D>($"robby_image/player_{animalKey}");
        }

        if (tex != null)
        {
            veImage.style.backgroundImage = new StyleBackground(tex);
            veImage.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
        }
        else
        {
            // 키가 유효하지 않으면 인덱스 폴백 유지
            ApplyPlayerImage(ps != null ? ps.playerImageIndex : 0);
        }
    }

    void ApplyPlayerImage(int index)
    {
        if (veImage == null) return;
        if (!string.IsNullOrEmpty(ps?.avatarAnimal))
        {
            // 동물 키가 있으면 이 함수에서는 아무 것도 하지 않음
            return;
        }

        // 인덱스 보정
        int i = Mathf.Max(0, index);

        // 0/1 기반 리소스 네이밍 모두 시도
        // 예) "Player_img/player_img{i}", "image_{i}"
        Texture2D tex = TryLoadTexture(
            $"Player_img/player_img{i}",
            $"Player_img/player_img{i + 1}",
            $"image_{i}",
            $"image_{i + 1}"
        );

        if (tex != null)
        {
            veImage.style.backgroundImage = new StyleBackground(tex);
            veImage.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
        }
        else
        {
            veImage.style.backgroundImage = null;
        }
    }

    static Texture2D TryLoadTexture(params string[] paths)
    {
        foreach (var p in paths)
        {
            if (string.IsNullOrEmpty(p)) continue;
            var t = Resources.Load<Texture2D>(p);
            if (t != null) return t;
        }
        return null;
    }

    static string FormatMoney(long cents)
    {
        bool neg = cents < 0;
        long abs = Math.Abs(cents);
        long dollars = abs / 100;
        long remain = abs % 100;
        return (neg ? "-" : "") + $"${dollars:n0}.{remain:00}";
    }

    /* ================== 아이템 아이콘 표시 기능 ================== */

    /// <summary>
    /// 외부에서 호출. 선택된 아이템의 iconPath(Resources 기준)를 받아 비어있는 슬롯에 순서대로 표시.
    /// 예) "item/9"
    /// </summary>
    public void AddItemIconToDashboard(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return;

        // 슬롯 결정
        VisualElement slot = null;
        if      (nextItemSlot == 1) slot = veItem1;
        else if (nextItemSlot == 2) slot = veItem2;
        else if (nextItemSlot == 3) slot = veItem3;

        if (slot == null) return;

        var tex = Resources.Load<Texture2D>(iconPath);
        if (tex == null) return;

        slot.style.backgroundImage = new StyleBackground(tex);
        slot.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;

        // 클래스 토글: 빈 칸 -> 채움
        slot.RemoveFromClassList("player_item_empty");
        slot.AddToClassList("player_item_filled");

        // 다음 슬롯 인덱스 증가(3개까지만)
        nextItemSlot = Mathf.Clamp(nextItemSlot + 1, 1, 3);
    }

    /// <summary>
    /// 라운드 시작 등 필요 시 호출 가능. 세 슬롯 초기화.
    /// </summary>
    public void ResetItemIcons()
    {
        nextItemSlot = 1;
        ResetSlot(veItem1);
        ResetSlot(veItem2);
        ResetSlot(veItem3);
    }

    void ResetSlot(VisualElement ve)
    {
        if (ve == null) return;
        ve.style.backgroundImage = null;
        ve.RemoveFromClassList("player_item_filled");
        ve.AddToClassList("player_item_empty");
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying) return;
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (uiDocument != null) BindElements();
    }
#endif
}
