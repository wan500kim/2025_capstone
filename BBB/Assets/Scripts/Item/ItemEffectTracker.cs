using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// 플레이어별 아이템 효과를 추적하고 관리하는 싱글톤 클래스
/// PlayerState에 activeEffects 필드가 없으므로 별도로 관리
/// </summary>
public class ItemEffectTracker : NetworkBehaviour
{
    public static ItemEffectTracker Instance;

    // 플레이어별 활성화된 아이템 효과 (effectType -> value)
    private Dictionary<PlayerState, Dictionary<ItemEffect, int>> playerEffects = 
        new Dictionary<PlayerState, Dictionary<ItemEffect, int>>();

    void Awake()
    {
        if (Instance == null) 
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else 
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 플레이어에게 효과 추가
    /// </summary>
    [Server]
    public void AddEffect(PlayerState player, ItemEffect effectType, int value)
    {
        if (player == null) return;

        if (!playerEffects.ContainsKey(player))
        {
            playerEffects[player] = new Dictionary<ItemEffect, int>();
        }

        playerEffects[player][effectType] = value;
        Debug.Log($"[ItemEffectTracker] {player.playerName}에게 {effectType} 효과 추가 (값: {value})");
    }

    /// <summary>
    /// 플레이어의 특정 효과 값 조회
    /// </summary>
    public int GetEffectValue(PlayerState player, ItemEffect effectType)
    {
        if (player == null || !playerEffects.ContainsKey(player))
            return 0;

        return playerEffects[player].TryGetValue(effectType, out int value) ? value : 0;
    }

    /// <summary>
    /// 플레이어가 특정 효과를 가지고 있는지 확인
    /// </summary>
    public bool HasEffect(PlayerState player, ItemEffect effectType)
    {
        if (player == null || !playerEffects.ContainsKey(player))
            return false;

        return playerEffects[player].ContainsKey(effectType);
    }

    /// <summary>
    /// 플레이어의 특정 효과 제거
    /// </summary>
    [Server]
    public void RemoveEffect(PlayerState player, ItemEffect effectType)
    {
        if (player == null || !playerEffects.ContainsKey(player))
            return;

        playerEffects[player].Remove(effectType);
        Debug.Log($"[ItemEffectTracker] {player.playerName}의 {effectType} 효과 제거");
    }

    /// <summary>
    /// 플레이어의 모든 효과 제거
    /// </summary>
    [Server]
    public void ClearPlayerEffects(PlayerState player)
    {
        if (player == null) return;

        playerEffects.Remove(player);
        Debug.Log($"[ItemEffectTracker] {player.playerName}의 모든 효과 제거");
    }

    /// <summary>
    /// 모든 플레이어의 효과 초기화
    /// </summary>
    [Server]
    public void ClearAllEffects()
    {
        playerEffects.Clear();
        Debug.Log("[ItemEffectTracker] 모든 플레이어 효과 초기화");
    }

    /// <summary>
    /// 플레이어의 모든 활성 효과 조회
    /// </summary>
    public Dictionary<ItemEffect, int> GetPlayerEffects(PlayerState player)
    {
        if (player == null || !playerEffects.ContainsKey(player))
            return new Dictionary<ItemEffect, int>();

        return new Dictionary<ItemEffect, int>(playerEffects[player]);
    }
}
