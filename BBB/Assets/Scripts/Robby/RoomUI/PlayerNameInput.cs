using TMPro;
using UnityEngine;

public class PlayerNameInput : MonoBehaviour
{
    [SerializeField] TMP_InputField input;

    void Start()
    {
        if (input) input.text = NameStore.Current; // 이전 값 채우기
    }

    public void OnClickSave()
    {
        if (!input) return;
        NameStore.Current = input.text; // 저장
    }
}