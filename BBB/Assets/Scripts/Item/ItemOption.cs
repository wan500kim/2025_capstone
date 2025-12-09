using System;
using UnityEngine;

/// <summary>
/// Ready 페이즈에서 플레이어에게 제공되는 아이템 선택지
/// 아이템 효과 적용 로직도 포함
/// </summary>
[Serializable]
public struct ItemOption
{
    public string id;              // 고유 ID (예: "sell-bonus-hp-50")
    public string displayName;     // UI 표시 이름
    public string description;     // 아이템 효과 설명
    public ItemEffect effectType;  // 효과 타입 (Enum)
    public int value;              // 효과 값
    public string iconPath;        // 아이템 아이콘 경로 (Resources 폴더 기준)

    public ItemOption(string id, string displayName, string description, ItemEffect effectType, int value, string iconPath = "")
    {
        this.id = id;
        this.displayName = displayName;
        this.description = description;
        this.effectType = effectType;
        this.value = value;
        this.iconPath = iconPath;
    }

    /// <summary>
    /// 아이템 효과를 적용
    /// 서버에서만 호출되어야 함
    /// </summary>
    public void ApplyEffect(PlayerState player)
    {
        if (player == null)
        {
            Debug.LogWarning("[ItemOption] PlayerState가 null입니다.");
            return;
        }

        switch (effectType)
        {
            case ItemEffect.money:
                // 지속 효과: 매 라운드 초기 자본금 증가 (value는 % 단위)
                ItemEffectTracker.Instance?.AddEffect(player, effectType, value);
                Debug.Log($"[ItemOption] {player.playerName}: 라운드 시작 시 초기 자본금 +{value}% 효과 활성화");
                break;
                
            case ItemEffect.health:
                // 즉시 HP 증가
                int oldHp = player.hp;
                player.ServerHeal(value);
                Debug.Log($"[ItemOption] {player.playerName}: 체력 {oldHp} → {player.hp} (+{value})");
                break;
                
            // 지속 효과는 ItemEffectTracker에 저장 (별도 관리 필요)
            case ItemEffect.HPSellBonus:
            case ItemEffect.triple:
            case ItemEffect.lowStack:
            case ItemEffect.stay:
            case ItemEffect.dividend:
            case ItemEffect.hightStack:
            case ItemEffect.deficit:
            case ItemEffect.shortSell:
            case ItemEffect.timeBonus:
            case ItemEffect.playerBonus:
            case ItemEffect.noBuyDiscount:
            case ItemEffect.reverseTrade:
                // ItemEffectTracker를 통해 효과 저장
                ItemEffectTracker.Instance?.AddEffect(player, effectType, value);
                Debug.Log($"[ItemOption] {player.playerName}: {effectType} 효과 활성화 (값: {value})");
                break;
                
            default:
                Debug.LogWarning($"[ItemOption] 알 수 없는 효과 타입: {effectType}");
                break;
        }
    }

    /// <summary>
    /// 아이템 효과 설명 메시지 생성
    /// </summary>
    public string GetEffectMessage()
    {
        switch (effectType)
        {
            case ItemEffect.money:
                return $"아이템 적용: 라운드 시작 시 초기 자본금 +{value}%";
                
            case ItemEffect.health:
                return $"아이템 적용: 체력 +{value}";
                
            case ItemEffect.HPSellBonus:
                return $"아이템 적용: 잃은 체력에 비례한 판매 보너스";
                
            case ItemEffect.triple:
                return $"아이템 적용: 3종목 이상 보유 시 판매 보너스 +5%";
                
            case ItemEffect.lowStack:
                return $"아이템 적용: 가격 하락 시 스택 누적, 3스택 달성 시 판매 보너스 +5%";
                
            case ItemEffect.stay:
                return $"아이템 적용: 40초 이상 보유 시 판매 보너스 +5%";
                
            case ItemEffect.dividend:
                return $"아이템 적용: 30초 이상 보유 시 5초마다 배당금 2% 지급";
                
            case ItemEffect.hightStack:
                return $"아이템 적용: 가격 상승 시 다음 판매 보너스 +5%";
                
            case ItemEffect.deficit:
                return $"아이템 적용: 손실 거래 시 판매 보너스 +5%";
                
            case ItemEffect.shortSell:
                return $"아이템 적용: 15초 이내 단타 시 판매 보너스 +5%";
                
            case ItemEffect.timeBonus:
                return $"아이템 적용: 장 마감 10초 전 판매 시 보너스 +5%";
                
            case ItemEffect.playerBonus:
                return $"아이템 적용: 남은 플레이어 수에 반비례한 판매 보너스";
                
            case ItemEffect.noBuyDiscount:
                return $"아이템 적용: 20초간 주식 미구매 시 구매 금액 10% 감소";
                
            case ItemEffect.reverseTrade:
                return $"아이템 적용: 손실 거래 시 +35%, 이익 거래 시 -15%";
                
            default:
                return $"아이템 적용: {displayName}";
        }
    }
}
