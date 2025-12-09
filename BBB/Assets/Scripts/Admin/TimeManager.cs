using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UI;            // 추가: UGUI
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public enum GamePhase : byte
{
    Idle = 0,
    Round = 1,
    Result = 2,
    Prep = 3,
    Finished = 4
}

public sealed class TimeManager : NetworkBehaviour
{
    public static TimeManager Instance { get; private set; }

    [Header("Round Config")]
    public float secondsPerDay = 3f;
    public float resultDurationSec = 10f;
    public float prepDurationSec = 30f;
    public int roundLengthDays = 90; // 표시용

    [Header("Target Capital (Quadratic Growth)")]
    public double targetRound1USD = 120_000;
    public double targetLinearA = 25_000;
    public double targetQuadB = 5_000;

    [Header("Initial Capital Policy")]
    [Range(0f, 1f)] public float initialCapitalRatio = 0.8f;

    [Header("HP Damage Policy")]
    public int baseHpDamage = 20;
    public int damageIncPerRound = 10;

    [Header("End-of-Game UI (Full-screen)")]
    public string victoryResourcePath = "victory"; // Assets/Resources/victory.png
    public string loseResourcePath = "lose";       // Assets/Resources/lose.png
    public float endImageDurationSec = 5f;
    public string lobbySceneName = "Lobby";

    [SyncVar(hook = nameof(OnPhaseSync))] public GamePhase currentPhase = GamePhase.Idle;
    [SyncVar(hook = nameof(OnRoundSync))] public int currentRound = 0;
    [SyncVar(hook = nameof(OnDateSync))] public long currentDateTicks = 0;
    [SyncVar(hook = nameof(OnRemainSync))] public float phaseRemainingSec = 0f;
    [SyncVar(hook = nameof(OnPhaseTotalSync))] public float phaseTotalSec = 0f;
    [SyncVar(hook = nameof(OnDayIndexSync))] public int dayIndex = -1;
    [SyncVar(hook = nameof(OnIsDailyTickingSync))] public bool isDailyTicking = false;
    [SyncVar] public float syncedSecondsPerDay = 3f;
    [SyncVar(hook = nameof(OnPausedSync))] public bool isPaused = false;
    [SyncVar] public long currentTargetCapitalCents = 0;

    public static event Action<GamePhase, int> OnClientPhaseChanged;
    public static event Action<DateTime, int, int> OnClientNewDay;
    public static event Action<float, float> OnClientTick;
    public static event Action<bool> OnClientPauseChanged;

    public static event Action<GamePhase, int> OnServerPhaseChanged;
    public static event Action<DateTime, int, int> OnServerNewDay;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("[TimeManager] Duplicate instance. Destroy.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        syncedSecondsPerDay = secondsPerDay;
        StartCoroutine(ServerGameLoop());
        StartCoroutine(ServerHeartbeat());
        StartCoroutine(ItemEffectTimerUpdate());
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        StopAllCoroutines();
    }

    IEnumerator ServerGameLoop()
    {
        yield return null;

        DateTime dateCursor = new DateTime(2026, 1, 1);
        int round = 0;

        while (true)
        {
            round++;
            currentRound = round;

            currentTargetCapitalCents = USDToCents(ComputeTargetUSD(round));

            // 본게임
            yield return RunRoundToQuarterEnd(round, dateCursor);

            // 결과 계산
            yield return ServerHandleResultPhase(round);

            // 결과턴
            yield return RunPhase(GamePhase.Result, round, resultDurationSec);

            if (CountAlivePlayers() <= 1)
            {
                AnnounceWinnerAndFinish();
                yield break;
            }

            // 준비턴
            yield return RunPhase(GamePhase.Prep, round, prepDurationSec);

            // 다음 라운드 준비
            ServerPrepareNextRound(round + 1);

            var qEnd = GetQuarterEndFixed(dateCursor);
            dateCursor = qEnd.AddDays(1);
        }
    }

