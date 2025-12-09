using Mirror;
using UnityEngine;

/// <summary>
/// 플레이어 추정자산 네트워크 동기화 컴포넌트
/// - PlayerState와 같은 GameObject에 부착
/// - 모든 클라이언트가 서로의 추정자산을 볼 수 있음
/// </summary>
public sealed class PlayerEstimatedAssetSync : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnEstimatedChanged))]
    public long EstimatedEquityCents; // 현금 + 전 종목 현재가 청산가 합계

    public delegate void EstimatedChanged(long oldVal, long newVal);
    public event EstimatedChanged OnChanged;

    [Command]
    public void CmdSetEstimatedEquity(long cents)
    {
        EstimatedEquityCents = cents;
    }

    [Server]
    public void ServerSetEstimated(long cents)
    {
        EstimatedEquityCents = cents;
    }

    void OnEstimatedChanged(long oldVal, long newVal)
    {
        OnChanged?.Invoke(oldVal, newVal);
    }
}
