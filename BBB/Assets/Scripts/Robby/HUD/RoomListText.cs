using Mirror;
using TMPro;
using UnityEngine;
using System.Linq;

public class RoomListText : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI listText;

    void Update()
    {
        if (!listText) return;
        var players = FindObjectsOfType<NetworkRoomPlayer>()
                      .OrderBy(p => p.index) // 슬롯 순서
                      .ToArray();
        System.Text.StringBuilder sb = new();
        foreach (var p in players)
            sb.AppendLine($"Slot {p.index}: {(p.readyToBegin ? "READY" : "NOT READY")}");
        listText.text = sb.Length > 0 ? sb.ToString() : "No players";
    }
}
