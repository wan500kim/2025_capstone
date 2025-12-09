using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(10001)] // 대시보드 다음에 실행
public sealed class ResultModalBinder : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Display")]
    [Tooltip("통화 기호. 예: $, ₩")]
    [SerializeField] private string currencySymbol = "$";
    [Tooltip("소수점 두 자리 표기 여부")]
    [SerializeField] private bool showCents = true;

    [Tooltip("UI 갱신 주기(초). 결과 라운드 중 내용 보정용")]
    [SerializeField] private float refreshInterval = 0.3f;

    [Header("Animation Classes")]
    [Tooltip("HP가 감소한 플레이어에 부여할 클래스명(USS에 선택적으로 정의)")]
    [SerializeField] private string lifeDamageClass = "life_damage";
    [Tooltip("순위 정렬 시 카드 이동 전환용 클래스명(USS에 선택적으로 정의)")]
    [SerializeField] private string rankTransitionClass = "rank_transition";

    // ===== 내부 구조 =====
    private struct Slot
    {
        public VisualElement root;
        public VisualElement img;
        public Label name;
        public Label value;
        public Label life;
        public Label result; // success/fail 표시
        public int teamIndex; // 1..4
    }

    private VisualElement _root;
    private VisualElement _modalRoot;   // name="result_modal"
    private VisualElement _modalBody;   // name="modal_body"

    private readonly List<Slot> _slots = new List<Slot>(4);
    private readonly List<PlayerState> _players = new List<PlayerState>(8);

    private bool _panelReady;
    private bool _uiBound;
    private bool _clientReady;

    // 순위 고정 및 정렬
    private readonly List<PlayerState> _order = new List<PlayerState>(8);

    // HP 변화 감지
    private readonly Dictionary<uint, int> _lastHpByNetId = new Dictionary<uint, int>(); // netId -> hp

    // 현재 모달 표시 상태
    private bool _modalShown = false;

    void Awake()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
    }

    void OnEnable()
    {
        // 서버 전용(headless)에서 비활성
        if (Application.isBatchMode || (NetworkServer.active && !NetworkClient.active))
        {
            enabled = false;
            return;
        }

        TrySubscribeTimeEvents();
        StartCoroutine(Bootstrap());
        StartCoroutine(RefreshLoop());
    }

    void OnDisable()
    {
        TryUnsubscribeTimeEvents();
        StopAllCoroutines();
    }

    IEnumerator Bootstrap()
    {
        yield return StartCoroutine(WaitForClient(10f));
        yield return StartCoroutine(WaitForPanel(10f));
        BindUI();
        CachePlayersSnapshot();
        ComputeAndFreezeOrder(); // 최초 1회
        SafeRefresh(); // 초기 바인딩
        HideModalImmediate(); // 기본은 감춤
    }

    IEnumerator WaitForClient(float timeout)
    {
        float t = 0f;
        while (!NetworkClient.active || !NetworkClient.isConnected)
        {
            if (t > timeout) break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        _clientReady = NetworkClient.active && NetworkClient.isConnected;
    }

    IEnumerator WaitForPanel(float timeout)
    {
        float t = 0f;
        while (true)
        {
            if (uiDocument != null && uiDocument.rootVisualElement != null && uiDocument.rootVisualElement.panel != null)
                break;

            if (t > timeout) break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        _panelReady = uiDocument != null && uiDocument.rootVisualElement != null && uiDocument.rootVisualElement.panel != null;
        if (!_panelReady) Debug.LogError("[ResultModalBinder] UI Panel 준비 실패");
    }

    void TrySubscribeTimeEvents()
    {
        try
        {
            TimeManager.OnClientPhaseChanged += OnPhaseChanged;
            TimeManager.OnClientNewDay += OnNewDay;
        }
        catch { /* 이벤트 없는 프로젝트 호환 */ }
    }

    void TryUnsubscribeTimeEvents()
    {
        try
        {
            TimeManager.OnClientPhaseChanged -= OnPhaseChanged;
            TimeManager.OnClientNewDay -= OnNewDay;
        }
        catch { }
    }

    void BindUI()
    {
        _root = uiDocument != null ? uiDocument.rootVisualElement : null;
        if (_root == null) { Debug.LogError("[ResultModalBinder] rootVisualElement null"); return; }

        _modalRoot = _root.Q<VisualElement>("result_modal");
        if (_modalRoot == null) { Debug.LogError("[ResultModalBinder] 'result_modal' 미발견"); return; }

        _modalBody = _modalRoot.Q<VisualElement>("modal_body");
        if (_modalBody == null) { Debug.LogError("[ResultModalBinder] 'modal_body' 미발견"); return; }

        _slots.Clear();
        // team_1..team_4 루트는 class로만 지정되어 있음. name 미사용이므로 class 포함 탐색.
        for (int i = 1; i <= 4; i++)
        {
            var teamRoot = QueryTeamRoot(_modalBody, i);
            if (teamRoot == null)
            {
                Debug.LogWarning($"[ResultModalBinder] team_{i} 루트 미발견");
                continue;
            }

            var img    = teamRoot.Q<VisualElement>($"team_{i}_img");
            var name   = teamRoot.Q<Label>($"team_{i}_name");
            var value  = teamRoot.Q<Label>($"team_{i}_value");
            var life   = teamRoot.Q<Label>($"team_{i}_life_num");
            var result = teamRoot.Q<Label>($"team_{i}_result"); // success/fail 텍스트 라벨

            if (name == null || value == null || life == null || result == null)
                Debug.LogWarning($"[ResultModalBinder] team_{i} 내부 name 누락: name/value/life_num/result 확인");

            _slots.Add(new Slot {
                root = teamRoot, img = img, name = name, value = value, life = life, result = result, teamIndex = i
            });
        }

        _uiBound = true;

    }

    VisualElement QueryTeamRoot(VisualElement scope, int idx)
    {
        // class 포함 매칭
        return scope.Query<VisualElement>().Where(ve => ve != null && ve.ClassListContains($"team_{idx}")).First();
    }

    void OnPhaseChanged(GamePhase phase, int round)
    {
        if (!_panelReady || !_uiBound || !_clientReady) return;

        if (phase == GamePhase.Result)
        {
            CachePlayersSnapshot();          // HP 변동 비교용
            ComputeAndFreezeOrder();         // 결과 기준 정렬
            PopulateModal();                 // 데이터 반영
            ShowModal();                     // 표시
            TriggerHpDamageAnimations();     // HP 감소 애니메이션
            ReorderCardsByRankWithAnimation(); // 순위에 따른 재배치
        }
        else
        {
            // 결과 라운드 이탈 시 모달 닫기
            if (_modalShown) HideModal();
        }
    }

    void OnNewDay(DateTime date, int round, int dayIndex)
    {
        // 결과 라운드 중 정보 보정
        if (TimeManager.Instance != null && TimeManager.Instance.currentPhase == GamePhase.Result && _modalShown)
        {
            PopulateModal();
        }
        // HP 스냅샷 갱신
        CachePlayersSnapshot();
    }

    IEnumerator RefreshLoop()
    {
        var wait = new WaitForSeconds(refreshInterval);
        while (true)
        {
            if (_modalShown && TimeManager.Instance != null && TimeManager.Instance.currentPhase == GamePhase.Result)
            {
                SafeRefresh();
            }
            yield return wait;
        }
    }

    void SafeRefresh()
    {
        try { PopulateModal(); }
        catch (Exception ex) { Debug.LogWarning($"[ResultModalBinder] Refresh 예외: {ex.Message}"); }
    }

    void PopulateModal()
    {
        if (!_panelReady || !_uiBound || !_clientReady) return;
        if (_slots.Count == 0) return;

        _players.Clear();
        _players.AddRange(FindObjectsOfType<PlayerState>().Where(p => p != null));

        long targetCents = GetCurrentTargetCents();

        // 현재 정렬 순서대로 살아있는 플레이어 구성
        var alive = _players.Where(p => !p.isEliminated && p.hp > 0).ToList();
        var ordered = new List<PlayerState>();
        foreach (var p in _order) if (p != null && alive.Contains(p)) ordered.Add(p);
        foreach (var p in alive) if (!ordered.Contains(p)) ordered.Add(p);

        for (int i = 0; i < _slots.Count; i++)
        {
            var s = _slots[i];
            if (s.root == null) continue;

            bool has = i < ordered.Count;
            s.root.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            if (!has) continue;

            var ps = ordered[i];

            // 이름
            if (s.name != null)
                s.name.text = string.IsNullOrWhiteSpace(ps.playerName) ? $"Player {i + 1}" : ps.playerName;

            // 자산/목표
            if (s.value != null)
            {
                long eq = GetEstimatedCents(ps);
                s.value.text = $"{FormatMoney(eq)} / {FormatMoney(targetCents)}";
            }

            // 라이프
            if (s.life != null)
            {
                int clamped = Mathf.Clamp(ps.hp, 0, Mathf.Max(1, ps.maxHp));
                s.life.text = $"♡{clamped}";
            }

            // 아바타
            if (s.img != null) ApplyAvatarToVisualElement(ps, s.img);

            // 결과(success/fail)
            if (s.result != null)
            {
                long eq = GetEstimatedCents(ps);
                bool success = eq >= targetCents;

                // 텍스트
                s.result.text = success ? "성공!" : "실패...";

                // 클래스 토글
                s.result.EnableInClassList("success", success);
                s.result.EnableInClassList("fail", !success);
                // 혹시 이전 상태 잔존 방지
                if (success) s.result.RemoveFromClassList("fail");
                else         s.result.RemoveFromClassList("success");
            }
        }
    }

    /* ======= 순위 계산 및 정렬 ======= */

    void ComputeAndFreezeOrder()
    {
        _order.Clear();

        var list = FindObjectsOfType<PlayerState>()
            .Where(p => p != null && !p.isEliminated && p.hp > 0)
            .ToList();

        // HP ↓, 평가금 ↓, 이름
        list.Sort((a, b) =>
        {
            int hpCmp = b.hp.CompareTo(a.hp);
            if (hpCmp != 0) return hpCmp;

            long aEq = GetEstimatedCents(a);
            long bEq = GetEstimatedCents(b);
            int eqCmp = bEq.CompareTo(aEq);
            if (eqCmp != 0) return eqCmp;

            return string.Compare(b.playerName, a.playerName, StringComparison.Ordinal);
        });

        _order.AddRange(list);
    }

    void ReorderCardsByRankWithAnimation()
    {
        if (_modalBody == null || _slots.Count == 0) return;

        // 현재 표시 중인 카드들만 대상으로 정렬
        var aliveSlots = _slots.Where(s => s.root != null && s.root.style.display != DisplayStyle.None).ToList();

        // order 순서를 modal_body 자식 순서로 반영
        var currentPlayersBySlot = MapSlotsToPlayers();
        if (currentPlayersBySlot == null) return;

        // 목표 순서: _order 기준 살아있는 플레이어만
        var alivePlayers = currentPlayersBySlot.Values.Where(p => p != null && !p.isEliminated && p.hp > 0).ToList();
        var desiredOrder = _order.Where(p => p != null && alivePlayers.Contains(p)).ToList();

        // 전환 애니메이션을 유도하기 위해 각 카드에 클래스 부여
        foreach (var s in aliveSlots)
            s.root.EnableInClassList(rankTransitionClass, true);

        // 실제 DOM 순서 재배치
        // 간단히: modal_body에서 모든 team_*를 제거한 뒤 원하는 순서대로 다시 추가
        var allChildren = _modalBody.Children().ToList();
        foreach (var ch in allChildren) ch.RemoveFromHierarchy();

        // desiredOrder 개수만큼 해당하는 슬롯을 찾아서 붙인다
        foreach (var p in desiredOrder)
        {
            var slot = aliveSlots.FirstOrDefault(sl => currentPlayersBySlot.TryGetValue(sl, out var ps) && ps == p);
            if (slot.root != null) _modalBody.Add(slot.root);
        }

        // 남은 카드(표시 중이지만 desiredOrder에 없는 경우)가 있다면 뒤에 부착
        foreach (var s in aliveSlots)
            if (!_modalBody.Contains(s.root))
                _modalBody.Add(s.root);

        // 잠시 뒤 전환 클래스 제거(한 번만 트리거)
        StartCoroutine(RemoveClassNextFrame(rankTransitionClass, aliveSlots.Select(s => s.root)));
    }

    Dictionary<Slot, PlayerState> MapSlotsToPlayers()
    {
        // 슬롯 i에 어떤 플레이어가 그려졌는지 추정: PopulateModal에서 _order 기반으로 매핑했으므로
        _players.Clear();
        _players.AddRange(FindObjectsOfType<PlayerState>().Where(p => p != null));
        long targetCents = GetCurrentTargetCents();

        // 화면에 그려진 순서대로 _order의 라이브 플레이어를 매칭
        var alive = _players.Where(p => !p.isEliminated && p.hp > 0).ToList();
        var ordered = new List<PlayerState>();
        foreach (var p in _order) if (p != null && alive.Contains(p)) ordered.Add(p);
        foreach (var p in alive) if (!ordered.Contains(p)) ordered.Add(p);

        var dict = new Dictionary<Slot, PlayerState>();
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].root == null || _slots[i].root.style.display == DisplayStyle.None) continue;
            if (i < ordered.Count) dict[_slots[i]] = ordered[i];
        }
        return dict;
    }

    IEnumerator RemoveClassNextFrame(string className, IEnumerable<VisualElement> elements)
    {
        yield return null; // 1프레임
        foreach (var ve in elements)
            ve.RemoveFromClassList(className);
    }

    /* ======= HP 감소 애니메이션 ======= */

    void CachePlayersSnapshot()
    {
        var list = FindObjectsOfType<PlayerState>();
        foreach (var ps in list)
        {
            try
            {
                uint id = ps.netId; // Mirror NetworkIdentity
                if (_lastHpByNetId.ContainsKey(id)) _lastHpByNetId[id] = ps.hp;
                else _lastHpByNetId.Add(id, ps.hp);
            }
            catch { /* netId 없으면 무시 */ }
        }
    }

    void TriggerHpDamageAnimations()
    {
        if (_slots.Count == 0) return;

        var current = FindObjectsOfType<PlayerState>().Where(p => p != null).ToList();

        foreach (var s in _slots)
        {
            if (s.root == null || s.root.style.display == DisplayStyle.None) continue;

            var ps = FindPlayerForSlotIndex(s);
            if (ps == null) continue;

            int prevHp = 0;
            bool hasPrev = false;
            try
            {
                uint id = ps.netId;
                hasPrev = _lastHpByNetId.TryGetValue(id, out prevHp);
            }
            catch { }

            if (hasPrev && ps.hp < prevHp)
            {
                // HP 감소 감지. lifeDamageClass 부여 후 잠시 뒤 제거.
                s.root.AddToClassList(lifeDamageClass);
                StartCoroutine(RemoveClassAfterDelay(s.root, lifeDamageClass, 1.0f));
            }
        }

        // 최신 HP로 스냅샷 갱신
        CachePlayersSnapshot();
    }

    PlayerState FindPlayerForSlotIndex(Slot s)
    {
        // PopulateModal의 정렬 규칙과 동일하게 슬롯 인덱스를 _order 기준으로 맵핑
        _players.Clear();
        _players.AddRange(FindObjectsOfType<PlayerState>().Where(p => p != null));
        var alive = _players.Where(p => !p.isEliminated && p.hp > 0).ToList();
        var ordered = new List<PlayerState>();
        foreach (var p in _order) if (p != null && alive.Contains(p)) ordered.Add(p);
        foreach (var p in alive) if (!ordered.Contains(p)) ordered.Add(p);

        int idx = _slots.IndexOf(s);
        if (idx >= 0 && idx < ordered.Count) return ordered[idx];
        return null;
        }

    IEnumerator RemoveClassAfterDelay(VisualElement ve, string className, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        ve.RemoveFromClassList(className);
    }

    /* ======= 모달 표시/비표시 ======= */

    void ShowModal()
    {
        if (_modalRoot == null) return;
        _modalRoot.RemoveFromClassList("hidden");
        _modalShown = true;
    }

    void HideModal()
    {
        if (_modalRoot == null) return;
        _modalRoot.AddToClassList("hidden");
        _modalShown = false;
    }

    void HideModalImmediate()
    {
        if (_modalRoot == null) return;
        if (!_modalRoot.ClassListContains("hidden"))
            _modalRoot.AddToClassList("hidden");
        _modalShown = false;
    }

    /* ======= 유틸 ======= */

    long GetCurrentTargetCents()
    {
        var tm = TimeManager.Instance;
        if (tm == null) return 0;
        return tm.currentTargetCapitalCents;
    }

    long GetEstimatedCents(PlayerState ps)
    {
        if (ps == null) return 0;

        var sync = ps.GetComponent<PlayerEstimatedAssetSync>();
        if (sync != null && sync.EstimatedEquityCents > 0) return sync.EstimatedEquityCents;

        return ps.EquityCents;
    }

    string FormatMoney(long cents)
    {
        if (!showCents)
        {
            long unit = cents / 100;
            return currencySymbol + unit.ToString("#,0", CultureInfo.InvariantCulture);
        }
        double v = cents / 100.0;
        return currencySymbol + v.ToString("#,0.00", CultureInfo.InvariantCulture);
    }

    // ★ UPDATED: 동물 키 우선 로딩
    void ApplyAvatarToVisualElement(PlayerState ps, VisualElement ve)
    {
        if (ve == null || ps == null)
        {
            if (ve != null) ve.style.backgroundImage = null;
            return;
        }

        Sprite sprite = null;

        // 1) 동물 키 경로: Assets/Resources/robby_image/player_{animal}.png
        if (!string.IsNullOrWhiteSpace(ps.avatarAnimal))
        {
            sprite = Resources.Load<Sprite>($"robby_image/player_{ps.avatarAnimal}");
        }

        // 2) 폴백: PlayerState의 해석 로직 사용
        if (sprite == null)
            sprite = ps.ResolveAvatarSprite();

        if (sprite != null)
        {
            ve.style.backgroundImage = new StyleBackground(sprite);
            ve.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
        }
        else
        {
            ve.style.backgroundImage = null;
        }
    }
}
