using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Collections;

public class CustomRoomHUD : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] TMP_InputField addressInput;
    [SerializeField] Button hostBtn, joinBtn, serverBtn, stopBtn;
    [SerializeField] Button readyBtn, unreadyBtn, startGameBtn;
    [SerializeField] TextMeshProUGUI statusText;

    NetworkRoomManager room;

    // ⬇️ Awake 제거 (여기서 싱글톤 만지지 않음)

    IEnumerator Start()
    {
        // 1) NetworkManager.singleton 생성될 때까지 대기
        yield return new WaitUntil(() => NetworkManager.singleton != null);

        // 2) 룸 매니저로 캐스팅 (커스텀 상속형도 OK)
        room = NetworkManager.singleton as NetworkRoomManager;
        if (!room)
        {
            // 혹시 씬에 직접 붙어있는 케이스
            room = FindObjectOfType<NetworkRoomManager>();
        }

        if (!room)
        {
            Debug.LogWarning("NetworkRoomManager를 찾지 못했습니다. Host/Join/Server/Stop만 사용합니다.");
        }

        // 3) 이제 리스너 연결 (room 준비 이후)
        if (hostBtn)      hostBtn.onClick.AddListener(OnHost);
        if (joinBtn)      joinBtn.onClick.AddListener(OnJoin);
        if (serverBtn)    serverBtn.onClick.AddListener(OnServerOnly);
        if (stopBtn)      stopBtn.onClick.AddListener(OnStop);
        if (readyBtn)     readyBtn.onClick.AddListener(() => SetReady(true));
        if (unreadyBtn)   unreadyBtn.onClick.AddListener(() => SetReady(false));
        if (startGameBtn) startGameBtn.onClick.AddListener(OnStartGame);
    }

    void OnDisable()
    {
        if (hostBtn)      hostBtn.onClick.RemoveListener(OnHost);
        if (joinBtn)      joinBtn.onClick.RemoveListener(OnJoin);
        if (serverBtn)    serverBtn.onClick.RemoveListener(OnServerOnly);
        if (stopBtn)      stopBtn.onClick.RemoveListener(OnStop);
        if (readyBtn)     readyBtn.onClick.RemoveAllListeners();
        if (unreadyBtn)   unreadyBtn.onClick.RemoveAllListeners();
        if (startGameBtn) startGameBtn.onClick.RemoveAllListeners();
    }

    void Update()
    {
        // room이 아직 없으면 UI만 기본 상태로 유지
        string s =
            (NetworkServer.active && NetworkClient.isConnected) ? "Host" :
            NetworkServer.active                                 ? "Server" :
            NetworkClient.isConnected                             ? "Client" :
            "Idle";
        if (statusText) statusText.text = $"State: {s}";

        bool connecting = NetworkClient.active && !NetworkClient.isConnected;
        bool idle       = !NetworkServer.active && !NetworkClient.active;

        if (hostBtn)      hostBtn.interactable      = idle;
        if (joinBtn)      joinBtn.interactable      = idle;
        if (serverBtn)    serverBtn.interactable    = idle;
        if (stopBtn)      stopBtn.interactable      = !idle || connecting;

        // 룸 기능은 room이 있을 때만
        var rp = GetLocalRoomPlayer();
        bool inRoom = (room != null) && NetworkClient.isConnected && rp != null && rp.isOwned;
        if (readyBtn)     readyBtn.gameObject.SetActive(room != null);
        if (unreadyBtn)   unreadyBtn.gameObject.SetActive(room != null);
        if (startGameBtn) startGameBtn.gameObject.SetActive(room != null);

        if (room != null)
        {
            if (readyBtn)     readyBtn.interactable     = inRoom && !rp.readyToBegin;
            if (unreadyBtn)   unreadyBtn.interactable   = inRoom &&  rp.readyToBegin;
            if (startGameBtn) startGameBtn.interactable = NetworkServer.active;
        }
    }

    // --- 버튼 핸들러 ---
    void OnHost()       { if (!room) return; ApplyAddress(); room.StartHost(); }
    void OnJoin()       { if (!room) return; ApplyAddress(); room.StartClient(); }
    void OnServerOnly() { if (!room) return; ApplyAddress(); room.StartServer(); }
    void OnStop()
    {
        if (!room) return;
        if (NetworkClient.activeHost) room.StopHost();
        else {
            if (NetworkClient.isConnected) room.StopClient();
            if (NetworkServer.active)      room.StopServer();
        }
    }

    void SetReady(bool ready)
    {
        if (!room) return;
        var rp = GetLocalRoomPlayer();
        if (!rp) return;
        rp.CmdChangeReadyState(ready);
    }

    void OnStartGame()
    {
        if (!room || !NetworkServer.active) return;
        room.ServerChangeScene(room.GameplayScene);
    }

    // --- 유틸 ---
    void ApplyAddress()
    {
        if (!room) return;
        if (addressInput && !string.IsNullOrWhiteSpace(addressInput.text))
            room.networkAddress = addressInput.text.Trim();
    }

    NetworkRoomPlayer GetLocalRoomPlayer()
    {
        if (!room) return null;
        return FindObjectsOfType<NetworkRoomPlayer>()
               .FirstOrDefault(p => p.isLocalPlayer);
    }
}

