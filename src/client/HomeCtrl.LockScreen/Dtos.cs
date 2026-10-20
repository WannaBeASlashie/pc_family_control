namespace HomeCtrl.LockScreen;

// 与 HomeCtrl.Agent 共享的状态快照（字段名两端一致）
public record StateSnapshot(
    bool Locked,
    int? SessionId,
    string? PersonName,
    long BalanceSeconds,
    bool ServerOnline,
    string? Message,
    bool Offline);
