using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RebootOnLoad : NetworkBehaviour
{
    [Header("리셋 후 돌아갈 씬 이름(로비/부트 씬)")]
    public string bootSceneName = "Boot";

    [Header("리셋 직후 서버 재가동 여부")]
    public bool autoRestart = true;

    void Start()
    {
        // ReBoot 씬 입장 즉시 서버만 실행
        if (isServer) StartCoroutine(HardResetRoutine());
    }

    IEnumerator HardResetRoutine()
    {
        // 1) 접속한 클라이언트들에게 “종료/타이틀 이동” 안내
        foreach (var kv in NetworkServer.connections)
        {
            var conn = kv.Value;
            if (conn != null && conn.isAuthenticated)
                TargetReturnToTitle(conn, "세션이 종료되었습니다.", bootSceneName);
        }

        // UI 보여줄 약간의 여유(선택)
        yield return new WaitForSeconds(0.25f);

        // 2) 서버 네트워크 정지(모든 연결 종료)
        var nm = NetworkManager.singleton;
        if (nm != null)
        {
            if (NetworkClient.isConnected || NetworkClient.active) nm.StopHost(); // 호스트인 경우
            else if (NetworkServer.active)                         nm.StopServer(); // 전용 서버인 경우
        }

        // 트랜스포트가 완전히 내려가도록 한 틱 대기
        yield return null;
        yield return new WaitForSeconds(0.25f);

        // 3) (옵션) 부트/로비 씬 로드 후 서버 재가동
        if (autoRestart)
        {
            SceneManager.LoadScene(bootSceneName);
            // 씬 전환 한 프레임 대기
            yield return null;

            nm = NetworkManager.singleton;
            if (nm != null && !NetworkServer.active && !NetworkClient.active)
            {
#if UNITY_SERVER
                nm.StartServer(); // 전용 서버 빌드
#else
                nm.StartHost();   // 에디터/호스트 시연
#endif
            }
        }
        // autoRestart=false면 여기서 서버 완전 정지 상태로 끝.
    }

    // 각 클라이언트를 안전하게 타이틀/부트 씬으로 돌려보냄
    [TargetRpc]
    void TargetReturnToTitle(NetworkConnection target, string message, string clientScene)
    {
        Debug.Log(message);

        if (NetworkClient.isConnected) NetworkClient.Disconnect();
        if (NetworkClient.active && NetworkManager.singleton != null)
            NetworkManager.singleton.StopClient();

        // 클라이언트가 볼 씬으로 이동(원하면 "Title" 등 고정 이름으로 교체)
        SceneManager.LoadScene(clientScene);
        // 완전 종료하고 싶으면 Application.Quit();
    }
}
