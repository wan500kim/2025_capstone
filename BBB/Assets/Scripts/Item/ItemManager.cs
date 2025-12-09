using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mirror;

/// <summary>
/// 아이템 시스템 관리
/// - 아이템 생성
/// - 플레이어에게 아이템 배포
/// - 아이템 선택 추적
/// - [추가] 선택된 아이템 아이콘을 대시보드에 반영
/// </summary>
public class ItemManager : NetworkBehaviour
{
    public static ItemManager Instance;

    // 아이템 선택 추적 (플레이어별)
    private Dictionary<PlayerState, bool> playerItemSelected = new Dictionary<PlayerState, bool>();
    
    // 플레이어별 제공된 아이템 선택지 저장 (랜덤 선택용)
    private Dictionary<PlayerState, ItemOption[]> playerItemOptions = new Dictionary<PlayerState, ItemOption[]>();
    
    // 플레이어별 이미 나온 아이템 추적 (중복 방지)
    private Dictionary<PlayerState, HashSet<string>> playerUsedItemIds = new Dictionary<PlayerState, HashSet<string>>();
    
    // 아이템 선택 횟수 추적
    private int itemSelectionCount = 0;
    private const int MAX_ITEM_SELECTIONS = 3;  // 총 3번만 선택

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void OnEnable()
    {
        // TimeManager의 페이즈 변경 이벤트 구독
        TimeManager.OnServerPhaseChanged += OnPhaseChanged;
    }

    void OnDisable()
    {
        // 이벤트 구독 해제
        TimeManager.OnServerPhaseChanged -= OnPhaseChanged;
    }

    /// <summary>
    /// TimeManager의 페이즈 변경 시 호출
    /// </summary>
    [Server]
    void OnPhaseChanged(GamePhase phase, int round)
    {
        if (phase == GamePhase.Prep)
        {
            // 준비 턴 시작 시 아이템 배포 (라운드 1, 2, 3의 Prep에서만)
            Debug.Log($"[ItemManager] Prep 페이즈 시작 - 라운드 {round}");
            // PlayerState가 모두 등록될 때까지 약간의 지연
            StartCoroutine(DelayedDistributeItems(round));
            
        }
        else if (phase == GamePhase.Round)
        {
            // 라운드 시작 시 미선택 플레이어 처리
            Debug.Log($"[ItemManager] Round 페이즈 시작 - 라운드 {round}");
            AutoSelectAndCloseUI();
        }
    }

    /// <summary>
    /// 약간의 지연 후 아이템 배포 (PlayerState 등록 대기)
    /// </summary>
    [Server]
    System.Collections.IEnumerator DelayedDistributeItems(int round)
    {
        yield return new WaitForSeconds(0.5f); // 0.5초 대기
        Debug.Log($"[ItemManager] 지연 후 아이템 배포 시작 - PlayerState.All.Count: {PlayerState.All.Count}");
        DistributeItemsToAllPlayers(round);
    }