    IEnumerator RunRoundToQuarterEnd(int round, DateTime startDate)
    {
        currentRound = round;
        isDailyTicking = true;
        SetPhase(GamePhase.Round, round, 0f);

        DateTime qEnd = GetQuarterEndFixed(startDate);
        DateTime cur = startDate;
        int idx = 0;

        while (cur <= qEnd)
        {
            dayIndex = idx;
            currentDateTicks = cur.Ticks;
            phaseTotalSec = syncedSecondsPerDay;
            phaseRemainingSec = syncedSecondsPerDay;

            OnServerNewDay?.Invoke(cur, round, idx);

            while (phaseRemainingSec > 0f)
            {
                yield return ServerWaitUnpaused(0.25f);
                phaseRemainingSec = Mathf.Max(0f, phaseRemainingSec - 0.25f);
            }

            idx++;
            cur = cur.AddDays(1);
        }

        isDailyTicking = false;
        dayIndex = -1;
        phaseRemainingSec = 0f;
        phaseTotalSec = 0f;
    }

    IEnumerator RunPhase(GamePhase phase, int round, float duration)
    {
        if (duration <= 0f)
        {
            SetPhase(phase, round, 0f);
            yield break;
        }

        currentRound = round;
        isDailyTicking = false;
        dayIndex = -1;

        SetPhase(phase, round, duration);

        while (phaseRemainingSec > 0f)
        {
            yield return ServerWaitUnpaused(0.25f);
            phaseRemainingSec = Mathf.Max(0f, phaseRemainingSec - 0.25f);
        }
        phaseRemainingSec = 0f;
    }

    void SetPhase(GamePhase phase, int round, float totalSec)
    {
        currentPhase = phase;
        currentRound = round;
        phaseTotalSec = Mathf.Max(0f, totalSec);
        phaseRemainingSec = phaseTotalSec;

        if (phase != GamePhase.Round) currentDateTicks = 0;

        OnServerPhaseChanged?.Invoke(currentPhase, currentRound);
        OnClientPhaseChanged?.Invoke(currentPhase, currentRound);
    }

