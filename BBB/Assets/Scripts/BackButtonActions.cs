using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BackButtonActions : MonoBehaviour
{
    [Header("▶ 1) 정해진 씬으로 전환")]
    [Tooltip("버튼이 눌렸을 때 이동할 씬 이름")]
    public string targetScene;

    // 인스펙터에 scene 이름을 넣어두고 연결해서 쓰는 기본형
    public void BackToScene()
    {
        if (string.IsNullOrWhiteSpace(targetScene))
        {
            Debug.LogWarning("[BackButtonActions] targetScene이 비어있습니다.");
            return;
        }
        SceneManager.LoadScene(targetScene);
    }

    // 버튼 OnClick에 문자열 매개변수로 직접 씬 이름을 넘겨 쓰고 싶은 경우
    public void BackToScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[BackButtonActions] sceneName 인자가 비었습니다.");
            return;
        }
        SceneManager.LoadScene(sceneName);
    }


    [Header("▶ 2) 로비 연결 끊고 패널 전환")]
    [Tooltip("비활성화할 패널 (옵션, 비워도 됨)")]
    public GameObject panelToDisable;
    [Tooltip("활성화할 패널 (옵션, 비워도 됨)")]
    public GameObject panelToEnable;

    // 인스펙터에 패널을 미리 연결해두고 쓰는 기본형
    public void DisconnectAndSwitch()
    {
        // UI 먼저 전환(즉시 화면 반응)
        if (panelToDisable) panelToDisable.SetActive(false);
        if (panelToEnable)  panelToEnable.SetActive(true);

        // Mirror 연결 정리
        var nm = NetworkManager.singleton;
        if (nm == null) return;

        if (NetworkClient.activeHost)
        {
            nm.StopHost();
        }
        else
        {
            if (NetworkClient.isConnected) NetworkClient.Disconnect();
            if (NetworkClient.active)      nm.StopClient();
            if (NetworkServer.active)      nm.StopServer(); // 혹시 서버였던 경우 대비
        }
    }

    // 버튼 OnClick에 두 개의 패널을 직접 넘겨서 쓰고 싶은 경우
    public void DisconnectAndSwitch(GameObject toDisable, GameObject toEnable)
    {
        if (toDisable) toDisable.SetActive(false);
        if (toEnable)  toEnable.SetActive(true);

        var nm = NetworkManager.singleton;
        if (nm == null) return;

        if (NetworkClient.activeHost)
        {
            nm.StopHost();
        }
        else
        {
            if (NetworkClient.isConnected) NetworkClient.Disconnect();
            if (NetworkClient.active)      nm.StopClient();
            if (NetworkServer.active)      nm.StopServer();
        }
    }
}