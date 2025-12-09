using System.Collections;
using Mirror;
using UnityEngine;

public class AutoServerBootstrap : MonoBehaviour
{
    [Header("Editor/Standalone 테스트용")]
    [Tooltip("에디터나 일반 실행에서 서버 자동 시작(테스트에만 사용)")]
    public bool startServerInEditor = false;

    [Header("KCP 설정 (옵션)")]
    public ushort defaultPort = 7777;

    void Start()
    {
        StartCoroutine(BootRoutine());
    }

    IEnumerator BootRoutine()
    {
        // 네트워크 매니저가 준비될 때까지 1프레임 대기
        yield return null;

        var nm = NetworkManager.singleton;
        if (nm == null)
        {
            Debug.LogError("[AutoServerBootstrap] NetworkManager.singleton 이 없음");
            yield break;
        }

        // 포트/주소 커맨드라인 파싱(선택)
        ApplyTransportArgs();

        // 배치모드(헤드리스) 또는 -dedicated 인자 → 서버만 시작
        if (Application.isBatchMode || HasArg("-dedicated"))
        {
            if (!NetworkServer.active && !NetworkClient.active)
            {
                Debug.Log("[AutoServerBootstrap] Dedicated Server start");
                nm.StartServer();
            }
            yield break;
        }

        // 테스트 편의용: 에디터에서도 자동 서버 시작하고 싶을 때
        if (startServerInEditor && !NetworkServer.active && !NetworkClient.active)
        {
            Debug.Log("[AutoServerBootstrap] Editor/Standalone: StartServer()");
            nm.StartServer();
        }
    }

    // ───────────────────────────── 커맨드라인 유틸 ─────────────────────────────
    static bool HasArg(string name)
    {
        var args = System.Environment.GetCommandLineArgs();
        foreach (var a in args)
            if (a.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static string GetArg(string name, string fallback = null)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
            if (args[i].Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return (i + 1 < args.Length) ? args[i + 1] : fallback;
        return fallback;
    }

    void ApplyTransportArgs()
    {
        // KcpTransport를 쓰는 경우 포트 지정
        var kcp = Transport.active as kcp2k.KcpTransport;
        if (kcp != null)
        {
            // -port 9000 형태 지원
            var portStr = GetArg("-port");
            if (ushort.TryParse(portStr, out var p)) kcp.Port = p;
            else kcp.Port = defaultPort;

            // (옵션) -mtu, -recv, -send 등 추가 인자도 여기서 적용 가능
            Debug.Log($"[AutoServerBootstrap] KCP Port = {kcp.Port}");
        }

        // 다른 트랜스포트 사용 시에도 비슷하게 인자 적용
        // var tel = Transport.active as TelepathyTransport; ...
    }
}