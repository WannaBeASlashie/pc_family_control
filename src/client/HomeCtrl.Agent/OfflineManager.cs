using System.Text.Json;

namespace HomeCtrl.Agent;

public sealed class OfflineActive
{
    public string RecordId { get; set; } = "";
    public string CardNo { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public long PersonQuotaLeft { get; set; }      // 个人当日离线额度剩余（登录时点）
    public long MachineQuotaLeft { get; set; }     // 机器当日离线额度剩余
    public long BalanceAtStart { get; set; }       // 快照余额扣减未补账用量
    public DateTime? ExhaustedAtUtc { get; set; }
}

public sealed class OfflineDone
{
    public string RecordId { get; set; } = "";
    public string CardNo { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public long ChargedSeconds { get; set; }
    public string? Reason { get; set; }
}

public sealed class OfflineLedgerFile
{
    public string? Today { get; set; }
    public Dictionary<string, long>? PersonUsage { get; set; }   // "yyyy-MM-dd|card" -> 秒
    public long MachineUsage { get; set; }
    public List<OfflineDone>? Pending { get; set; }
    public OfflineActive? Active { get; set; }
    public DateTime LastAliveUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 离线额度管理（FR-17，design-offline.md）：
///  - 持久化服务端下发的 offlineKit（ProgramData\HomeCtrl\offline\kit.json）
///  - 心跳连续失败 ≥2 次 → 离线模式：本地 PIN 校验 + 本地额度判断 + 本地倒计时
///  - 离线会话写本地台账（ledger.json），耗尽 → 本地锁屏+宽限+注销
///  - 重连时由 HeartbeatWorker 调 BuildSyncPayload 上报，服务器补账
/// 线程模型：公开方法自带 _gate；心跳路径的方法由 HeartbeatWorker 在 AgentGate 内调用。
/// </summary>
public class OfflineManager
{
    private const int LocalGraceSeconds = 300;    // 与服务端 Policy:GraceSeconds 对齐
    private readonly AgentState _state;
    private readonly ILogger<OfflineManager> _log;
    private readonly object _gate = new();

    private readonly string _dir;
    private readonly string _kitPath;
    private readonly string _ledgerPath;

    private OfflineKit? _kit;
    private string _kitHash = "";

    private int _failCount;
    private bool _offlineMode;
    private DateTime _lastPersist = DateTime.MinValue;

    private string _today = "";
    private Dictionary<string, long> _personUsage = new();
    private long _machineUsage;
    private List<OfflineDone> _pending = new();
    private OfflineActive? _active;
    private bool _localEndedFlag;                 // 在线开始的会话在离线中被本地结束，重连后补 logout

    // 收养的在线会话（服务器断开时已在机上机）：进入离线时刻起本地继续倒计时
    private long _adoptedRemaining;
    private DateTime _adoptedSince;

    public OfflineManager(AgentState state, ILogger<OfflineManager> log)
    {
        _state = state;
        _log = log;
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HomeCtrl", "offline");
        _kitPath = Path.Combine(_dir, "kit.json");
        _ledgerPath = Path.Combine(_dir, "ledger.json");
        LoadLedger();
    }

    public bool IsOfflineMode { get { lock (_gate) return _offlineMode; } }
    public bool HasPending { get { lock (_gate) return _pending.Count > 0 || _active != null; } }

    public bool ConsumeLocalEndedFlag()
    {
        lock (_gate)
        {
            var f = _localEndedFlag;
            _localEndedFlag = false;
            return f;
        }
    }

    // ---------- 心跳回调（HeartbeatWorker 调用）----------

    public void OnHeartbeatOk(OfflineKit? kit)
    {
        lock (_gate)
        {
            _failCount = 0;
            _offlineMode = false;
            UpdateKit(kit);
        }
    }

    public void OnHeartbeatFail() { lock (_gate) _failCount++; }

    public bool ShouldEnterOffline() { lock (_gate) return !_offlineMode && _failCount >= 2; }

    public void EnterOffline()
    {
        lock (_gate)
        {
            _offlineMode = true;
            var st = _state.Snapshot();
            if (st.SessionId is > 0)   // 收养在线会话（>0 为真实会话，-1 是离线哨兵）
            {
                _adoptedRemaining = st.BalanceSeconds;
                _adoptedSince = DateTime.UtcNow;
            }
            _log.LogWarning("进入离线模式（kit: {HasKit}, 人员 {Count}）", _kit != null, _kit?.Persons?.Count ?? 0);
        }
    }

    // ---------- 离线登录 / 下机（PipeServerService 在 AgentGate 内调用）----------

