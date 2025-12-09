using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// 아이템 효과를 주식 거래에 적용하는 클래스
/// 모든 아이템 효과 로직을 한 곳에서 관리
/// </summary>
public static class ItemEffectApplier
{
    /// <summary>
    /// 구매 시 아이템 효과 적용 (가격 할인)
    /// </summary>
    [Server]
    public static long ApplyBuyEffects(PlayerState ps, string stockId, long originalPrice)
    {
        if (ItemEffectTracker.Instance == null) return originalPrice;

        float discount = 0f;

        // noBuyDiscount: 구매 금액 10% 감소
        if (ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.noBuyDiscount))
        {
            discount += 0.10f;
            Debug.Log($"[ItemEffect] {ps.playerName} - noBuyDiscount 적용: 10% 할인");
            // 주식 구매 후 효과 제거
            ItemEffectTracker.Instance.RemoveEffect(ps, ItemEffect.noBuyDiscount);
        }

        long finalPrice = (long)(originalPrice * (1f - discount));
        return finalPrice;
    }

    /// <summary>
    /// 판매 시 아이템 효과 적용 (가격 보너스)
    /// </summary>
    [Server]
    public static long ApplySellEffects(PlayerState ps, string stockId, long originalPrice)
    {
        if (ItemEffectTracker.Instance == null) return originalPrice;

        float bonus = 0f;

        // HPSellBonus: 잃은 체력에 비례한 보너스
        bonus += ApplyHPSellBonus(ps);

        // triple: 3종목 이상 보유 시 5% 보너스
        bonus += ApplyTripleBonus(ps);

        // lowStack: 가격 하락 시 스택 누적, 3스택 달성 시 5% 보너스
        bonus += ApplyLowStackBonus(ps, stockId);

        // stay: 40초 이상 보유 시 5% 보너스
        bonus += ApplyStayBonus(ps, stockId);

        // hightStack: 가격 상승 시 5% 보너스
        bonus += ApplyHighStackBonus(ps, stockId);

        // deficit: 손해 매도 시 5% 보너스
        bonus += ApplyDeficitBonus(ps, stockId, originalPrice);

        // shortSell: 15초 이내 매도 시 5% 보너스
        bonus += ApplyShortSellBonus(ps, stockId);

        // timeBonus: 거래 시간 종료 임박 시 5% 보너스
        bonus += ApplyTimeBonus(ps);

        // playerBonus: (5 - 남은 플레이어 수)% 보너스
        bonus += ApplyPlayerBonus(ps);

        // reverseTrade: 구매가보다 낮게 팔면 35% 증가, 높게 팔면 15% 감소
        bonus += ApplyReverseTrade(ps, stockId, originalPrice);

        long finalPrice = (long)(originalPrice * (1f + bonus));
        return finalPrice;
    }

    // ===== 개별 효과 적용 함수들 =====

    /// <summary>
    /// HPSellBonus: 잃은 체력에 비례한 판매 보너스
    /// </summary>
    static float ApplyHPSellBonus(PlayerState ps)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.HPSellBonus))
            return 0f;

        float hpLossPercent = (ps.maxHp - ps.hp) / 1000f;
        Debug.Log($"[ItemEffect] {ps.playerName} - HPSellBonus 적용: {hpLossPercent * 100:F1}% 보너스");
        return hpLossPercent;
    }

    /// <summary>
    /// triple: 3종목 이상 보유 시 5% 보너스
    /// </summary>
    static float ApplyTripleBonus(PlayerState ps)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.triple))
            return 0f;

        // portfolio에서 보유 종목 수 계산
        int stockTypeCount = ps.portfolio.Count;
        if (stockTypeCount >= 3)
        {
            Debug.Log($"[ItemEffect] {ps.playerName} - triple 적용: 5% 보너스 (보유 종목: {stockTypeCount})");
            return 0.05f;
        }
        return 0f;
    }

    /// <summary>
    /// lowStack: 가격 하락 시 스택 누적, 3스택 달성 시 5% 보너스
    /// </summary>
    static float ApplyLowStackBonus(PlayerState ps, string stockId)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.lowStack))
            return 0f;

        int stackCount = ItemEffectTracker.Instance.GetEffectValue(ps, ItemEffect.lowStack);
        if (stackCount >= 3)
        {
            Debug.Log($"[ItemEffect] {ps.playerName} - lowStack 적용: 5% 보너스 (스택: {stackCount})");
            // 스택 초기화
            ItemEffectTracker.Instance.RemoveEffect(ps, ItemEffect.lowStack);
            return 0.05f;
        }
        return 0f;
    }

    /// <summary>
    /// stay: 40초 이상 보유 시 5% 보너스
    /// </summary>
    static float ApplyStayBonus(PlayerState ps, string stockId)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.stay))
            return 0f;

        // 보유 시간 확인 (초 단위로 저장되어 있다고 가정)
        int holdTime = ItemEffectTracker.Instance.GetEffectValue(ps, ItemEffect.stay);
        if (holdTime >= 40)
        {
            Debug.Log($"[ItemEffect] {ps.playerName} - stay 적용: 5% 보너스 (보유 시간: {holdTime}초)");
            return 0.05f;
        }
        return 0f;
    }

    /// <summary>
    /// hightStack: 가격 상승 시 5% 보너스
    /// </summary>
    static float ApplyHighStackBonus(PlayerState ps, string stockId)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.hightStack))
            return 0f;

        int stackCount = ItemEffectTracker.Instance.GetEffectValue(ps, ItemEffect.hightStack);
        if (stackCount > 0)
        {
            Debug.Log($"[ItemEffect] {ps.playerName} - hightStack 적용: 5% 보너스 (스택: {stackCount})");
            // 스택 초기화
            ItemEffectTracker.Instance.RemoveEffect(ps, ItemEffect.hightStack);
            return 0.05f;
        }
        return 0f;
    }

    /// <summary>
    /// deficit: 손해 매도 시 5% 보너스
    /// </summary>
    static float ApplyDeficitBonus(PlayerState ps, string stockId, long currentPrice)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.deficit))
            return 0f;

        // portfolio에서 해당 종목 찾기
        foreach (var stock in ps.portfolio)
        {
            if (stock.stockId == stockId)
            {
                double avgBuyPrice = stock.averagePurchasePrice;
                double sellPrice = currentPrice / 100.0;

                if (sellPrice < avgBuyPrice)
                {
                    Debug.Log($"[ItemEffect] {ps.playerName} - deficit 적용: 5% 보너스 (손해 매도)");
                    return 0.05f;
                }
                break;
            }
        }
        return 0f;
    }

    /// <summary>
    /// shortSell: 15초 이내 매도 시 5% 보너스
    /// </summary>
    static float ApplyShortSellBonus(PlayerState ps, string stockId)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.shortSell))
            return 0f;

        // 보유 시간 확인 (초 단위로 저장되어 있다고 가정)
        int holdTime = ItemEffectTracker.Instance.GetEffectValue(ps, ItemEffect.shortSell);
        if (holdTime <= 15)
        {
            Debug.Log($"[ItemEffect] {ps.playerName} - shortSell 적용: 5% 보너스 (보유 시간: {holdTime}초)");
            return 0.05f;
        }
        return 0f;
    }

    /// <summary>
    /// timeBonus: 거래 시간 종료 임박 시 5% 보너스
    /// </summary>
    static float ApplyTimeBonus(PlayerState ps)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.timeBonus))
            return 0f;

        // TimeManager에서 남은 시간 확인
        if (TimeManager.Instance != null)
        {
            float remainingTime = ItemEffectTracker.Instance.GetEffectValue(ps, ItemEffect.timeBonus);
            if (remainingTime <= 30f) // 30초 이하 남았을 때
            {
                Debug.Log($"[ItemEffect] {ps.playerName} - timeBonus 적용: 5% 보너스 (남은 시간: {remainingTime}초)");
                return 0.05f;
            }
        }
        return 0f;
    }

    /// <summary>
    /// playerBonus: (5 - 남은 플레이어 수)% 보너스
    /// </summary>
    static float ApplyPlayerBonus(PlayerState ps)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.playerBonus))
            return 0f;

        int alivePlayers = 0;
        foreach (var p in PlayerState.All)
        {
            if (!p.isEliminated) alivePlayers++;
        }
        
        float bonus = (8 - alivePlayers) / 100f;
        if (bonus > 0)
        {
            Debug.Log($"[ItemEffect] {ps.playerName} - playerBonus 적용: {bonus * 100:F1}% 보너스 (생존자: {alivePlayers})");
            return bonus;
        }
        return 0f;
    }

    /// <summary>
    /// reverseTrade: 구매가보다 낮게 팔면 35% 증가, 높게 팔면 15% 감소
    /// </summary>
    static float ApplyReverseTrade(PlayerState ps, string stockId, long currentPrice)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.reverseTrade))
            return 0f;

        // portfolio에서 해당 종목 찾기
        foreach (var stock in ps.portfolio)
        {
            if (stock.stockId == stockId)
            {
                double avgBuyPrice = stock.averagePurchasePrice;
                double sellPrice = currentPrice / 100.0;

                if (sellPrice < avgBuyPrice)
                {
                    Debug.Log($"[ItemEffect] {ps.playerName} - reverseTrade 적용: 손해 매도 35% 보너스");
                    return 0.35f;
                }
                else
                {
                    Debug.Log($"[ItemEffect] {ps.playerName} - reverseTrade 적용: 이익 매도 15% 감소");
                    return -0.15f;
                }
            }
        }
        return 0f;
    }

    // ===== 구매 시 스택 업데이트 함수들 =====

    /// <summary>
    /// 구매 시 lowStack 스택 업데이트
    /// </summary>
    [Server]
    public static void UpdateLowStackOnBuy(PlayerState ps, string stockId, double buyPrice)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.lowStack))
            return;

        // portfolio에서 해당 종목 찾기
        foreach (var stock in ps.portfolio)
        {
            if (stock.stockId == stockId && buyPrice < stock.averagePurchasePrice)
            {
                int currentStack = ItemEffectTracker.Instance.GetEffectValue(ps, ItemEffect.lowStack);
                ItemEffectTracker.Instance.AddEffect(ps, ItemEffect.lowStack, currentStack + 1);
                Debug.Log($"[ItemEffect] {ps.playerName} - lowStack 스택 증가: {currentStack + 1}");
                break;
            }
        }
    }

    /// <summary>
    /// 구매 시 hightStack 스택 업데이트
    /// </summary>
    [Server]
    public static void UpdateHighStackOnBuy(PlayerState ps, string stockId, double buyPrice)
    {
        if (!ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.hightStack))
            return;

        // portfolio에서 해당 종목 찾기
        foreach (var stock in ps.portfolio)
        {
            if (stock.stockId == stockId && buyPrice > stock.averagePurchasePrice)
            {
                int currentStack = ItemEffectTracker.Instance.GetEffectValue(ps, ItemEffect.hightStack);
                ItemEffectTracker.Instance.AddEffect(ps, ItemEffect.hightStack, currentStack + 1);
                Debug.Log($"[ItemEffect] {ps.playerName} - hightStack 스택 증가: {currentStack + 1}");
                break;
            }
        }
    }

    /// <summary>
    /// 구매 시 보유 시간 추적 시작 (stay, shortSell, dividend용)
    /// </summary>
    [Server]
    public static void StartHoldingTimer(PlayerState ps, string stockId)
    {
        // stay 효과가 있으면 타이머 시작
        if (ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.stay))
        {
            ItemEffectTracker.Instance.AddEffect(ps, ItemEffect.stay, 0); // 0초부터 시작
        }

        // shortSell 효과가 있으면 타이머 시작
        if (ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.shortSell))
        {
            ItemEffectTracker.Instance.AddEffect(ps, ItemEffect.shortSell, 0);
        }

        // dividend 효과가 있으면 타이머 시작
        if (ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.dividend))
        {
            ItemEffectTracker.Instance.AddEffect(ps, ItemEffect.dividend, 0);
        }
    }

    /// <summary>
    /// 매 초마다 보유 시간 업데이트 (TimeManager나 별도 코루틴에서 호출)
    /// </summary>
    [Server]
    public static void UpdateHoldingTimers()
    {
        if (ItemEffectTracker.Instance == null) return;

        foreach (var ps in PlayerState.All)
        {
            // stay 타이머 업데이트
            if (ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.stay))
            {
                int time = ItemEffectTracker.Instance.GetEffectValue(ps, ItemEffect.stay);
                ItemEffectTracker.Instance.AddEffect(ps, ItemEffect.stay, time + 1);
            }

            // shortSell 타이머 업데이트
            if (ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.shortSell))
            {
                int time = ItemEffectTracker.Instance.GetEffectValue(ps, ItemEffect.shortSell);
                ItemEffectTracker.Instance.AddEffect(ps, ItemEffect.shortSell, time + 1);
            }

            // dividend 타이머 업데이트 및 배당금 지급
            if (ItemEffectTracker.Instance.HasEffect(ps, ItemEffect.dividend))
            {
                int time = ItemEffectTracker.Instance.GetEffectValue(ps, ItemEffect.dividend);
                ItemEffectTracker.Instance.AddEffect(ps, ItemEffect.dividend, time + 1);

                // 30초 이상 보유 시 5초마다 배당금 지급
                if (time >= 30 && time % 5 == 0)
                {
                    ApplyDividend(ps);
                }
            }
        }
    }

    /// <summary>
    /// dividend: 30초 이상 보유 시 5초마다 구매 금액의 2% 배당금
    /// </summary>
    [Server]
    static void ApplyDividend(PlayerState ps)
    {
        // 모든 보유 주식에 대해 배당금 계산
        long totalDividend = 0;
        foreach (var stock in ps.portfolio)
        {
            long investedAmount = (long)(stock.averagePurchasePrice * stock.quantity * 100);
            long dividend = (long)(investedAmount * 0.02f);
            totalDividend += dividend;
        }

        if (totalDividend > 0)
        {
            ps.ServerAddCash(totalDividend);
            Debug.Log($"[ItemEffect] {ps.playerName} - dividend 적용: {totalDividend / 100.0:F2}원 배당금 지급");
        }
    }
}
