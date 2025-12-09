using System.IO;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MyRoomManager : NetworkRoomManager
{
    [Header("Optional Fallback (디버그용)")]
    [Tooltip("base가 null을 반환할 때 임시로 수동 생성에 사용할 GamePlayer 프리팹(선택).")]
    [SerializeField] private GameObject fallbackGamePlayerPrefab;

    public static MyRoomManager Instance { get; private set; }

    [Header("Avatars (index 0..3)")]
    [Tooltip("로비 미리보기용 스프라이트 배열(선택). 실제 게임에서는 Resources/robby_image/player_{animal}.png 사용 권장")]
    public Sprite[] avatarSprites = new Sprite[4];

    // 서버에서만: 점유 중인 아바타/슬롯
    private readonly HashSet<int> takenAvatarIds = new();
    private readonly HashSet<int> takenSlots     = new();

    // 아바타 id ↔ 동물 키 매핑
    public static readonly string[] AvatarAnimals = { "cat", "dog", "hamster", "rabbit" };

    public static string AvatarIdToAnimal(int id)
    {
        if (id >= 0 && id < AvatarAnimals.Length) return AvatarAnimals[id];
        return "";
    }

    public override void Awake()
    {
        base.Awake();

        showRoomGUI = false;

        RoomScene     = NormalizeSceneName(RoomScene);
        GameplayScene = NormalizeSceneName(GameplayScene);

        autoCreatePlayer = true;

        Instance = this;
        DumpConfig("[Awake]");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 슬롯 배정/반환
    // ─────────────────────────────────────────────────────────────────────────────
    [Server]
    public int AssignSlot(MyRoomPlayer p)
    {
        for (int i = 0; i < 4; i++)
        {
            if (!takenSlots.Contains(i))
            {
                takenSlots.Add(i);
                p.slotIndex = i;   // SyncVar
                return i;
            }
        }
        p.slotIndex = -1;
        return -1;
    }

    [Server]
    public void ReleaseSlot(MyRoomPlayer p)
    {
        if (p.slotIndex >= 0)
        {
            takenSlots.Remove(p.slotIndex);
            p.slotIndex = -1;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 아바타 배정/반환
    // ─────────────────────────────────────────────────────────────────────────────
    [Server]
    public int AssignRandomAvatar(MyRoomPlayer p)
    {
        // 사용 가능한 id 수집
        List<int> free = new();
        for (int i = 0; i < AvatarAnimals.Length; i++)
            if (!takenAvatarIds.Contains(i)) free.Add(i);

        if (free.Count == 0)
        {
            Debug.LogWarning("[MyRoomManager] 사용 가능한 아바타가 없습니다.");
            p.avatarId = -1;
            p.avatarAnimal = "";
            return -1;
        }

        int pick = free[Random.Range(0, free.Count)];
        takenAvatarIds.Add(pick);
        p.avatarId = pick;                              // SyncVar
        p.avatarAnimal = AvatarIdToAnimal(pick);        // SyncVar
        return pick;
    }

    [Server]
    public void ReleaseAvatar(MyRoomPlayer p)
    {
        if (p.avatarId >= 0)
        {
            takenAvatarIds.Remove(p.avatarId);
            p.avatarId = -1;
            p.avatarAnimal = "";
        }
    }

    public override void OnRoomServerDisconnect(NetworkConnectionToClient conn)
    {
        var rp = conn.identity ? conn.identity.GetComponent<MyRoomPlayer>() : null;
        if (rp != null)
        {
            ReleaseAvatar(rp);
            ReleaseSlot(rp);
        }
        base.OnRoomServerDisconnect(conn);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 씬 전환
    // ─────────────────────────────────────────────────────────────────────────────
    public override void OnRoomServerPlayersReady()
    {
        // 모든 룸 플레이어가 준비되면 게임 씬으로 전환
        ServerChangeScene(GameplayScene);
    }

    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);
        Debug.Log($"[MyRoomManager] OnServerSceneChanged: {sceneName} (Active={SceneManager.GetActiveScene().name})");
    }

    // GamePlayer 생성(서버): 룸의 데이터를 게임 플레이어(PlayerState)로 복사
    public override GameObject OnRoomServerCreateGamePlayer(NetworkConnectionToClient conn, GameObject roomPlayerObj)
    {
        var active = SceneManager.GetActiveScene().name;
        if (!string.Equals(active, GameplayScene, System.StringComparison.Ordinal))
        {
            // 아직 게임 씬이 아니면 Mirror 기본 로직에 따라 null 반환
            Debug.LogWarning("[MyRoomManager] GameplayScene이 아닙니다. GamePlayer 생성 생략");
            return null;
        }

        GameObject gamePlayer = null;

        // 기본 구현 시도(설정된 GamePlayer Prefab에서 생성)
        try
        {
            gamePlayer = base.OnRoomServerCreateGamePlayer(conn, roomPlayerObj);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MyRoomManager] base.OnRoomServerCreateGamePlayer 예외: {ex}");
        }

        // base가 실패했다면 Fallback 프리팹으로 보완(선택)
        if (!gamePlayer)
        {
            if (fallbackGamePlayerPrefab != null)
            {
                gamePlayer = Instantiate(fallbackGamePlayerPrefab);
                NetworkServer.AddPlayerForConnection(conn, gamePlayer);
                Debug.Log("[MyRoomManager] Fallback GamePlayer 생성 완료");
            }
            else
            {
                Debug.LogError("[MyRoomManager] GamePlayer 프리팹 생성 실패. fallbackGamePlayerPrefab 미지정");
                return null;
            }
        }

        // 룸 → 게임 데이터 이관
        var rp = roomPlayerObj ? roomPlayerObj.GetComponent<MyRoomPlayer>() : null;
        var gp = gamePlayer.GetComponent<PlayerState>();
        if (!rp) Debug.LogError("[MyRoomManager] MyRoomPlayer 컴포넌트를 찾을 수 없습니다.");
        if (!gp) Debug.LogError("[MyRoomManager] PlayerState 컴포넌트를 찾을 수 없습니다.");

        if (rp && gp)
        {
            // 이름
            gp.playerName = string.IsNullOrWhiteSpace(rp.playerName)
                ? $"Player{conn.connectionId}"
                : rp.playerName;

            // 아바타 id 및 동물 키 동기화
            gp.avatarId = rp.avatarId;
            gp.avatarAnimal = string.IsNullOrWhiteSpace(rp.avatarAnimal)
                ? AvatarIdToAnimal(rp.avatarId)
                : rp.avatarAnimal;
        }

        return gamePlayer;
    }

    // 클라이언트 접속시 자동 플레이어 생성 옵션 보완
    public override void OnClientConnect()
    {
        base.OnClientConnect();

        if (!autoCreatePlayer)
        {
            if (!NetworkClient.ready) NetworkClient.Ready();
            if (NetworkClient.localPlayer == null)
                NetworkClient.AddPlayer();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 유틸
    // ─────────────────────────────────────────────────────────────────────────────
    private static string NormalizeSceneName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        return s.EndsWith(".unity") ? Path.GetFileNameWithoutExtension(s) : s;
    }

    private void DumpConfig(string tag)
    {
        var active = NetworkManager.singleton;
        var list = FindObjectsOfType<NetworkRoomManager>(true);
        Debug.Log($"{tag} RoomManagers in scene = {list.Length}. this={name}, singleton={(active ? active.name : "null")}");
        foreach (var m in list)
        {
            Debug.Log($"  - {m.name}  Room='{m.RoomScene}'  Game='{m.GameplayScene}'");
        }
    }
}
