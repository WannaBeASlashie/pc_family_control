namespace HomeCtrl.Server.Models;

public class Person
{
    public int Id { get; set; }
    public string CardNo { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PinHash { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public long BalanceSeconds { get; set; }
    public long Points { get; set; }          // 预留：积分（P2 任务/兑换使用）
    public int PolicyId { get; set; } = 1;    // 策略（时段/每日额度/单次上限）
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>策略（FR-05）：可用时段 + 每日额度（工作日/周末）+ 单次连续上限。</summary>
public class Policy
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long WeekdayQuotaSeconds { get; set; }   // 0 = 不限
    public long WeekendQuotaSeconds { get; set; }   // 0 = 不限
    public string AllowedWindowsJson { get; set; } = "[{\"start\":\"00:00\",\"end\":\"24:00\"}]";
    public long MaxSessionSeconds { get; set; }     // 0 = 不限
    public long SlidingWindowSeconds { get; set; }  // 滑动窗口长度（0=不启用，需与限额同时>0）
    public long SlidingQuotaSeconds { get; set; }   // 窗口内最大累计用时（0=不启用）
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Machine
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Secret { get; set; } = "";
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Session
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
    public int MachineId { get; set; }
    public Machine? Machine { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public long ChargedSeconds { get; set; }
    public DateTime? ExhaustedAt { get; set; }     // 余额耗尽时刻（宽限计时起点）
    public DateTime? LockSentAt { get; set; }
    public DateTime? LogoffSentAt { get; set; }
    public string? EndReason { get; set; }
    public string Status { get; set; } = SessionStatus.Active;
}

public class Command
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public string Action { get; set; } = "";       // lock|unlock|logoff|shutdown|reboot|message
    public string Payload { get; set; } = "{}";
    public string Status { get; set; } = CommandStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }       // 过期不再下发，防止隔天执行旧指令
    public DateTime? AckedAt { get; set; }
}

/// <summary>加时申请（FR-11a）：孩子发起，家长审批。</summary>
public class TimeRequest
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
    public int MachineId { get; set; }
    public int Minutes { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = RequestStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
}

public class TimeLedger
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public long DeltaSeconds { get; set; }
    public string Kind { get; set; } = "";         // initial|parent_grant|parent_deduct|session_charge
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>预留：积分流水（P2 任务/兑换使用）</summary>
public class PointsLedger
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public long Delta { get; set; }
    public string Kind { get; set; } = "";
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public DateTime Ts { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

public static class SessionStatus
{
    public const string Active = "active";
    public const string Ended = "ended";
}

public static class CommandStatus
{
    public const string Pending = "pending";
    public const string Ack = "ack";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
}

public static class RequestStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class LedgerKind
{
    public const string Initial = "initial";
    public const string ParentGrant = "parent_grant";
    public const string ParentDeduct = "parent_deduct";
    public const string SessionCharge = "session_charge";
}

public static class EndReason
{
    public const string Logout = "logout";
    public const string Exhausted = "exhausted";
    public const string Admin = "admin";
    public const string MachineOff = "machine_off";
}
