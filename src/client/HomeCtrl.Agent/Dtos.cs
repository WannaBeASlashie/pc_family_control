namespace HomeCtrl.Agent;

// 与 LockScreen UI 共享的状态快照（字段名两端一致，camelCase 序列化）
public record StateSnapshot(
    bool Locked,
    int? SessionId,
    string? PersonName,
    long BalanceSeconds,
    bool ServerOnline,
    string? Message);

public record HeartbeatSession(int Id, string Person, DateTime StartedAt);

public record HeartbeatCommand(int Id, string Action, string? Payload);

public record HeartbeatResp(
    DateTime ServerTime,
    HeartbeatSession? Session,
    long BalanceSeconds,
    List<HeartbeatCommand>? Commands);

public record LoginResp(bool Ok, string? Reason, int? SessionId, string? Person, long BalanceSeconds);

public record LogoutResp(bool Ok, long ChargedSeconds);
