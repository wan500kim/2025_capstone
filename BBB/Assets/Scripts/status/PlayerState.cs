using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

[Serializable] 
public class OwnedStock
{
    public string stockId;
    public int quantity;
    public double averagePurchasePrice;
}

[Serializable]
public class PlayerState : NetworkBehaviour
{
    public static readonly HashSet<PlayerState> All = new HashSet<PlayerState>();

    [SyncVar] public string playerId = "";
    [SyncVar] public string playerName = "Player";

    [SyncVar(hook = nameof(OnHpSync))] public int hp = 100;
    [SyncVar] public int maxHp = 100;
    [SyncVar] public bool isEliminated;

    [Header("Config")]
    [SerializeField] private long initialCashCents = 100_00L;
    [SerializeField] private bool allowRoundResetDownscale = false;

    [SyncVar(hook = nameof(OnCashSync))]   private long cashCents;
    [SyncVar(hook = nameof(OnEquitySync))] private long equityCents;

    public readonly SyncDictionary<string, int> holdings = new SyncDictionary<string, int>();
    public readonly SyncList<OwnedStock> portfolio = new SyncList<OwnedStock>();

    [SyncVar(hook = nameof(OnImageIndexSync))] public int playerImageIndex = 1;

    [SyncVar(hook = nameof(OnAvatarChanged))] public int avatarId = -1;
    void OnAvatarChanged(int oldV, int newV) => ApplyHeadIconByState();

    // ★ 로비에서 전달되는 동물 키(cat, dog, hamster, rabbit)
    [SyncVar(hook = nameof(OnAvatarAnimalChanged))] public string avatarAnimal = "";
    void OnAvatarAnimalChanged(string oldV, string newV)
    {
        OnAvatarAnimalChangedEvent?.Invoke(oldV, newV);
        ApplyHeadIconByState();
    }
    public event Action<string, string> OnAvatarAnimalChangedEvent;

    [SerializeField] Image headIcon;

    int lastAppliedRound = 0;

    public long CashCents   => cashCents;
    public long EquityCents => equityCents;
    public long InitialCashCents => initialCashCents;

    public event Action<long, long> OnCashChanged;
    public event Action<long, long> OnEquityChanged;
    public event Action<int, int>   OnHpChanged;
    public event Action<int, int>   OnImageIndexChanged;
    public event Action<SyncDictionary<string, int>.Operation, string, int> OnHoldingsChanged;

    public override void OnStartServer()
    {
        All.Add(this);
        if (maxHp <= 0) maxHp = 100;
        if (hp <= 0) hp = maxHp;

        cashCents = Math.Max(0, initialCashCents);
        if (equityCents < 0) equityCents = cashCents;
        if (playerImageIndex < 1) playerImageIndex = 1;

        TimeManager.OnServerPhaseChanged += OnServerPhaseChangedHandler;

        TryApplyRoundInitialCapital(TimeManager.Round);
    }

    public override void OnStopServer()
    {
        All.Remove(this);
        TimeManager.OnServerPhaseChanged -= OnServerPhaseChangedHandler;
    }

    public override void OnStartClient()
    {
        ApplyHeadIconByState();
        holdings.OnChange += Holdings_OnChange;
        portfolio.OnChange += Portfolio_OnChange;
    }

    public override void OnStopClient()
    {
        holdings.OnChange -= Holdings_OnChange;
        portfolio.OnChange -= Portfolio_OnChange;
    }

    //추가한 함수 본인을 로컬 플레이어로 등록
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Debug.Log($"[PlayerState] OnStartLocalPlayer 호출됨 - {playerName} (isLocalPlayer: {isLocalPlayer})");
        
