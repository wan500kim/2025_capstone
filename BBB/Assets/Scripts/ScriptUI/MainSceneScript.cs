using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

//
// MainMenuController.cs
// - UI Toolkit 버튼 바인딩
// - btn_start → room 씬 로드
// - btn_quit  → 게임 종료 (에디터에서는 플레이 중지)
//
[RequireComponent(typeof(UIDocument))]
public class MainMenuController : MonoBehaviour
{
    [Header("Scene")]
    [Tooltip("시작 버튼이 로드할 씬 이름(빌드 세팅에 포함되어 있어야 합니다).")]
    public string roomSceneName = "room";

    UIDocument ui;
    Button btnStart;
    Button btnQuit;

    void Awake()
    {
        ui = GetComponent<UIDocument>();
        if (ui == null)
        {
            Debug.LogError("[MainMenuController] UIDocument가 없습니다.");
        }
    }

    void OnEnable()
    {
        if (ui == null) return;

        var root = ui.rootVisualElement;
        btnStart = root.Q<Button>("btn_start");
        btnQuit  = root.Q<Button>("btn_quit");

        if (btnStart == null) Debug.LogError("[MainMenuController] 'btn_start'를 찾지 못했습니다.");
        if (btnQuit  == null) Debug.LogError("[MainMenuController] 'btn_quit'를 찾지 못했습니다.");

        if (btnStart != null) btnStart.clicked += OnClickStart;
        if (btnQuit  != null) btnQuit.clicked  += OnClickQuit;
    }

    void OnDisable()
    {
        if (btnStart != null) btnStart.clicked -= OnClickStart;
        if (btnQuit  != null) btnQuit.clicked  -= OnClickQuit;
    }

    void OnClickStart()
    {
        if (string.IsNullOrEmpty(roomSceneName))
        {
            Debug.LogError("[MainMenuController] roomSceneName이 비어 있습니다.");
            return;
        }

        // 빌드 세팅에 씬이 포함되어 있어야 합니다.
        SceneManager.LoadScene(roomSceneName, LoadSceneMode.Single);
    }

    void OnClickQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
