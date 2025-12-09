using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

public class HudBinder : MonoBehaviour
{
    [SerializeField] Image localHeadIcon; // 씬의 Image (인스펙터 연결)

    IEnumerator Start()
    {
        // 로컬 플레이어 준비될 때까지 대기
        yield return new WaitUntil(() => NetworkClient.active && NetworkClient.localPlayer != null);

        var me = NetworkClient.localPlayer.GetComponent<PlayerState>(); // 게임 씬
        if (me != null) me.BindHeadIcon(localHeadIcon); // ★ 런타임 주입!
    }
}
