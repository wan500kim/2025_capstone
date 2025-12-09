using UnityEngine;

public class PanelToggler : MonoBehaviour
{
    [Header("패널 레퍼런스")]
    [SerializeField] GameObject namePanel;   // 이름 입력 패널
    [SerializeField] GameObject lobbyPanel;  // 멀티룸 패널

    // 이름 입력 패널 보여주기, 로비 숨기기
    public void ShowNamePanel()
    {
        if (namePanel)  namePanel.SetActive(true);
        if (lobbyPanel) lobbyPanel.SetActive(false);
    }

    // 로비 패널 보여주기, 이름 입력 숨기기
    public void ShowLobbyPanel()
    {
        if (namePanel)  namePanel.SetActive(false);
        if (lobbyPanel) lobbyPanel.SetActive(true);
    }

    // (옵션) 아무 두 패널을 직접 지정해서 전환하고 싶을 때 버튼에서 사용
    public void Toggle(GameObject toEnable, GameObject toDisable)
    {
        if (toEnable)  toEnable.SetActive(true);
        if (toDisable) toDisable.SetActive(false);
    }
}