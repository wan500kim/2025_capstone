using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class debugClick : MonoBehaviour
{
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            var list = new List<RaycastResult>();
            EventSystem.current.RaycastAll(
                new PointerEventData(EventSystem.current){ position = Input.mousePosition }, list);
            foreach (var r in list) Debug.Log($"[HIT] {r.gameObject.name}");
        }
    }
}

