namespace HomeCtrl.Agent;

/// <summary>
/// Agent 内存状态机。locked = session==null || balance&lt;=0 || adminLock。
/// 所有变更触发 Changed 事件 → 管道推送给 UI。
/// </summary>
public class AgentState
{
    private readonly object _lock = new();

    public bool Locked { get; private set; } = true;
    public int? SessionId { get; private set; }
    public string? PersonName { get; private set; }
    public long BalanceSeconds { get; private set; }
    public bool ServerOnline { get; private set; }
    public bool Offline { get; private set; }
    public string? Message { get; private set; }
    public string? CardNo { get; private set; }
    private bool AdminLocked { get; set; }

    public event Action? Changed;

    public StateSnapshot Snapshot()
    {
        lock (_lock)
            return new StateSnapshot(Locked, SessionId, PersonName, BalanceSeconds, ServerOnline, Message, Offline);
    }

    public void SetOnline(bool online)
    {
        lock (_lock) ServerOnline = online;
        Changed?.Invoke();
    }

    public void SetOffline(bool offline)
    {
        lock (_lock) Offline = offline;
        Changed?.Invoke();
    }

    public void SetCardNo(string? cardNo)
    {
        lock (_lock) CardNo = cardNo;
    }

    public void SetMessage(string? message)
    {
        lock (_lock) Message = message;
        Changed?.Invoke();
    }

    /// <summary>以服务端心跳/登录结果刷新状态。</summary>
    public void ApplyServerState(HeartbeatSession? session, long balance)
    {
        lock (_lock)
        {
            SessionId = session?.Id;
            PersonName = session?.Person;
            BalanceSeconds = balance;
            if (session is null) AdminLocked = false;
            Locked = session is null || balance <= 0 || AdminLocked;
        }
        Changed?.Invoke();
    }

    /// <summary>家长远程 lock/unlock 指令。</summary>
    public void AdminLock(bool locked)
    {
        lock (_lock)
        {
            AdminLocked = locked;
            Locked = locked || SessionId is null || BalanceSeconds <= 0;
        }
        Changed?.Invoke();
    }
}