        // 클라이언트 측에서도 All에 추가 (이미 서버에서 추가되었지만 확실하게)
        if (!All.Contains(this))
        {
            All.Add(this);
        }
    }

    void OnServerPhaseChangedHandler(GamePhase phase, int round)
    {
        if (!isServer) return;

        if (phase == GamePhase.Round)
        {
            TryApplyRoundInitialCapital(round);
        }
    }

    [Server]
    void TryApplyRoundInitialCapital(int round)
    {
        if (round <= 0) return;
        if (lastAppliedRound == round) return;
        if (isEliminated || hp <= 0) return;

        var tm = TimeManager.Instance;
        if (tm == null) return;

        double targetUsd = tm.ComputeTargetUSD(round);
        long targetCents = tm.USDToCents(targetUsd);

        long desiredInitial = (long)Math.Round(targetCents * tm.initialCapitalRatio);

        // 추가한 함수 "충분한 총알" 아이템 효과 적용 (ItemEffect.money)
        if (ItemEffectTracker.Instance != null && ItemEffectTracker.Instance.HasEffect(this, ItemEffect.money))
        {
            int bonusPercent = ItemEffectTracker.Instance.GetEffectValue(this, ItemEffect.money);
            desiredInitial = (long)Math.Round(desiredInitial * (1.0 + bonusPercent / 100.0));
            Debug.Log($"[PlayerState] {playerName}: '충분한 총알' 아이템 효과 적용 - 초기 자본금 +{bonusPercent}% 증가");
        }

        long nextInitial = allowRoundResetDownscale ? desiredInitial : Math.Max(initialCashCents, desiredInitial);

        initialCashCents = nextInitial;

        if (allowRoundResetDownscale)
            cashCents = nextInitial;
        else
            cashCents = Math.Max(cashCents, nextInitial);

        RecalculateTotalValuation();
        var sync = GetComponent<PlayerEstimatedAssetSync>();
        if (sync != null) sync.ServerSetEstimated(EquityCents);

        lastAppliedRound = round;
    }

    public void BindHeadIcon(Image img)
    {
        headIcon = img;
        ApplyHeadIconByState();
    }

    // ★ 아바타 적용 로직을 동물 키 우선으로 통합
    void ApplyHeadIconByState()
    {
        if (!headIcon) return;

        Sprite s = ResolveAvatarSprite();
        if (s != null)
        {
            headIcon.enabled = true;
            headIcon.sprite = s;
        }
        else
        {
            headIcon.enabled = false;
        }
    }

    // ★ 동물 키 → Sprite, 폴백으로 avatarId / playerImageIndex 사용
    public Sprite ResolveAvatarSprite()
    {
        // 1) 동물 키 우선
        if (!string.IsNullOrWhiteSpace(avatarAnimal))
        {
            var sprite = Resources.Load<Sprite>($"robby_image/player_{avatarAnimal}");
            if (sprite != null) return sprite;
        }

        // 2) MyRoomManager 배열 스프라이트 폴백
        if (MyRoomManager.Instance != null && MyRoomManager.Instance.avatarSprites != null)
        {
            var list = MyRoomManager.Instance.avatarSprites;
            int idx = avatarId >= 0 ? avatarId : playerImageIndex;
            if (idx >= 0 && idx < list.Length && list[idx] != null) return list[idx];
        }

        // 3) 리소스 폴백(인덱스 네이밍 관례)
        int i = Mathf.Max(0, avatarId >= 0 ? avatarId : playerImageIndex);
        Sprite res = null;
        string[] tryPaths =
        {
            $"avatars/avatar_{i}",
            $"avatar/avatar_{i}",
            $"Player_img/player_img{i}",
            $"image_{i}"
        };
        foreach (var p in tryPaths)
        {
            res = Resources.Load<Sprite>(p);
            if (res != null) break;
        }
        return res;
    }

    [Server] public void ServerSetCash(long value)         { cashCents   = Math.Max(0, value); }
    [Server] public void ServerAddCash(long delta)         { cashCents   = Math.Max(0, checked(cashCents + delta)); }
    [Server] public void ServerSetEquity(long value)       { equityCents = Math.Max(0, value); }

    [Server]
    public void ServerConfigureInitialCash(long value, bool alsoApplyToCurrent = true)
    {
        initialCashCents = Math.Max(0, value);
        if (alsoApplyToCurrent)
        {
            ServerSetCash(initialCashCents);
        }
    }

    [Server] public void ServerSetHp(int value)
    {
        hp = Mathf.Clamp(value, 0, Mathf.Max(1, maxHp));
        if (hp <= 0) isEliminated = true;
    }
    [Server] public void ServerDamage(int amount) { if (amount > 0) ServerSetHp(hp - amount); }
    [Server] public void ServerHeal(int amount)   { if (amount > 0) ServerSetHp(hp + amount); }

    [Server]
    public void ServerAddHolding(string symbol, int delta)
    {
        if (string.IsNullOrWhiteSpace(symbol) || delta == 0) return;
        symbol = NormalizeSymbol(symbol);
        int cur = holdings.TryGetValue(symbol, out var v) ? v : 0;
        int next = checked(cur + delta);
        if (next > 0) holdings[symbol] = next;
        else holdings.Remove(symbol);
    }

    [Server]
    public void ServerSetHolding(string symbol, int quantity)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        symbol = NormalizeSymbol(symbol);
        if (quantity > 0) holdings[symbol] = quantity;
        else holdings.Remove(symbol);
    }

    [Server]
    public void ServerSetPlayerImageIndex(int index)
    {
        playerImageIndex = Mathf.Max(1, index);
    }

    // ===== 거래 관련 =====  이거 두개 수정함
    [Server]
    public void ServerExecuteBuyOrder(string stockId, int qty, double purchasePrice)
    {
        if (qty <= 0) return;

        int existingIndex = -1;
        OwnedStock owned = null;
        for (int i = 0; i < portfolio.Count; i++)
        {
            if (portfolio[i].stockId == stockId)
            {
                existingIndex = i;
                owned = portfolio[i];
                break;
            }
        }

        if (owned != null && existingIndex >= 0)
        {
            // 기존 주식 업데이트: SyncList 동기화를 위해 인덱서로 재할당
            double totalAmount = (owned.quantity * owned.averagePurchasePrice) + (qty * purchasePrice);
            int newQuantity = owned.quantity + qty;
            double newAvgPrice = totalAmount / newQuantity;
            
            portfolio[existingIndex] = new OwnedStock
            {
                stockId = stockId,
                quantity = newQuantity,
                averagePurchasePrice = newAvgPrice
            };
        }
        else
        {
            // 새 주식 추가
            portfolio.Add(new OwnedStock
            {
                stockId = stockId,
                quantity = qty,
                averagePurchasePrice = purchasePrice
            });
        }

        ServerAddHolding(stockId, qty);
    }

    [Server]
    public void ServerExecuteSellOrder(string stockId, int qty)
    {
        if (qty <= 0) return;

        int existingIndex = -1;
        OwnedStock owned = null;
        for (int i = 0; i < portfolio.Count; i++)
        {
            if (portfolio[i].stockId == stockId)
            {
                existingIndex = i;
                owned = portfolio[i];
                break;
            }
        }

        if (owned != null && existingIndex >= 0)
        {
            int newQuantity = owned.quantity - qty;
            if (newQuantity <= 0)
            {
                // 수량이 0 이하면 제거
                portfolio.RemoveAt(existingIndex);
            }
            else
            {
                // 수량 업데이트: SyncList 동기화를 위해 인덱서로 재할당
                portfolio[existingIndex] = new OwnedStock
                {
                    stockId = owned.stockId,
                    quantity = newQuantity,
                    averagePurchasePrice = owned.averagePurchasePrice
                };
            }
        }

        ServerAddHolding(stockId, -qty);
    }

    [Server]
    public void ServerLiquidateAllHoldings()
    {
        long realized = 0;
        for (int i = portfolio.Count - 1; i >= 0; i--)
        {
            var s = portfolio[i];
            double px = ServerMarketData.GetCurrentPrice(s.stockId);
            long proceed = (long)Math.Round(px * s.quantity * 100.0);
            realized += proceed;

            ServerSetHolding(s.stockId, 0);
            portfolio.RemoveAt(i);
        }

        if (realized != 0)
            ServerAddCash(realized);

        RecalculateTotalValuation();
    }

    [Server]
    public void RecalculateTotalValuation()
    {
        long cash = cashCents;
        long portfolioValue = 0;

        foreach (var stock in portfolio)
        {
            double currentPrice = ServerMarketData.GetCurrentPrice(stock.stockId);
            portfolioValue += (long)(currentPrice * stock.quantity * 100);
        }

        equityCents = cash + portfolioValue;
    }

    [Server]
    public double GetAveragePurchasePrice(string stockId)
    {
        var owned = portfolio.Find(p => p.stockId == stockId);
        return owned?.averagePurchasePrice ?? 100.0;
    }

    public int GetHoldingQuantity(string stockId)
    {
        return holdings.TryGetValue(stockId, out var qty) ? qty : 0;
    }

    [Server]
    public void ServerShowDefeatThenKick()
    {
        if (hp > 0) return;

        if (connectionToClient != null)
        {
            TargetShowDefeat(connectionToClient);
            StartCoroutine(CoKickAfterSeconds(3f));
        }
    }

    [Server]
    public void ServerShowVictory()
    {
        if (connectionToClient != null)
        {
            TargetShowVictory(connectionToClient);
        }
    }

    System.Collections.IEnumerator CoKickAfterSeconds(float sec)
    {
        float t = 0f;
        while (t < sec)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (connectionToClient != null)
            connectionToClient.Disconnect();
    }

    [TargetRpc]
    void TargetShowDefeat(NetworkConnectionToClient conn)
    {
        TryEnableCanvasByName("DefeatCanvas", true);
        TryEnableCanvasByTag("DefeatCanvas", true);
    }

    [TargetRpc]
    void TargetShowVictory(NetworkConnectionToClient conn)
    {
        TryEnableCanvasByName("VictoryCanvas", true);
        TryEnableCanvasByTag("VictoryCanvas", true);
    }

    void TryEnableCanvasByName(string name, bool state)
    {
        var go = GameObject.Find(name);
        if (go != null) go.SetActive(state);
    }

    void TryEnableCanvasByTag(string tag, bool state)
    {
        try
        {
            var gos = GameObject.FindGameObjectsWithTag(tag);
            foreach (var g in gos) g.SetActive(state);
        }
        catch { }
    }

    void OnCashSync(long oldValue, long newValue)         => OnCashChanged?.Invoke(oldValue, newValue);
    void OnEquitySync(long oldValue, long newValue)       => OnEquityChanged?.Invoke(oldValue, newValue);
    void OnHpSync(int oldValue, int newValue)             => OnHpChanged?.Invoke(oldValue, newValue);
    void OnImageIndexSync(int oldValue, int newValue)     => OnImageIndexChanged?.Invoke(oldValue, newValue);

    void Holdings_OnChange(SyncDictionary<string, int>.Operation op, string key, int value)
        => OnHoldingsChanged?.Invoke(op, key, value);

    void Portfolio_OnChange(SyncList<OwnedStock>.Operation op, int index, OwnedStock item)
    {
        Debug.Log($"[PlayerState] Portfolio 변경: {op} | {(item!=null?item.stockId:"null")}");
    }

    static string NormalizeSymbol(string s) => s.Trim().ToUpperInvariant();
}
