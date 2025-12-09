using Mirror;
using UnityEngine;

public class ServerBootstrap : NetworkBehaviour
{
    public GameObject[] prefabsToSpawn;

    public override void OnStartServer()
    {
        foreach (var p in prefabsToSpawn)
            NetworkServer.Spawn(Instantiate(p));
    }
}