    /// <summary>
    /// 아이템 생성 - 랜덤으로 3개 선택 (플레이어별 중복 제외)
    /// </summary>
    [Server]
    public ItemOption[] GenerateRandomItems(PlayerState player)
    {
        // 플레이어별 사용된 아이템 목록 초기화
        if (!playerUsedItemIds.ContainsKey(player))
        {
            playerUsedItemIds[player] = new HashSet<string>();
        }
        
        // 아이템 풀 정의 (iconPath는 Resources 폴더 기준 경로)
        var itemPool = new List<ItemOption>
        {
            new ItemOption("hpbonus", "HP보너스", "남은 HP에 반비례하여 주식 판매 시 보너스", ItemEffect.HPSellBonus, 100, "item/9"),
            new ItemOption("triple", "많은 달걀", "3가지 이상의 주식을 가졌을 때 주식 판매 시 보너스", ItemEffect.triple, 100, "item/2"),
            new ItemOption("lowstack", "데드캣 바운스", "한 종목의 주식을 구매할 때 이전 구매 금액보다 작을 경우 1스택. 3스택 시 주식 판매 시 보너스. 스택 초기화", ItemEffect.lowStack, 100, "item/4"),
            new ItemOption("money", "충분한 총알", "라운드 시작 시 초기 자본금 +10%", ItemEffect.money, 10, "item/12"),
            new ItemOption("stay", "가치 구매", "주식을 일정 시간 이상 보유했다면 판미 시 보너스", ItemEffect.stay, 100, "item/1"),
            new ItemOption("dividend", "황금알을 낳는 거위", "주식을 일정 시간 보유했다면 배당금 획득", ItemEffect.dividend, 100, "item/14"),
            new ItemOption("hightstack", "달리는 말에 올라타라", "한 종목의 주식을 구매할 때 이전 구매 금액보다 클 경우 주식 판매 시 보너스", ItemEffect.hightStack, 100, "item/3"),
            new ItemOption("deficit", "베어마켓", "구매 가격보다 판매 가격이 낮을 경우 주식 판매 시 보너스", ItemEffect.deficit, 100, "item/13"),
            new ItemOption("shortSell", "스켈핑 트레이더", "주식 구매 이후 일정 시간 내에 판매 시 보너스", ItemEffect.shortSell, 100, "item/8"),
            new ItemOption("timebonus", "산타렐리", "거래 턴이 얼마 남지 않았을 때 판매 시 보너스", ItemEffect.timeBonus, 100, "item/6"),
            new ItemOption("health", "마음 다스리기", "HP 회복'주식은 심리전이다. 자신을 다스려라'", ItemEffect.health, 25, "item/5"),
            new ItemOption("playerBonus", "제로섬 게임", "남은 개미에 반비례하여 주식 판매 시 보너스", ItemEffect.playerBonus, 100, "item/11"),
            new ItemOption("noBuyDiscount", "쉬는 것도 투자", "아무 주식도 구매하지 않고 20초가 지나면 구매 금액 10% 감소, 주식 구매 후 효과 종료", ItemEffect.noBuyDiscount, 10, "item/7"),
            new ItemOption("reverseTrade", "장미를 원한다면 가시를 조심해", "구매가보다 낮게 팔면 판매 금액 35% 증가, 높게 팔면 판매 금액 15% 감소", ItemEffect.reverseTrade, 5, "item/10")
        };
        
        // 해당 플레이어가 이미 받은 아이템 제외
        var playerUsedIds = playerUsedItemIds[player];
        var availableItems = itemPool.Where(item => !playerUsedIds.Contains(item.id)).ToList();
        
        Debug.Log($"[ItemManager] [{player.playerName}] 사용 가능한 아이템: {availableItems.Count}개 (전체: {itemPool.Count}개, 사용됨: {playerUsedIds.Count}개)");
        
        if (availableItems.Count < 3)
        {
            Debug.LogWarning($"[ItemManager] [{player.playerName}] 사용 가능한 아이템이 3개 미만! 아이템 풀 초기화");
            playerUsedIds.Clear();  // 해당 플레이어의 사용된 아이템 목록 초기화
            availableItems = itemPool;
        }
        
        // 랜덤으로 3개 선택
        var selected = availableItems.OrderBy(x => UnityEngine.Random.value).Take(3).ToArray();
        
        // 선택된 아이템을 해당 플레이어의 사용 목록에 추가
        foreach (var item in selected)
        {
            if (!playerUsedIds.Contains(item.id))
            {
                playerUsedIds.Add(item.id);
                Debug.Log($"[ItemManager] [{player.playerName}] '{item.id}' 아이템을 사용 목록에 추가");
            }
            else
            {
                Debug.LogWarning($"[ItemManager] [{player.playerName}] '{item.id}' 아이템이 이미 사용 목록에 있습니다!");
            }
        }
        
        Debug.Log($"[ItemManager] [{player.playerName}] 선택된 아이템: [{selected[0].displayName}, {selected[1].displayName}, {selected[2].displayName}]");
        Debug.Log($"[ItemManager] [{player.playerName}] 현재 사용된 아이템 목록: {string.Join(", ", playerUsedIds)}");
        
        return selected;
    }

