using Mirror;
using TMPro;
using UnityEngine;

public class FixedConnectOnConfirm : MonoBehaviour
{
    [Header("고정 접속 설정")]
    [Tooltip("우리 서버의 도메인 또는 퍼블릭 IP")]
    public string fixedAddress = "127.0.0.1"; // 예: "13.124.xxx.xxx"
    [Tooltip("KCP/Telepathy 등 트랜스포트 포트(옵션)")]
    public ushort port = 7777;

    [Header("UI 참조(선택)")]
    public TMP_InputField nameInput;   // 플레이어 이름 입력칸
    public GameObject namePanel;       // 이름 입력 패널
    public GameObject connectingPanel; // '접속 중' 표시 패널(없으면 비워두기)

    [Header("개발용 옵션")]
    public bool startHostInsteadInEditor = false; // 에디터에서 호스트로 띄워보고 싶을 때

    public void OnClickConfirm()
    {
        // 2) NetworkRoomManager 가져오기
        var room = NetworkManager.singleton as NetworkRoomManager;
        if (room == null)
        {
            Debug.LogError("[FixedConnectOnConfirm] NetworkRoomManager가 씬에 없습니다.");
            return;
        }

        // 3) (옵션) 포트 적용: 사용 중인 트랜스포트에 맞춰 설정
        var kcp = Transport.active as kcp2k.KcpTransport;
        if (kcp != null) kcp.Port = port;
        // TelepathyTransport 쓰면:
        // var tel = Transport.active as TelepathyTransport;
        // if (tel != null) tel.port = port;

        // 4) 고정 주소로 접속
        room.networkAddress = fixedAddress;

#if UNITY_EDITOR
        if (startHostInsteadInEditor)
        {
            Debug.Log("[FixedConnectOnConfirm] (Editor) StartHost()");
            room.StartHost();
        }
        else
        {
            Debug.Log($"[FixedConnectOnConfirm] StartClient() -> {fixedAddress}:{port}");
            room.StartClient();
        }
#else
        Debug.Log($"[FixedConnectOnConfirm] StartClient() -> {fixedAddress}:{port}");
        room.StartClient();
#endif

        // 5) UI 전환(선택)
        if (namePanel)       namePanel.SetActive(false);
        if (connectingPanel) connectingPanel.SetActive(true);
    }
}