    public (bool Ok, string? Reason) TryLogin(string cardNo, string pin)
    {
        lock (_gate)
        {
            if (_state.Snapshot().SessionId != null)
                return (false, "本机已有上机会话");
            if (_kit is null || _kit.Persons is null || _kit.Persons.Count == 0)
                return (false, "离线档案不可用，请联系家长");
            if (DateTime.UtcNow - _kit.GeneratedAt > TimeSpan.FromHours(Math.Max(_kit.MaxAgeHours, 1)))
                return (false, "离线档案已过期，请联系家长");

            var p = _kit.Persons.FirstOrDefault(x => x.CardNo == cardNo);
            if (p is null || !PinVerifier.Verify(pin, p.PinHash))
                return (false, "卡号或 PIN 错误");
            if (p.Quota <= 0)
                return (false, "当前账户不允许离线上机");

            RollDate();
            var used = _personUsage.GetValueOrDefault($"{_today}|{cardNo}");
            if (used >= p.Quota)
                return (false, "今日离线额度已用完");
            if (_machineUsage >= _kit.MachineDailySeconds)
                return (false, "本机今日离线额度已用完");

            var balanceLeft = p.Balance - UnsyncedUsage(cardNo);
            if (balanceLeft <= 0)
                return (false, "余额不足，请联系家长");

            _active = new OfflineActive
            {
                RecordId = Guid.NewGuid().ToString("N"),
                CardNo = cardNo,
                DisplayName = string.IsNullOrEmpty(p.DisplayName) ? cardNo : p.DisplayName,
                StartedAtUtc = DateTime.UtcNow,
                PersonQuotaLeft = p.Quota - used,
                MachineQuotaLeft = _kit.MachineDailySeconds - _machineUsage,
                BalanceAtStart = balanceLeft
            };
            PersistLedger();
            _state.SetCardNo(cardNo);
            PushActiveState();
            _log.LogInformation("离线上机：{Card}（余额上限 {Bal}s，个人额度剩 {Pq}s，机器额度剩 {Mq}s）",
                cardNo, balanceLeft, _active.PersonQuotaLeft, _active.MachineQuotaLeft);
            return (true, null);
        }
    }

    /// <summary>离线下机：离线开始的会话本地记账；收养的在线会话打标，重连后向服务器补 logout。</summary>
    public void Logout()
    {
        lock (_gate)
        {
            if (_active != null)
            {
                FinalizeActive("logout");
                _state.ApplyServerState(null, 0);
            }
            else if (_state.Snapshot().SessionId is > 0)
            {
                _localEndedFlag = true;
                _state.ApplyServerState(null, 0);
            }
        }
    }

    // ---------- 离线倒计时（HeartbeatWorker 每轮调用，AgentGate 内）----------

    public void Tick()
    {
        lock (_gate)
        {
            if (!_offlineMode) return;
            var st = _state.Snapshot();

            if (_active != null)
            {
                var remaining = ActiveRemaining();
                if (remaining > 0)
                {
                    PushActiveState();
                    MaybePersist();
                    return;
                }

                // 耗尽：锁屏 → 宽限 → 本地注销（与在线 UX 一致）
                _active.ExhaustedAtUtc ??= DateTime.UtcNow;
                _state.ApplyServerState(new HeartbeatSession(-1, _active.DisplayName, _active.StartedAtUtc), 0);
                if (DateTime.UtcNow - _active.ExhaustedAtUtc > TimeSpan.FromSeconds(LocalGraceSeconds))
                {
                    var card = _active.CardNo;
                    FinalizeActive("exhausted");
                    Win32.LogoffActiveSession();
                    _state.ApplyServerState(null, 0);
                    _log.LogInformation("离线会话耗尽，已本地注销（{Card}）", card);
                }
                return;
            }

            if (st.SessionId is > 0)   // 收养的在线会话：本地倒计时，耗尽仅锁屏（logoff 交给服务器恢复后的正常流程）
            {
                var remaining = _adoptedRemaining - (long)(DateTime.UtcNow - _adoptedSince).TotalSeconds;
                if (remaining <= 0)
                {
                    _localEndedFlag = true;
                    _state.ApplyServerState(
                        new HeartbeatSession(st.SessionId.Value, st.PersonName ?? "", DateTime.UtcNow), 0);
                }
                else
                {
                    _state.ApplyServerState(
                        new HeartbeatSession(st.SessionId.Value, st.PersonName ?? "", DateTime.UtcNow),
                        remaining);
                }
            }
        }
    }

    // ---------- 重连补账（HeartbeatWorker 调用）----------

    public object? BuildSyncPayload()
    {
        lock (_gate)
        {
            if (_pending.Count == 0 && _active is null) return null;
            return new
            {
                completed = _pending.Select(r => new
                {
                    recordId = r.RecordId,
                    cardNo = r.CardNo,
                    startedAtUtc = r.StartedAtUtc,
                    endedAtUtc = r.EndedAtUtc,
                    chargedSeconds = r.ChargedSeconds,
                    endReason = r.Reason
                }).ToList(),
                active = _active is null ? null : new
                {
                    recordId = _active.RecordId,
                    cardNo = _active.CardNo,
                    startedAtUtc = _active.StartedAtUtc
                }
            };
        }
    }

