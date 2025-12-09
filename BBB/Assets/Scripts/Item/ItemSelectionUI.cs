using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

/// <summary>
/// Ready 페이즈에서 아이템 선택 UI를 관리
/// 3개의 버튼 중 하나를 선택하면 서버에 전송
/// </summary>
public class ItemSelectionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Animator panelAnimator;  // 패널의 Animator 컴포넌트
    [SerializeField] private Button[] itemButtons = new Button[3];  // 이미지에 Button 컴포넌트 필요
    [SerializeField] private Image[] itemIcons = new Image[3];  // 아이템 아이콘 이미지
    [SerializeField] private TextMeshProUGUI[] itemNameTexts = new TextMeshProUGUI[3];  // 아이템 이름
    [SerializeField] private TextMeshProUGUI[] itemDescriptionTexts = new TextMeshProUGUI[3];  // 아이템 설명
    [SerializeField] private TextMeshProUGUI targetAmountText;

    private PlayerState localPlayer;
    private ItemOption[] currentItems;
    private bool itemSelected = false;

    void OnEnable()
    {
        Debug.Log("[ItemSelectionUI] OnEnable - 이벤트 구독 시작");
        LocalUiBus.OnItemOptions += ShowItemSelection;
        LocalUiBus.OnTargetAmount += UpdateTargetAmount;
        Debug.Log($"[ItemSelectionUI] OnEnable - LocalUiBus.OnItemOptions 구독 완료. 현재 구독자 수: {LocalUiBus.OnItemOptions?.GetInvocationList().Length ?? 0}");
    }

    void OnDisable()
    {
        Debug.Log("[ItemSelectionUI] OnDisable - 이벤트 구독 해제");
        LocalUiBus.OnItemOptions -= ShowItemSelection;
        LocalUiBus.OnTargetAmount -= UpdateTargetAmount;
    }

    void Start()
    {
        Debug.Log("[ItemSelectionUI] Start 호출됨!");
        
        // Panel이 연결되지 않았으면 자식 오브젝트 찾기 (자기 자신은 비활성화하면 안 됨!)
        if (panel == null)
        {
            // 자식 중에서 "Panel" 이름을 가진 오브젝트 찾기
            Transform panelTransform = transform.Find("Panel");
            if (panelTransform != null)
            {
                panel = panelTransform.gameObject;
                Debug.Log("[ItemSelectionUI] Panel을 자식에서 찾았습니다.");
            }
            else
            {
                Debug.LogError("[ItemSelectionUI] Panel을 찾을 수 없습니다! Inspector에서 Panel GameObject를 연결하거나 'Panel' 이름의 자식 오브젝트를 만드세요.");
                // 패널이 없으면 스크립트 자체는 활성화 상태 유지
                return;
            }
        }
        
        // Animator가 연결되지 않았으면 panel에서 찾기
        if (panelAnimator == null && panel != null)
        {
            panelAnimator = panel.GetComponent<Animator>();
            if (panelAnimator != null)
            {
                Debug.Log("[ItemSelectionUI] Panel Animator를 찾았습니다.");
            }
        }
        
        // 초기에는 패널 숨김 (스크립트가 있는 GameObject가 아닌 panel만 숨김)
        if (panel && panel != this.gameObject) 
        {
            panel.SetActive(false);
            Debug.Log("[ItemSelectionUI] Start - Panel 비활성화");
        }
        
        // Animator 초기 상태 설정 (애니메이션 자동 재생 방지)
        if (panelAnimator)
        {
            // 현재 상태 확인
            if (panelAnimator.GetCurrentAnimatorStateInfo(0).IsName("Show"))
            {
                Debug.LogWarning("[ItemSelectionUI] Start - Animator가 Show 상태입니다! Default State를 Idle로 변경하세요.");
            }
            
            // 강제로 Idle 상태로 설정 후 비활성화
            panelAnimator.Play("Idle", 0, 0f);  // Idle 상태로 강제 이동
            panelAnimator.Update(0f);  // 즉시 적용
            panelAnimator.enabled = false;  // Animator 비활성화
            Debug.Log("[ItemSelectionUI] Start - Animator 비활성화");
        }

        // 버튼 리스너 등록
        for (int i = 0; i < itemButtons.Length; i++)
        {
            int index = i; // 클로저 캡처 방지
            if (itemButtons[i])
            {
                itemButtons[i].onClick.AddListener(() => OnItemButtonClicked(index));
            }
        }
        
        // 로컬 플레이어 찾기 (지연 바인딩)
        Invoke(nameof(FindLocalPlayer), 1f);
    }
    
    void FindLocalPlayer()
    {
        Debug.Log($"[ItemSelectionUI] FindLocalPlayer 호출 - PlayerState.All.Count: {PlayerState.All.Count}");
        
        // 모든 PlayerState 중에서 isLocalPlayer가 true인 것 찾기
        int index = 0;
        foreach (var player in PlayerState.All)
        {
            index++;
            Debug.Log($"[ItemSelectionUI] PlayerState [{index}]: {player.playerName}, isLocalPlayer: {player.isLocalPlayer}, netId: {player.netId}");
            
            if (player.isLocalPlayer)
            {
                BindLocalPlayer(player);
                Debug.Log($"[ItemSelectionUI] 로컬 플레이어 발견: {player.playerName}");
                break;
            }
        }
        
        // 찾지 못한 경우 재시도
        if (localPlayer == null)
        {
            Debug.LogWarning($"[ItemSelectionUI] 로컬 플레이어를 찾지 못했습니다. PlayerState.All에 {PlayerState.All.Count}명 있음. 1초 후 재시도...");
            Invoke(nameof(FindLocalPlayer), 1f);
        }
    }

    void BindLocalPlayer(PlayerState player)
    {
        localPlayer = player;
        Debug.Log($"[ItemSelectionUI] 로컬 플레이어 바인딩: {player.playerName}");
        
        // targetAmount는 PlayerState에 없으므로 기본값 사용 또는 제거
        if (targetAmountText)
        {
            // 현재 자산 (equityCents)를 표시
            long currentAsset = player.EquityCents / 100; // 센트를 원으로 변환
            targetAmountText.text = $"현재 자산: {currentAsset}원";
        }
    }

    void ShowItemSelection(ItemOption[] items)
    {
        if (items == null || items.Length != 3)
        {
            Debug.LogWarning("[ItemSelectionUI] 아이템 배열이 null이거나 길이가 3이 아닙니다.");
            return;
        }

        currentItems = items;
        itemSelected = false;

        Debug.Log($"[ItemSelectionUI] 아이템 선택지 수신:");
        Debug.Log($"  - 옵션 0: {items[0].displayName} - {items[0].description}");
        Debug.Log($"  - 옵션 1: {items[1].displayName} - {items[1].description}");
        Debug.Log($"  - 옵션 2: {items[2].displayName} - {items[2].description}");

        // UI 업데이트
        for (int i = 0; i < 3; i++)
        {
            if (itemNameTexts[i])
            {
                itemNameTexts[i].text = items[i].displayName;
            }
            if (itemDescriptionTexts[i])
            {
                itemDescriptionTexts[i].text = items[i].description;
            }
            if (itemButtons[i])
            {
                itemButtons[i].interactable = true;
            }
            
            // 아이템 아이콘 로드 및 설정
            if (itemIcons[i] && !string.IsNullOrEmpty(items[i].iconPath))
            {
                Sprite iconSprite = Resources.Load<Sprite>(items[i].iconPath);
                if (iconSprite != null)
                {
                    itemIcons[i].sprite = iconSprite;
                    itemIcons[i].enabled = true;
                    Debug.Log($"[ItemSelectionUI] 아이템 {i} 아이콘 로드 성공: {items[i].iconPath}");
                }
                else
                {
                    Debug.LogWarning($"[ItemSelectionUI] 아이템 {i} 아이콘을 찾을 수 없습니다: {items[i].iconPath}");
                    itemIcons[i].enabled = false;
                }
            }
            else if (itemIcons[i])
            {
                itemIcons[i].enabled = false;
            }
        }

        // 패널 표시 (애니메이션 포함)
        if (panel) 
        {
            panel.SetActive(true);
            
            // Animator 활성화 및 Show 트리거 실행
            if (panelAnimator)
            {
                panelAnimator.enabled = true;  // Animator 활성화
                Debug.Log("[ItemSelectionUI] Animator 활성화 및 Show 트리거");
                panelAnimator.Play("Idle", 0, 0f);  // Idle 상태로 리셋
                panelAnimator.SetTrigger("Show");  // Show 애니메이션 트리거
            }
        }
        Debug.Log("[ItemSelectionUI] 아이템 선택 패널 표시됨");
    }

    void OnItemButtonClicked(int index)
    {
        if (itemSelected)
        {
            Debug.Log("[ItemSelectionUI] 이미 아이템을 선택했습니다.");
            return;
        }
        if (currentItems == null || index < 0 || index >= currentItems.Length)
        {
            Debug.LogWarning($"[ItemSelectionUI] 잘못된 인덱스: {index}");
            return;
        }
        if (localPlayer == null)
        {
            Debug.LogWarning("[ItemSelectionUI] localPlayer가 null입니다.");
            return;
        }

        // 선택한 아이템 정보
        ItemOption selectedItem = currentItems[index];
        
        Debug.Log("=== 아이템 선택됨 ===");
        Debug.Log($"[ItemSelectionUI] 선택한 아이템: {selectedItem.displayName}");
        Debug.Log($"[ItemSelectionUI] 효과 타입: {selectedItem.effectType}");
        Debug.Log($"[ItemSelectionUI] 효과 값: {selectedItem.value}");
        Debug.Log($"[ItemSelectionUI] 설명: {selectedItem.description}");
        Debug.Log("===================");

        // ItemManager를 통해 서버에 전송
        if (ItemManager.Instance != null)
        {
            // Command는 클라이언트에서 호출하면 자동으로 서버로 전송됨
            ItemManager.Instance.CmdSelectItem(selectedItem);
        }
        else
        {
            Debug.LogError("[ItemSelectionUI] ItemManager.Instance가 null입니다!");
        }

        itemSelected = true;

        // 버튼 비활성화
        foreach (var btn in itemButtons)
        {
            if (btn) btn.interactable = false;
        }

        // 선택 피드백
        if (itemNameTexts[index])
        {
            itemNameTexts[index].text += " ✓";
        }

        Debug.Log("[ItemSelectionUI] 아이템 선택 UI를 숨깁니다.");
        
        // 즉시 패널 숨김
        HidePanel();
    }

    // TurnManager 의존성 제거 - 필요시 다른 방법으로 페이즈 변경 감지

    void UpdateTargetAmount(int newAmount)
    {
        if (targetAmountText)
        {
            targetAmountText.text = $"현재 자산: {newAmount}원";
        }
    }

    void HidePanel()
    {
        if (panel) 
        {
            // Animator가 있으면 Hide 트리거 실행 후 비활성화
            if (panelAnimator)
            {
                panelAnimator.SetTrigger("Hide");
                // 애니메이션 재생 시간만큼 대기 후 비활성화 (예: 0.3초)
                Invoke(nameof(DeactivatePanel), 0.3f);
            }
            else
            {
                panel.SetActive(false);
            }
        }
    }
    
    void DeactivatePanel()
    {
        if (panel) panel.SetActive(false);
    }

    /// <summary>
    /// 서버에서 강제 닫기 요청이 왔을 때 즉시 닫기
    /// </summary>
    public void ForceClosePanel()
    {
        Debug.Log("[ItemSelectionUI] ForceClosePanel 호출됨 -> 패널 닫기 시작");
        
        // 버튼 인터랙션 잠금
        foreach (var btn in itemButtons)
        {
            if (btn) btn.interactable = false;
        }
        
        // 선택 완료 플래그 설정
        itemSelected = true;
        
        // 패널 숨기기
        HidePanel();
    }
    
    /// <summary>
    /// 외부에서 패널을 숨기기 위한 public 메서드
    /// </summary>
    public void HidePanelPublic()
    {
        HidePanel();
    }
}
