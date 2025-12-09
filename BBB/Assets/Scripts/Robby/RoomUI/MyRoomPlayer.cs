using Mirror;
using UnityEngine;
using UnityEngine.UI;

public class MyRoomPlayer : NetworkRoomPlayer
{
    [SyncVar] public string playerName;

    [SyncVar(hook = nameof(OnAvatarChanged))]
    public int avatarId = -1; // -1=미배정, 0..3

    // ★ 동물 키 동기화
    [SyncVar(hook = nameof(OnAvatarAnimalChanged))]
    public string avatarAnimal = "";

    [Header("UI (로비 미리보기)")]
    [SerializeField] Image previewImage; // 로비 카드/슬롯의 Image

    [SyncVar(hook = nameof(OnSlotChanged))]
    public int slotIndex = -1; // 0~3, -1=미배정

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        CmdSetName(NameStore.Current);
    }

    [Command]
    void CmdSetName(string n)
    {
        if (string.IsNullOrWhiteSpace(n))
            n = $"Player{Random.Range(1000,9999)}";
        playerName = n.Trim();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (MyRoomManager.Instance)
        {
            var picked = MyRoomManager.Instance.AssignRandomAvatar(this);
            MyRoomManager.Instance.AssignSlot(this);
            // AssignRandomAvatar 안에서 avatarAnimal도 설정됨
            Debug.Log($"[Room] Assigned avatar {picked} ({avatarAnimal}) to {connectionToClient.connectionId}");
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyPreview(); // 현재 값 즉시 반영
    }

    public override void OnStopServer()
    {
        if (MyRoomManager.Instance){
            MyRoomManager.Instance.ReleaseAvatar(this);
            MyRoomManager.Instance.ReleaseSlot(this);
        }
        base.OnStopServer();
    }

    // ===== Hooks =====
    void OnAvatarChanged(int oldV, int newV) => ApplyPreview();
    void OnAvatarAnimalChanged(string oldKey, string newKey) => ApplyPreview();
    void OnSlotChanged(int oldV, int newV) { /* 슬롯 UI 필요 시 사용 */ }

    // ===== Preview Loader =====
    void ApplyPreview()
    {
        if (!previewImage) return;

        // 1) 동물 키 우선: Resources/robby_image/player_{animal}.png
        if (!string.IsNullOrWhiteSpace(avatarAnimal))
        {
            var sprite = Resources.Load<Sprite>($"robby_image/player_{avatarAnimal}");
            if (sprite != null)
            {
                previewImage.enabled = true;
                previewImage.sprite  = sprite;
                return;
            }
        }

        // 2) 폴백: 인덱스 기반 스프라이트 배열
        var m = MyRoomManager.Instance;
        if (m != null && m.avatarSprites != null && avatarId >= 0 && avatarId < m.avatarSprites.Length && m.avatarSprites[avatarId] != null)
        {
            previewImage.enabled = true;
            previewImage.sprite  = m.avatarSprites[avatarId];
            return;
        }

        // 3) 최종 폴백: 미표시
        previewImage.enabled = false;
        previewImage.sprite  = null;
    }
}
