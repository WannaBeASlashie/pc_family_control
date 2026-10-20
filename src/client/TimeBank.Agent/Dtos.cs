namespace WillBeyond.TimeBank.Agent;

// 与 LockScreen UI 共享的状态快照（字段名两端一致，camelCase 序列化）
public record StateSnapshot(
    bool Locked,
    int? SessionId,
    string? PersonName,
    long BalanceSeconds,
    bool ServerOnline,
    string? Message,
    bool Offline);

public record HeartbeatSession(int Id, string Person, DateTime StartedAt);

public record HeartbeatCommand(int Id, string Action, string? Payload);

public record HeartbeatResp(
    DateTime ServerTime,
    HeartbeatSession? Session,
    long BalanceSeconds,
    List<HeartbeatCommand>? Commands,
    OfflineKit? OfflineKit = null);

// 离线档案（服务端心跳下发，Agent 持久化到 ProgramData\TimeBank\offline\kit.json）
public record OfflineKitPerson(string CardNo, string? DisplayName, string PinHash, long Balance, long Quota);
public record OfflineKit(DateTime GeneratedAt, int MaxAgeHours, int TzOffsetHours, long MachineDailySeconds, List<OfflineKitPerson>? Persons);

public record LoginResp(bool Ok, string? Reason, int? SessionId, string? Person, long BalanceSeconds);

public record LogoutResp(bool Ok, long ChargedSeconds);

public record RequestResp(bool Ok, string? Reason, int? RequestId);