    IEnumerator ServerWaitUnpaused(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            if (!isPaused) elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    IEnumerator ServerHeartbeat()
    {
        var wait = new WaitForSecondsRealtime(1f);
        while (true) { yield return wait; }
    }

    [Server] public void Pause()  { isPaused = true; }
    [Server] public void Resume() { isPaused = false; }

    // 결과턴 시작 시 처리
    [Server]
    IEnumerator ServerHandleResultPhase(int roundJustEnded)
    {
        foreach (var ps in PlayerState.All)
        {
            if (ps == null || ps.isEliminated) continue;
            ps.ServerLiquidateAllHoldings();
            ps.RecalculateTotalValuation();
        }

        foreach (var ps in PlayerState.All)
        {
            if (ps == null || ps.isEliminated) continue;
            var sync = ps.GetComponent<PlayerEstimatedAssetSync>();
            if (sync != null) sync.ServerSetEstimated(ps.EquityCents);
        }

        long target = currentTargetCapitalCents;
        int damage = ComputeRoundHpDamage(roundJustEnded);

        foreach (var ps in PlayerState.All)
        {
            if (ps == null || ps.isEliminated) continue;

            bool pass = ps.EquityCents >= target;
            if (!pass)
            {
                ps.ServerDamage(damage);

                if (ps.hp <= 0)
                {
                    if (!ps.isEliminated) ps.isEliminated = true;
                    StartCoroutine(ServerDefeatFlow(ps));
                }
            }
        }

        yield return null;

        if (CountAlivePlayers() <= 1)
        {
            AnnounceWinnerAndFinish();
        }
    }

    [Server]
    int ComputeRoundHpDamage(int round)
    {
        if (round <= 1) return Mathf.Max(1, baseHpDamage);
        int add = (round - 1) * Mathf.Max(0, damageIncPerRound);
        int dmg = baseHpDamage + add;
        return Mathf.Max(1, dmg);
    }

    [Server]
    void AnnounceWinnerAndFinish()
    {
        var winner = GetFirstAlive();
        if (winner != null)
        {
            StartCoroutine(ServerVictoryFlow(winner));
        }
        else
        {
            SafeServerChangeToLobby();
        }
    }

    [Server]
    IEnumerator ServerDefeatFlow(PlayerState ps)
    {
        var conn = ps != null ? ps.connectionToClient as NetworkConnectionToClient : null;
        if (conn != null)
        {
            TargetShowEndImage(conn, false, endImageDurationSec, loseResourcePath);
            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, endImageDurationSec));
            try { conn.Disconnect(); } catch { }
        }
    }

    [Server]
    IEnumerator ServerVictoryFlow(PlayerState winner)
    {
        float waitSec = Mathf.Max(0.1f, endImageDurationSec);

        foreach (var kv in NetworkServer.connections)
        {
            var conn = kv.Value;
            if (conn == null) continue;

            TargetShowEndImage(conn, true, waitSec, victoryResourcePath);
        }

        yield return new WaitForSecondsRealtime(waitSec);

        SetPhase(GamePhase.Finished, currentRound, 0f);
        SafeServerChangeToLobby();
    }

    // 일부 수정
    [Server]
    void SafeServerChangeToLobby()
    {
        // RebootOnLoad 씬으로 전환하여 완전한 리셋 수행
        // RebootOnLoad.cs가 자동으로 클라이언트 알림, 네트워크 종료, 서버 재시작 처리
        if (NetworkManager.singleton != null)
        {
            try 
            { 
                // "ReBoot" 씬으로 전환 (RebootOnLoad.cs가 있는 씬)
                NetworkManager.singleton.ServerChangeScene("ReBootServer"); 
            }
            catch (Exception e) 
            { 
                Debug.LogWarning($"[TimeManager] ServerChangeScene to ReBoot failed: {e.Message}"); 
            }
        }
        else
        {
            Debug.LogWarning("[TimeManager] NetworkManager missing.");
        }
    }

    [TargetRpc]
    void TargetShowEndImage(NetworkConnectionToClient conn, bool isVictory, float seconds, string resourcePath)
    {
        StartCoroutine(Client_ShowFullScreenImage(seconds, resourcePath));
    }

    // 전체화면 오버레이: UGUI Canvas + RawImage, 씬 전환 유지
    IEnumerator Client_ShowFullScreenImage(float seconds, string resourcePath)
    {
        Texture2D tex = null;
        try { tex = Resources.Load<Texture2D>(resourcePath); } catch { }
        if (tex == null)
        {
            Debug.LogWarning($"[TimeManager] Resources.Load failed: {resourcePath}");
            yield break;
        }

        // Canvas 생성
        var go = new GameObject("EndOverlayCanvas");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = Int16.MaxValue; // 최상단
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();
        DontDestroyOnLoad(go); // 씬 전환에도 유지

        // RawImage 생성
        var imgGO = new GameObject("EndImage");
        imgGO.transform.SetParent(go.transform, false);
        var raw = imgGO.AddComponent<RawImage>();
        raw.texture = tex;
        raw.raycastTarget = true; // 입력 차단

        // 전체화면 앵커
        var rt = raw.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // 화면 가득 채우도록 Aspect 보정
        // RawImage는 기본으로 늘려 채움. 필요 시 Material로 보정 가능하나 현재 요구사항은 전체화면 표시.
        
        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, seconds));

        if (go != null) Destroy(go);
    }

    [Server]
    void ServerPrepareNextRound(int nextRound)
    {
        long nextTarget = USDToCents(ComputeTargetUSD(nextRound));
        currentTargetCapitalCents = nextTarget;
    }

    int CountAlivePlayers()
    {
        int n = 0;
        foreach (var ps in PlayerState.All)
            if (ps != null && !ps.isEliminated && ps.hp > 0) n++;
        return n;
    }

    PlayerState GetFirstAlive()
    {
        foreach (var ps in PlayerState.All)
            if (ps != null && !ps.isEliminated && ps.hp > 0) return ps;
        return null;
    }

    public double ComputeTargetUSD(int round)
    {
        if (round <= 1) return targetRound1USD;
        double r = round - 1;
        return targetRound1USD + targetLinearA * r + targetQuadB * r * r;
    }

    public long USDToCents(double usd) => (long)Math.Round(usd * 100.0);

    static int FixedMonthDays(int month)
    {
        switch (month)
        {
            case 1: case 3: case 5: case 7: case 8: case 10: case 12: return 31;
        }
        if (month == 2) return 28;
        return 30;
    }

    static DateTime GetQuarterEndFixed(DateTime date)
    {
        int qEndMonth = ((date.Month - 1) / 3 + 1) * 3;
        int day = FixedMonthDays(qEndMonth);
        return new DateTime(date.Year, qEndMonth, day);
    }

    void OnPhaseSync(GamePhase _, GamePhase newPhase) => OnClientPhaseChanged?.Invoke(newPhase, currentRound);
    void OnRoundSync(int _, int __) { }
    void OnDateSync(long _, long newTicks)
    {
        if (newTicks > 0)
        {
            DateTime d = new DateTime(newTicks);
            OnClientNewDay?.Invoke(d, currentRound, dayIndex);
        }
    }
    void OnRemainSync(float _, float __) => OnClientTick?.Invoke(phaseRemainingSec, phaseTotalSec);
    void OnPhaseTotalSync(float _, float __) => OnClientTick?.Invoke(phaseRemainingSec, phaseTotalSec);
    void OnDayIndexSync(int _, int __) { }
    void OnIsDailyTickingSync(bool _, bool __) { }
    void OnPausedSync(bool _, bool paused) => OnClientPauseChanged?.Invoke(paused);

    public static DateTime? CurrentDate
    {
        get
        {
            if (Instance == null || Instance.currentDateTicks == 0) return null;
            return new DateTime(Instance.currentDateTicks);
        }
    }

    public static float RemainingSeconds => Instance != null ? Instance.phaseRemainingSec : 0f;
    public static float PhaseTotalSeconds => Instance != null ? Instance.phaseTotalSec : 0f;
    public static GamePhase Phase => Instance != null ? Instance.currentPhase : GamePhase.Idle;
    public static int Round => Instance != null ? Instance.currentRound : 0;
    public static bool IsPaused => Instance != null && Instance.isPaused;
    public static bool IsDailyTicking => Instance != null && Instance.isDailyTicking;

    IEnumerator ItemEffectTimerUpdate()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);
            if (currentPhase == GamePhase.Round)
            {
                ItemEffectApplier.UpdateHoldingTimers();
            }
        }
    }

#if UNITY_EDITOR
    void OnGUI()
    {
        if (!Application.isPlaying) return;

        var rect = new Rect(10, 10, 600, 150);
        GUILayout.BeginArea(rect, GUI.skin.box);
        GUILayout.Label($"[TimeManager] Phase={currentPhase}  Round={currentRound}");
        if (CurrentDate.HasValue)
            GUILayout.Label($"Date={CurrentDate.Value:yyyy-MM-dd}  DayIdx={dayIndex}");
        else
            GUILayout.Label("Date=—  DayIdx={dayIndex}");
        GUILayout.Label($"Remain={phaseRemainingSec:F1}s / {phaseTotalSec:F1}s  Daily={(isDailyTicking ? "Yes" : "No")}  Paused={(isPaused ? "Yes" : "No")}");
        GUILayout.Label($"Target=${currentTargetCapitalCents/100.0:###,###,###}");
        GUILayout.EndArea();
    }
#endif
}
