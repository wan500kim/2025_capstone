#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Mirror;
using UnityEngine;
using System.Collections;

public class debug : MonoBehaviour
{
    static string PathOf(Transform t){
        var p=t.name;
        while(t.parent){ t=t.parent; p=t.name+"/"+p; }
        return p;
    }
    IEnumerator Start() {
        yield return new WaitForEndOfFrame(); // 씬 등록 완료 대기
        foreach (var ni in FindObjectsOfType<NetworkIdentity>(true)) {
            Debug.Log($"[NI] {ni.gameObject.name} sceneId={ni.sceneId} assetId={ni.assetId}");
        }
        foreach (var ni in FindObjectsOfType<NetworkIdentity>(true))
        {
            if (ni.sceneId != 0)
                Debug.Log($"[NI-{(NetworkServer.active? "SERVER":"CLIENT")}] {PathOf(ni.transform)} sceneId=0x{ni.sceneId:X16} active={ni.gameObject.activeInHierarchy}");
        }
        
    }
}

#endif