    /// <summary>
    /// Prep 페이즈 시작 시 모든 플레이어에게 아이템 배포 (총 3번)
    /// 라운드 1, 2, 3 후의 Prep 페이즈에서 실행
    /// </summary>
    [Server]
    public void DistributeItemsToAllPlayers(int currentRound)
    {
        // 라운드 1, 2, 3 후의 Prep 페이즈에서만 실행 (총 3번)
        if (currentRound < 1 || currentRound > 3)
        {
            Debug.Log($"[ItemManager] 아이템 배포 스킵 (Round: {currentRound}, 아이템은 라운드 1~3 후에만 배포)");
            return;
        }
        
        if (itemSelectionCount >= MAX_ITEM_SELECTIONS)
        {
            Debug.Log($"[ItemManager] 아이템 배포 스킵 (이미 {MAX_ITEM_SELECTIONS}번 배포 완료)");
            return;
        }
        
        itemSelectionCount++;
        Debug.Log($"[ItemManager] 아이템 선택 {itemSelectionCount}/{MAX_ITEM_SELECTIONS}번째 (라운드 {currentRound} 후)");
        
        playerItemSelected.Clear();
        
        // 각 플레이어에게 서로 다른 랜덤 아이템 선택지 제공
        int playerIndex = 0;
        int playerCount = PlayerState.All.Count;
        
        Debug.Log($"[ItemManager] PlayerState.All.Count: {playerCount}");
        
        if (playerCount == 0)
        {
            Debug.LogError("[ItemManager] PlayerState.All이 비어있습니다! 플레이어가 게임에 참여하지 않았거나 PlayerState가 제대로 등록되지 않았습니다.");
            return;
        }
        
        foreach (var player in PlayerState.All)
        {
            playerIndex++;
            Debug.Log($"[ItemManager] [{playerIndex}/{playerCount}] {player.playerName}에게 아이템 생성 중...");
            
            var items = GenerateRandomItems(player);  // 각 플레이어마다 다른 선택지 (플레이어별 중복 제외)
            
            // 클라이언트에게 아이템 선택지 전송 (RPC 사용)
            Debug.Log($"[ItemManager] [{playerIndex}/{playerCount}] {player.playerName}의 connectionToClient: {(player.connectionToClient != null ? "존재" : "NULL")}");
            
            if (player.connectionToClient != null)
            {
                TargetSendItemOptions(player.connectionToClient, items);
                playerItemSelected[player] = false;
                playerItemOptions[player] = items;  // 제공된 아이템 저장 (랜덤 선택용)
                Debug.Log($"[ItemManager] [{playerIndex}/{playerCount}] {player.playerName}에게 아이템 전송 완료: [{items[0].displayName}, {items[1].displayName}, {items[2].displayName}]");
            }
            else
            {
                Debug.LogError($"[ItemManager] [{playerIndex}/{playerCount}] {player.playerName}의 connectionToClient가 null입니다!");
            }
        }
    }

    /// <summary>
    /// 플레이어가 아이템을 선택했음을 기록
    /// </summary>
    [Server]
    public void OnPlayerSelectedItem(PlayerState player)
    {
        if (playerItemSelected.ContainsKey(player))
        {
            playerItemSelected[player] = true;
            Debug.Log($"[ItemManager] {player.playerName}이(가) 아이템 선택 완료");
        }
    }

    /// <summary>
    /// 미선택 플레이어에게 랜덤 아이템 자동 선택 및 UI 닫기
    /// </summary>
    [Server]
    void AutoSelectAndCloseUI()
    {
        if (playerItemSelected.Count == 0)
        {
            Debug.Log("[ItemManager] 아이템 선택이 활성화되지 않은 라운드 - 자동 선택 스킵");
            return;
        }
        
        foreach (var kvp in playerItemSelected.ToList())
        {
            if (!kvp.Value) // 선택하지 않은 플레이어
            {
                var player = kvp.Key;
                
                // 해당 플레이어에게 제공된 아이템 중에서 랜덤 선택
                if (playerItemOptions.ContainsKey(player) && playerItemOptions[player] != null)
                {
                    var items = playerItemOptions[player];
                    ItemOption randomItem = items[UnityEngine.Random.Range(0, items.Length)];
                    
                    Debug.Log($"[ItemManager] {player.playerName}이(가) 시간 내 선택하지 않아 자동 선택: {randomItem.displayName}");
                    
                    // 서버에서 직접 적용
                    randomItem.ApplyEffect(player);
                    
                    // 선택 완료 기록
                    playerItemSelected[player] = true;
                    
                    // 클라이언트에게 UI 닫기 + 대시보드 갱신 + 토스트 메시지 전송
                    if (player.connectionToClient != null)
                    {
                        TargetUpdateDashboard(player.connectionToClient, randomItem.iconPath);
                        TargetCloseItemUI(player.connectionToClient, randomItem.GetEffectMessage());
                    }
                }
            }
        }
    }

    /// <summary>
    /// [DEPRECATED] 이 메서드는 더 이상 사용되지 않습니다.
    /// AutoSelectAndCloseUI()를 사용하세요.
    /// </summary>
    [Server]
    public void ApplyRandomItemToUnselectedPlayers()
    {
        Debug.LogWarning("[ItemManager] ApplyRandomItemToUnselectedPlayers()는 더 이상 사용되지 않습니다. AutoSelectAndCloseUI()를 사용하세요.");
        AutoSelectAndCloseUI();
    }
    
    /// <summary>
    /// 게임 종료 시 모든 데이터 초기화
    /// </summary>
    [Server]
    public void ResetItemSystem()
    {
        playerItemSelected.Clear();
        playerItemOptions.Clear();
        playerUsedItemIds.Clear();
        itemSelectionCount = 0;
        
        Debug.Log("[ItemManager] 아이템 시스템 초기화");
    }

