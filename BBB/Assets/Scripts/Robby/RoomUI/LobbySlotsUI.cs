// LobbySlotsUI.cs
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

public class LobbySlotsUI : MonoBehaviour
{
    [System.Serializable]
    public class SlotView
    {
        public GameObject panel;         // 빈칸이면 비활성화
        public TextMeshProUGUI nameText; // 플레이어 이름
        public Image avatarImage;        // 아바타(선택)
        public GameObject readyBadge;    // 준비 배지
    }

    [Header("Slot Views (인스펙터에서 0~3 순서로 연결)")]
    [SerializeField] private SlotView[] slots;

    void OnEnable()
    {
        // 과도한 Update를 막고 주기 갱신
        CancelInvoke(nameof(Refresh));
        InvokeRepeating(nameof(Refresh), 0.1f, 0.25f);
    }

    void OnDisable()
    {
        CancelInvoke(nameof(Refresh));
    }

    void Refresh()
    {
        if (slots == null || slots.Length == 0)
            return;

        // 현재 로비의 플레이어 수집
        var players = FindObjectsOfType<MyRoomPlayer>(includeInactive: false);

        // slotIndex가 있으면 그 순서대로, 없으면 임의 정렬
        List<MyRoomPlayer> ordered = players
            .OrderBy(p => p.slotIndex < 0 ? int.MaxValue : p.slotIndex)
            .ThenBy(p => p.netId)
            .ToList();

        // 각 슬롯 갱신
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            if (s == null) continue;

            if (i < ordered.Count && ordered[i] != null)
            {
                var p = ordered[i];

                if (s.panel) s.panel.SetActive(true);

                if (s.nameText)
                    s.nameText.text = string.IsNullOrWhiteSpace(p.playerName)
                        ? $"Player {i + 1}"
                        : p.playerName;

                // === 핵심 수정: avatarAnimal 리소스를 최우선 사용 ===
                if (s.avatarImage)
                {
                    Sprite sprite = null;

                    // 1) 동물 키 우선: Assets/Resources/robby_image/player_{animal}.png
                    if (!string.IsNullOrWhiteSpace(p.avatarAnimal))
                    {
                        sprite = Resources.Load<Sprite>($"robby_image/player_{p.avatarAnimal}");
                    }

                    // 2) 폴백: 인덱스 기반 스프라이트 배열(MyRoomManager.avatarSprites)
                    if (sprite == null && MyRoomManager.Instance != null && MyRoomManager.Instance.avatarSprites != null)
                    {
                        var list = MyRoomManager.Instance.avatarSprites;
                        if (p.avatarId >= 0 && p.avatarId < list.Length)
                            sprite = list[p.avatarId];
                    }

                    // 3) 표시
                    if (sprite != null)
                    {
                        s.avatarImage.enabled = true;
                        s.avatarImage.sprite = sprite;
                    }
                    else
                    {
                        s.avatarImage.enabled = false;
                        s.avatarImage.sprite = null;
                    }
                }

                if (s.readyBadge) s.readyBadge.SetActive(p.readyToBegin);
            }
            else
            {
                // 빈 슬롯 처리
                if (s.panel) s.panel.SetActive(false);
                if (s.nameText) s.nameText.text = "";
                if (s.avatarImage)
                {
                    s.avatarImage.enabled = false;
                    s.avatarImage.sprite = null;
                }
                if (s.readyBadge) s.readyBadge.SetActive(false);
            }
        }
    }
}
