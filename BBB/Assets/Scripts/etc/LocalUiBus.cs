using System;
using System.Collections.Generic;

public static class LocalUiBus
{
    public static System.Action<int> OnMoney;
    public static System.Action<int,int> OnHp;
    public static System.Action<System.Collections.Generic.IDictionary<string,int>> OnHoldingsRefresh;
    public static System.Action<string> OnToast;
    public static Action<IReadOnlyList<string>> OnSymbols;
    
    // 아이템 시스템 관련 이벤트
    public static System.Action<ItemOption[]> OnItemOptions;  // 아이템 선택지 수신
    public static System.Action<int> OnTargetAmount;  // 목표 금액 업데이트
}