    /// <summary>
    /// 클라이언트에게 아이템 선택지 전송 (TargetRpc)
    /// </summary>
    [TargetRpc]
    void TargetSendItemOptions(NetworkConnection target, ItemOption[] items)
    {
        Debug.Log($"[ItemManager - CLIENT] TargetSendItemOptions 호출됨! 아이템 개수: {items?.Length ?? 0}");
        
        // LocalUiBus를 통해 UI에 전달
        if (LocalUiBus.OnItemOptions != null)
        {
            Debug.Log($"[ItemManager - CLIENT] LocalUiBus.OnItemOptions 이벤트 발생! 구독자 수: {LocalUiBus.OnItemOptions.GetInvocationList().Length}");
            LocalUiBus.OnItemOptions.Invoke(items);
            Debug.Log($"[ItemManager - CLIENT] 아이템 선택지 전송 완료: {items[0].displayName}, {items[1].displayName}, {items[2].displayName}");
        }
        else
        {
            Debug.LogError("[ItemManager - CLIENT] LocalUiBus.OnItemOptions가 null입니다! ItemSelectionUI가 활성화되어 있는지 확인하세요!");
        }
    }

    /// <summary>
    /// 클라이언트에게 토스트 메시지 전송 (TargetRpc)
    /// </summary>
    [TargetRpc]
    void TargetShowToast(NetworkConnection target, string message)
    {
        // LocalUiBus를 통해 UI에 전달
        if (LocalUiBus.OnToast != null)
        {
            LocalUiBus.OnToast.Invoke(message);
            Debug.Log($"[ItemManager] 토스트 메시지 전송: {message}");
        }
    }
    
    /// <summary>
    /// 클라이언트에게 아이템 UI 닫기 + 토스트 메시지 전송 (TargetRpc)
    /// </summary>
    [TargetRpc]
    void TargetCloseItemUI(NetworkConnection target, string message)
    {
        Debug.Log($"[ItemManager - CLIENT] 자동 선택으로 UI 닫기: {message}");
        
        // 토스트 메시지 표시
        if (LocalUiBus.OnToast != null)
        {
            LocalUiBus.OnToast.Invoke($"[자동 선택] {message}");
        }
        
        // ItemSelectionUI 찾아서 강제로 닫기
        var itemUI = FindObjectOfType<ItemSelectionUI>();
        if (itemUI != null)
        {
            itemUI.ForceClosePanel();
            Debug.Log("[ItemManager - CLIENT] ItemSelectionUI.ForceClosePanel() 호출 완료");
        }
        else
        {
            Debug.LogWarning("[ItemManager - CLIENT] ItemSelectionUI를 찾을 수 없습니다!");
        }
    }

    /// <summary>
    /// 선택 패널 강제 닫기 (TargetRpc)
    /// </summary>
    [TargetRpc]
    void TargetCloseItemPanel(NetworkConnection target)
    {
        var ui = GameObject.FindObjectOfType<ItemSelectionUI>();
        if (ui != null)
        {
            ui.ForceClosePanel();
        }
        else
        {
            Debug.LogWarning("[ItemManager - CLIENT] ItemSelectionUI를 찾을 수 없어 패널 강제 닫기 실패");
        }
    }

    /// <summary>
    /// [추가] 대시보드의 아이템 슬롯을 갱신하도록 클라이언트에 지시
    /// </summary>
    [TargetRpc]
    void TargetUpdateDashboard(NetworkConnection target, string iconPath)
    {
        var dashboard = FindObjectOfType<PlayerDashboard>();
        if (dashboard != null)
        {
            dashboard.AddItemIconToDashboard(iconPath);
            Debug.Log($"[ItemManager - CLIENT] PlayerDashboard 아이템 아이콘 반영: {iconPath}");
        }
        else
        {
            Debug.LogWarning("[ItemManager - CLIENT] PlayerDashboard를 찾을 수 없습니다. 아이콘 반영 실패");
        }
    }

    /// <summary>
    /// 클라이언트로부터 아이템 선택 수신 (Command)
    /// </summary>
    [Command(requiresAuthority = false)]
    public void CmdSelectItem(ItemOption selectedItem, NetworkConnectionToClient sender = null)
    {
        // sender로부터 PlayerState 찾기
        PlayerState player = null;
        foreach (var p in PlayerState.All)
        {
            if (p.connectionToClient == sender)
            {
                player = p;
                break;
            }
        }

        if (player == null)
        {
            Debug.LogWarning("[ItemManager] CmdSelectItem: PlayerState를 찾을 수 없습니다!");
            return;
        }

        Debug.Log($"[ItemManager] {player.playerName}이(가) 아이템 선택: {selectedItem.displayName}");

        // 아이템 효과 적용
        selectedItem.ApplyEffect(player);

        // 선택 완료 기록
        OnPlayerSelectedItem(player);

        // 클라이언트 UI 갱신: 대시보드 아이템 아이콘 추가, 토스트, 패널 닫기
        if (sender != null)
        {
            TargetUpdateDashboard(sender, selectedItem.iconPath);
            TargetShowToast(sender, selectedItem.GetEffectMessage());
            TargetCloseItemPanel(sender);
        }
    }
}
