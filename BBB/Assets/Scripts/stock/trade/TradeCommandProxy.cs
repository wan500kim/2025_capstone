using System;
using Mirror;
using UnityEngine;

/// <summary>
/// 서버 거래 명령 프록시
/// - 반드시 플레이어 프리팹에 '미리' 부착되어 있어야 합니다.
/// - Command는 서버/클라이언트 모두 동일 컴포넌트 인덱스로 존재해야 합니다.
/// </summary>
public sealed class TradeCommandProxy : NetworkBehaviour
{
    public event Action<bool, string> OnClientFeedback;

    [Command] // requiresAuthority=true 기본값: 소유 클라이언트만 호출 가능
    public void CmdBuy(string stockId, int qty, long unitPriceCents)
    {
        // Prep 턴과 Result 턴에는 매매 불가
        if (!CanTrade())
        {
            TargetFeedback(connectionToClient, false, "현재 거래할 수 없는 시간입니다.");
            return;
        }
        
        if (!ValidateTrade(stockId, qty, unitPriceCents, out var err))
        {
            TargetFeedback(connectionToClient, false, err);
            return;
        }

        var ps = GetComponent<PlayerState>();
        if (ps == null) { TargetFeedback(connectionToClient, false, "PlayerState 누락"); return; }

        // 아이템 효과 적용: 구매 가격 할인
        long finalUnitPrice = ItemEffectApplier.ApplyBuyEffects(ps, stockId, unitPriceCents);
        long total = checked(finalUnitPrice * qty);
        
        if (ps.CashCents < total)
        {
            TargetFeedback(connectionToClient, false, "보유 현금이 부족합니다.");
            return;
        }

        ps.ServerAddCash(-total);
        ps.ServerExecuteBuyOrder(stockId, qty, finalUnitPrice / 100.0);
        ps.RecalculateTotalValuation();

        // 아이템 효과: 구매 후 스택 업데이트 및 타이머 시작
        ItemEffectApplier.UpdateLowStackOnBuy(ps, stockId, finalUnitPrice / 100.0);
        ItemEffectApplier.UpdateHighStackOnBuy(ps, stockId, finalUnitPrice / 100.0);
        ItemEffectApplier.StartHoldingTimer(ps, stockId);

        // TODO: 서버 DB 저장 훅으로 교체
        Debug.Log($"[TradeLog][BUY] {ps.playerId} {stockId} x{qty} @ {finalUnitPrice/100.0:F2} (원가: {unitPriceCents/100.0:F2})");

        TargetFeedback(connectionToClient, true, "매수 체결되었습니다.");
    }

    [Command]
    public void CmdSell(string stockId, int qty, long unitPriceCents)
    {
        // Prep 턴과 Result 턴에는 매매 불가
        if (!CanTrade())
        {
            TargetFeedback(connectionToClient, false, "현재 거래할 수 없는 시간입니다.");
            return;
        }
        
        if (!ValidateTrade(stockId, qty, unitPriceCents, out var err))
        {
            TargetFeedback(connectionToClient, false, err);
            return;
        }

        var ps = GetComponent<PlayerState>();
        if (ps == null) { TargetFeedback(connectionToClient, false, "PlayerState 누락"); return; }

        int holding = ps.GetHoldingQuantity(stockId);
        if (holding < qty)
        {
            TargetFeedback(connectionToClient, false, "보유 수량이 부족합니다.");
            return;
        }

        // 아이템 효과 적용: 판매 가격 보너스
        long finalUnitPrice = ItemEffectApplier.ApplySellEffects(ps, stockId, unitPriceCents);
        long total = checked(finalUnitPrice * qty);

        ps.ServerExecuteSellOrder(stockId, qty);
        ps.ServerAddCash(total);
        ps.RecalculateTotalValuation();

        // TODO: 서버 DB 저장 훅으로 교체
        Debug.Log($"[TradeLog][SELL] {ps.playerId} {stockId} x{qty} @ {finalUnitPrice/100.0:F2} (원가: {unitPriceCents/100.0:F2})");

        TargetFeedback(connectionToClient, true, "매도 체결되었습니다.");
    }

    [TargetRpc]
    void TargetFeedback(NetworkConnection target, bool ok, string message)
    {
        OnClientFeedback?.Invoke(ok, message);
    }

    bool ValidateTrade(string stockId, int qty, long unitPriceCents, out string err)
    {
        if (string.IsNullOrWhiteSpace(stockId)) { err = "종목이 없습니다."; return false; }
        if (qty <= 0)                            { err = "수량은 1 이상이어야 합니다."; return false; }
        if (unitPriceCents <= 0)                 { err = "가격이 유효하지 않습니다."; return false; }
        err = null; return true;
    }
    
    bool CanTrade()
    {
        // TimeManager가 없으면 거래 불가
        if (TimeManager.Instance == null) return false;
        
        var phase = TimeManager.Phase;
        
        // Prep 턴과 Result 턴에는 거래 불가
        if (phase == GamePhase.Prep || phase == GamePhase.Result)
        {
            return false;
        }
        
        // Round 턴에만 거래 가능
        return phase == GamePhase.Round;
    }
}
