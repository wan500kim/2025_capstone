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
[DefaultExecutionOrder(10000)]
public sealed class TeamDashboardBinder : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Display")]
    [Tooltip("통화 기호. 예: $, ₩")]
    [SerializeField] private string currencySymbol = "$";
    [Tooltip("소수점 두 자리 표기 여부")]
    [SerializeField] private bool showCents = true;
    [Tooltip("UI 갱신 주기(초)")]
    [SerializeField] private float refreshInterval = 0.5f;

    private struct Slot
    {
        public VisualElement root;
        public VisualElement img;
        public Label name;
        public Label value;
        public Label life;
    }

    private VisualElement _root;
    private VisualElement _teamDashboard;
    private readonly List<Slot> _slots = new List<Slot>(4);
    private readonly List<PlayerState> _players = new List<PlayerState>(8);

    private bool _panelReady;
    private bool _uiBound;
    private bool _clientReady;    // 원격 클라이언트 연결 완료
    private bool _spawnObserved;  // 최소 한 번 PlayerState 스폰 확인

    // 순위 고정 로직
    private readonly List<PlayerState> _order = new List<PlayerState>(8);
    private GamePhase _lastPhase = GamePhase.Idle;
    private bool _orderedThisResult = false;

    void Awake()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
    }

    void OnEnable()
    {
        // 서버 전용 프로세스에서는 표시 불필요
        if (Application.isBatchMode || (NetworkServer.active && !NetworkClient.active))
        {
            enabled = false;
            return;
        }

        TrySubscribeTimeEvents();

        // 초기화 순서 보장 코루틴
        StartCoroutine(BootstrapSequence());

        // 주기 갱신 루프
        StartCoroutine(RefreshLoop());
    }

    void OnDisable()
    {
        TryUnsubscribeTimeEvents();
        StopAllCoroutines();
    }

    IEnumerator BootstrapSequence()
    {
        yield return StartCoroutine(WaitForClientActive(10f));
        yield return StartCoroutine(WaitForPanel(10f));
        BindUI();
        yield return StartCoroutine(WaitForAnyPlayerSpawn(5f));
        SafeRefresh(); // 초기 1회
        // 최초 표시용 순위 산정(아직 결과턴 전이므로 1회만)
        if (_order.Count == 0) ComputeAndFreezeOrder();
    }

    IEnumerator WaitForClientActive(float timeoutSec)
    {
        float t = 0f;
        while (!NetworkClient.active || !NetworkClient.isConnected)
        {
            if (t > timeoutSec) break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        _clientReady = NetworkClient.active;
    }

    IEnumerator WaitForPanel(float timeoutSec)
    {
        float t = 0f;
        while (true)
        {
            if (uiDocument != null && uiDocument.rootVisualElement != null && uiDocument.rootVisualElement.panel != null)
                break;

            if (t > timeoutSec) break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        _panelReady = uiDocument != null && uiDocument.rootVisualElement != null && uiDocument.rootVisualElement.panel != null;
        if (!_panelReady) Debug.LogError("[TeamDashboardBinder] UI Panel 준비 실패");
    }

    IEnumerator WaitForAnyPlayerSpawn(float timeoutSec)
    {
        float t = 0f;
        while (FindObjectsOfType<PlayerState>().Length == 0)
        {
            if (t > timeoutSec) break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        _spawnObserved = FindObjectsOfType<PlayerState>().Length > 0;
    }

    void TrySubscribeTimeEvents()
    {
        try
        {
            TimeManager.OnClientPhaseChanged += OnPhaseChanged;
            TimeManager.OnClientNewDay += OnNewDay;
        }
        catch { /* 이벤트가 없는 프로젝트 대비 */ }
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
        if (_root == null) { Debug.LogError("[TeamDashboardBinder] rootVisualElement null"); return; }

        _teamDashboard = _root.Q<VisualElement>("team_dashboard"); // 반드시 name="team_dashboard"
        if (_teamDashboard == null)
        {
            Debug.LogError("[TeamDashboardBinder] 'team_dashboard' 미발견. UXML name 확인");
            return;
        }

        _slots.Clear();
        for (int i = 1; i <= 4; i++)
        {
            var teamRoot = GetTeamRoot(_teamDashboard, i);
            if (teamRoot == null)
            {
                Debug.LogWarning($"[TeamDashboardBinder] 팀 루트 미발견: team_{i} (class 또는 name 확인)");
                continue;
            }

            var img  = teamRoot.Q<VisualElement>($"team_{i}_img");
            var name = teamRoot.Q<Label>($"team_{i}_name");
            var val  = teamRoot.Q<Label>($"team_{i}_value");
            var life = teamRoot.Q<Label>($"team_{i}_life_num");

            if (img == null || name == null || val == null || life == null)
                Debug.LogWarning($"[TeamDashboardBinder] team_{i} 내부 name 누락: img/name/value/life_num 확인");

            _slots.Add(new Slot {
                root = teamRoot,
                img  = img,
                name = name,
                value = val,
                life = life
            });
        }

        if (_slots.Count == 0)
            Debug.LogWarning("[TeamDashboardBinder] 바인딩된 슬롯이 없습니다. UXML 구조 확인");

        _uiBound = true;
    }

    // 팀 루트 선택: name 우선 → class 정확일치 → class 포함
    VisualElement GetTeamRoot(VisualElement scope, int idx)
    {
        var byName = scope.Q<VisualElement>($"team_{idx}");
        if (byName != null) return byName;

        var byClassExact = scope.Query<VisualElement>(className: $"team_{idx}").First();
        if (byClassExact != null) return byClassExact;

        var byClassContains = scope.Query<VisualElement>().Where(ve => ve != null && ve.ClassListContains($"team_{idx}")).First();
        return byClassContains;
    }

    void OnPhaseChanged(GamePhase phase, int round)
    {
        _lastPhase = phase;

        // 결과턴 진입 시점: HP 감소가 이미 서버에서 적용된 뒤임(TimeManager에서 먼저 처리)
        // 이번 결과턴 동안에 딱 한 번만 순위 재계산
        if (phase == GamePhase.Result)
        {
            _orderedThisResult = false;
            ComputeAndFreezeOrder(); // 1회 재정렬
            _orderedThisResult = true;
            SafeRefresh();
            return;
        }

        // 다른 페이즈에서는 순위 고정. 수치 갱신만.
        SafeRefresh();
    }

    void OnNewDay(DateTime date, int round, int dayIndex) => SafeRefresh();

    IEnumerator RefreshLoop()
    {
        var wait = new WaitForSeconds(refreshInterval);
        while (true)
        {
            SafeRefresh();
            yield return wait;
        }
    }

    void SafeRefresh()
    {
        try { Refresh(); }
        catch (Exception ex) { Debug.LogWarning($"[TeamDashboardBinder] Refresh 예외: {ex.Message}"); }
    }

    void Refresh()
    {
        if (!_panelReady || !_uiBound || !_clientReady) return;
        if (_slots.Count == 0) return;

        // 최신 플레이어 스냅샷
        _players.Clear();
        _players.AddRange(FindObjectsOfType<PlayerState>());

        if (!_spawnObserved && _players.Count > 0) _spawnObserved = true;

        // 최초 표시 전이라면 1회 정렬
        if (_order.Count == 0) ComputeAndFreezeOrder();

        // 생존자만 표시. 순위는 고정된 _order를 기준으로 생성
        var aliveSet = new HashSet<PlayerState>(_players.Where(p => p != null && !p.isEliminated && p.hp > 0));

        // _order에서 생존자만 추출
        var orderedAlive = new List<PlayerState>(aliveSet.Count);
        foreach (var p in _order)
            if (p != null && aliveSet.Contains(p)) orderedAlive.Add(p);

        // 새로 생긴 플레이어가 있으면 뒤에 부착
        foreach (var p in aliveSet)
            if (!orderedAlive.Contains(p)) orderedAlive.Add(p);

        // 슬롯 채우기
        long targetCents = GetCurrentTargetCents();

        for (int i = 0; i < _slots.Count; i++)
        {
            var s = _slots[i];
            if (s.root == null) continue;

            bool has = i < orderedAlive.Count;
            if (!has)
            {
                s.root.style.display = DisplayStyle.None;
                continue;
            }

            var ps = orderedAlive[i];
            s.root.style.display = DisplayStyle.Flex;

            if (s.name != null)
                s.name.text = string.IsNullOrWhiteSpace(ps.playerName) ? "Player" : ps.playerName;

            if (s.value != null)
            {
                long eq = GetEstimatedCents(ps);
                s.value.text = $"{FormatMoney(eq)} / {FormatMoney(targetCents)}";
            }

            if (s.life != null)
                s.life.text = $"♡{Mathf.Clamp(ps.hp, 0, Mathf.Max(1, ps.maxHp))}";

            if (s.img != null)
                ApplyAvatarToVisualElement(ps, s.img);
        }
    }

    // 결과턴에서 단 한 번만 호출하여 순위 고정
    void ComputeAndFreezeOrder()
    {
        _order.Clear();

        var all = FindObjectsOfType<PlayerState>()
            .Where(p => p != null && !p.isEliminated && p.hp > 0)
            .ToList();

        // 정렬: HP ↓, 평가금 ↓, 이름
        all.Sort((a, b) =>
        {
            int hpCmp = b.hp.CompareTo(a.hp);
            if (hpCmp != 0) return hpCmp;

            long aEq = GetEstimatedCents(a);
            long bEq = GetEstimatedCents(b);
            int eqCmp = bEq.CompareTo(aEq);
            if (eqCmp != 0) return eqCmp;

            return string.Compare(b.playerName, a.playerName, StringComparison.Ordinal);
        });

        _order.AddRange(all);
    }

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

    // === 변경 핵심: 아바타 인덱스 해석을 playerImageIndex/avatarId/animal 키 순서로 보강 → 동물 키 우선 ===
    int ResolveAvatarIndex(PlayerState ps, int maxCount)
    {
        // 동물 키가 있으면 인덱스를 사용하지 않음
        if (ps != null && !string.IsNullOrWhiteSpace(ps.avatarAnimal))
            return -1;

        // 1) playerImageIndex가 유효하면 사용
        if (ps != null && ps.playerImageIndex >= 0 && ps.playerImageIndex < maxCount)
            return ps.playerImageIndex;

        // 2) avatarId 폴백
        if (ps != null && ps.avatarId >= 0)
        {
            if (maxCount > 0) return ps.avatarId % maxCount;
            return ps.avatarId;
        }

        // 3) 최종 폴백
        return 0;
    }

    void ApplyAvatarToVisualElement(PlayerState ps, VisualElement ve)
    {
        if (ve == null || ps == null)
        {
            if (ve != null) ve.style.backgroundImage = null;
            return;
        }

        Sprite sprite = null;

        // 1) 동물 키 우선: Assets/Resources/robby_image/player_{animal}.png
        if (!string.IsNullOrWhiteSpace(ps.avatarAnimal))
        {
            sprite = Resources.Load<Sprite>($"robby_image/player_{ps.avatarAnimal}");
        }

        // 2) 인덱스 또는 avatarId 기반 폴백
        if (sprite == null)
        {
            var mrm = MyRoomManagerSafe();
            int arrayLen = (mrm != null && mrm.avatarSprites != null) ? mrm.avatarSprites.Length : 0;
            int idx = ResolveAvatarIndex(ps, arrayLen);

            if (idx >= 0 && mrm != null && mrm.avatarSprites != null && arrayLen > 0)
            {
                sprite = mrm.avatarSprites[Mathf.Clamp(idx, 0, arrayLen - 1)];
            }

            if (sprite == null && idx >= 0)
            {
                // Resources 폴백 경로들(프로젝트마다 다를 수 있어 다중 시도)
                string[] paths =
                {
                    $"avatars/avatar_{idx}",
                    $"avatar/avatar_{idx}",
                    $"Player_img/player_img{idx}",
                    $"image_{idx}"
                };

                foreach (var p in paths)
                {
                    sprite = Resources.Load<Sprite>(p);
                    if (sprite != null) break;
                }
            }
        }

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

    dynamic MyRoomManagerSafe()
    {
        try { return MyRoomManager.Instance; }
        catch { return null; }
    }
}
