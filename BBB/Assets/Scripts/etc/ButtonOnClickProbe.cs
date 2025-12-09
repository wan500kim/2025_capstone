using UnityEngine;
using UnityEngine.UI;

public class ButtonOnClickProbe : MonoBehaviour
{
    void Awake()
    {
        var btn = GetComponent<Button>();
        if (!btn) { Debug.LogWarning("Button 없음"); return; }
        Debug.Log($"[BTN] interactable={btn.interactable}");
        btn.onClick.AddListener(() => Debug.Log("[BTN] onClick Fired!"));
    }
}