    /// <summary>补账成功：清待补账记录；进行中的离线会话转由服务器会话驱动（随后的心跳负载接管）。</summary>
    public void OnSyncAccepted()
    {
        lock (_gate)
        {
            _pending.Clear();
            _active = null;
            _adoptedRemaining = 0;
            PersistLedger();
            _log.LogInformation("离线补账完成");
        }
    }

    // ---------- 内部 ----------

    private long ActiveRemaining()
    {
        var elapsed = (long)(DateTime.UtcNow - _active!.StartedAtUtc).TotalSeconds;
        return Math.Min(Math.Min(_active.BalanceAtStart - elapsed, _active.PersonQuotaLeft - elapsed),
            _active.MachineQuotaLeft - elapsed);
    }

    private void PushActiveState() =>
        _state.ApplyServerState(
            new HeartbeatSession(-1, _active!.DisplayName, _active.StartedAtUtc),
            Math.Max(ActiveRemaining(), 0));

    private void FinalizeActive(string reason)
    {
        var end = DateTime.UtcNow;
        var charged = (long)(end - _active!.StartedAtUtc).TotalSeconds;
        _pending.Add(new OfflineDone
        {
            RecordId = _active.RecordId, CardNo = _active.CardNo,
            StartedAtUtc = _active.StartedAtUtc, EndedAtUtc = end,
            ChargedSeconds = charged, Reason = reason
        });
        RollDate();
        _personUsage[$"{_today}|{_active.CardNo}"] = _personUsage.GetValueOrDefault($"{_today}|{_active.CardNo}") + charged;
        _machineUsage += charged;
        _active = null;
        PersistLedger();
    }

    private long UnsyncedUsage(string cardNo)
    {
        var done = _pending.Where(r => r.CardNo == cardNo).Sum(r => r.ChargedSeconds);
        var activeNow = _active?.CardNo == cardNo
            ? (long)(DateTime.UtcNow - _active.StartedAtUtc).TotalSeconds : 0;
        return done + activeNow;
    }

    private void RollDate()
    {
        var today = DateTime.UtcNow.AddHours(_kit?.TzOffsetHours ?? 8).ToString("yyyy-MM-dd");
        if (today != _today)
        {
            _today = today;
            _personUsage.Clear();
            _machineUsage = 0;
        }
    }

    private void UpdateKit(OfflineKit? kit)
    {
        if (kit?.Persons is null || kit.Persons.Count == 0) return;
        var hash = JsonSerializer.Serialize(kit.Persons);
        _kit = kit;
        if (hash == _kitHash) return;
        _kitHash = hash;
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_kitPath, JsonSerializer.Serialize(kit));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "离线 kit 持久化失败");
        }
    }

    private void MaybePersist()
    {
        if ((DateTime.UtcNow - _lastPersist).TotalSeconds < 60) return;
        PersistLedger();
    }

    private void PersistLedger()
    {
        _lastPersist = DateTime.UtcNow;
        try
        {
            Directory.CreateDirectory(_dir);
            var f = new OfflineLedgerFile
            {
                Today = _today, PersonUsage = _personUsage, MachineUsage = _machineUsage,
                Pending = _pending, Active = _active, LastAliveUtc = DateTime.UtcNow
            };
            File.WriteAllText(_ledgerPath, JsonSerializer.Serialize(f));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "离线台账写入失败");
        }
    }

    private void LoadLedger()
    {
        try
        {
            if (!File.Exists(_ledgerPath)) return;
            var f = JsonSerializer.Deserialize<OfflineLedgerFile>(File.ReadAllText(_ledgerPath));
            if (f is null) return;
            _today = f.Today ?? "";
            _personUsage = f.PersonUsage ?? new Dictionary<string, long>();
            _machineUsage = f.MachineUsage;
            _pending = f.Pending ?? new List<OfflineDone>();

            if (f.Active != null)
            {
                // Agent 重启恢复：活动会话按“最后活跃+60s”估算结束，转为待补账
                var end = f.LastAliveUtc.AddSeconds(60);
                if (end <= f.Active.StartedAtUtc) end = f.Active.StartedAtUtc.AddSeconds(60);
                if (end > DateTime.UtcNow) end = DateTime.UtcNow;
                _pending.Add(new OfflineDone
                {
                    RecordId = f.Active.RecordId, CardNo = f.Active.CardNo,
                    StartedAtUtc = f.Active.StartedAtUtc, EndedAtUtc = end,
                    ChargedSeconds = (long)(end - f.Active.StartedAtUtc).TotalSeconds,
                    Reason = "agent_restart"
                });
                _log.LogInformation("恢复离线台账：活动会话按最后活跃+60s 估算结束（{Card}）", f.Active.CardNo);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "离线台账加载失败");
        }
    }
}
