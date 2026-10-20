using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace WillBeyond.TimeBank.Agent;

/// <summary>
/// 命名管道 IPC 服务端：timebank.agent。
/// 协议：UTF-8 JSON 行（请求/响应 + 状态推送），见 design.md §7。
///
/// 结构（两个独立循环）：
///  - 接受循环：连接断开后【立即】重新监听，不阻塞
///  - 看门狗（D12）：会话进行中 && UI 断开超过 60s 未重连 → 自动结束会话停表
/// 与服务端交互的操作（登录/下机/申请/看门狗）经 AgentGate 与心跳串行化。
/// </summary>
public class PipeServerService : BackgroundService
{
    public const string PipeName = "timebank.agent";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly AgentState _state;
    private readonly ServerApiClient _api;
    private readonly AgentGate _gate;
    private readonly OfflineManager _offline;
    private readonly ILogger<PipeServerService> _log;
    private readonly int _watchdogSeconds;
    private readonly object _writeLock = new();
    private StreamWriter? _writer;                      // 当前连接的写入器
    private DateTime? _uiGoneSinceUtc;                  // UI 断开时刻（null=在线或从未连过）

    public PipeServerService(AgentState state, ServerApiClient api, AgentGate gate, OfflineManager offline,
        IConfiguration cfg, ILogger<PipeServerService> log)
    {
        _state = state;
        _api = api;
        _gate = gate;
        _offline = offline;
        _log = log;
        _watchdogSeconds = Math.Clamp(cfg.GetValue("UiWatchdogSeconds", 60), 10, 600);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _state.Changed += NotifyStateChanged;
        _ = AcceptLoopAsync(stoppingToken);
        _ = WatchdogLoopAsync(stoppingToken);
        return Task.CompletedTask;
    }

    /// <summary>接受连接并处理，断开后立即重新监听。</summary>
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                lock (_writeLock) _uiGoneSinceUtc = null;    // UI 回来了，看门狗停表

                var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                lock (_writeLock) _writer = writer;
                using var reader = new StreamReader(pipe, Encoding.UTF8);

                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;
                    if (line.Length == 0) continue;
                    var reply = await HandleAsync(line);
                    WriteLine(reply);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "管道连接异常");
            }
            finally
            {
                lock (_writeLock)
                {
                    _writer = null;
                    _uiGoneSinceUtc ??= DateTime.UtcNow; // 开始计时（若尚未在计时）
                }
                try { if (pipe.IsConnected) pipe.Disconnect(); } catch { }
                pipe.Dispose();
            }
        }
    }

    /// <summary>看门狗：会话活跃但 UI 消失超过宽限时间 → 结束会话（防杀 UI 白嫖）。</summary>
    private async Task WatchdogLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); }
            catch (OperationCanceledException) { break; }

            DateTime? goneSince;
            lock (_writeLock) goneSince = _uiGoneSinceUtc;

            if (goneSince is DateTime t
                && DateTime.UtcNow - t > TimeSpan.FromSeconds(_watchdogSeconds)
                && _state.Snapshot().SessionId is not null)
            {
                try
                {
                    await _gate.Sem.WaitAsync(ct);
                    try
                    {
                        if (_offline.IsOfflineMode)
                        {
                            // 离线模式：本地结束会话（服务器不可达，记录留待补账）
                            _offline.Logout();
                        }
                        else
                        {
                            await _api.PostAsync("/api/client/logout", new { });
                            _state.ApplyServerState(null, 0);
                        }
                    }
                    finally { _gate.Sem.Release(); }
                    _log.LogInformation("UI 断开超过 {Sec}s 且会话活跃，会话已自动结束", _watchdogSeconds);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "看门狗结束会话失败（服务器不可达？），稍后重试");
                }
                lock (_writeLock) _uiGoneSinceUtc = DateTime.UtcNow; // 重置计时，下轮再查
            }
        }
    }

    private void WriteLine(string json)
    {
        lock (_writeLock)
        {
            try { _writer?.WriteLine(json); }
            catch (IOException) { }
        }
    }

    private void NotifyStateChanged() =>
        WriteLine(JsonSerializer.Serialize(new { @event = "state", data = _state.Snapshot() }));

    private async Task<string> HandleAsync(string line)
    {
        var id = 0;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                id = idEl.GetInt32();
            var cmd = root.TryGetProperty("cmd", out var cmdEl) ? cmdEl.GetString() : null;

            switch (cmd)
            {
                case "getState":
                    return Reply(id, true, _state.Snapshot());

                case "login":
                {
                    var cardNo = root.TryGetProperty("cardNo", out var c) ? c.GetString() ?? "" : "";
                    var pin = root.TryGetProperty("pin", out var p) ? p.GetString() ?? "" : "";
                    await _gate.Sem.WaitAsync();
                    try
                    {
                        // 离线模式：本地校验 PIN 与离线额度
                        if (_offline.IsOfflineMode)
                        {
                            var (ok, reason) = _offline.TryLogin(cardNo, pin);
                            return Reply(id, true, new { ok, reason, offline = true });
                        }

                        var resp = await _api.PostAsync<LoginResp>("/api/client/login", new { cardNo, pin });
                        if (resp is not null && resp.Ok && resp.SessionId is not null)
                        {
                            _state.SetCardNo(cardNo);
                            _state.ApplyServerState(
                                new HeartbeatSession(resp.SessionId.Value, resp.Person ?? "", DateTime.UtcNow),
                                resp.BalanceSeconds);
                            _state.AdminLock(false);
                            return Reply(id, true, new { ok = true, person = resp.Person, balanceSeconds = resp.BalanceSeconds });
                        }
                        return Reply(id, true, new { ok = false, reason = resp?.Reason ?? "服务器无响应" });
                    }
                    finally { _gate.Sem.Release(); }
                }

                case "logout":
                {
                    await _gate.Sem.WaitAsync();
                    try
                    {
                        // 离线模式：本地记账（离线开始的会话）或打标重连补 logout（收养的会话）
                        if (_offline.IsOfflineMode)
                        {
                            _offline.Logout();
                            return Reply(id, true, new { ok = true, chargedSeconds = 0, offline = true });
                        }

                        var resp = await _api.PostAsync<LogoutResp>("/api/client/logout", new { });
                        _state.ApplyServerState(null, 0);
                        return Reply(id, true, new { ok = true, chargedSeconds = resp?.ChargedSeconds ?? 0 });
                    }
                    finally { _gate.Sem.Release(); }
                }

                case "requestTime":
                {
                    if (_offline.IsOfflineMode)
                        return Reply(id, true, new { ok = false, reason = "离线模式暂不可申请加时" });

                    var minutes = root.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Number
                        ? m.GetInt32() : 15;
                    await _gate.Sem.WaitAsync();
                    try
                    {
                        var resp = await _api.PostAsync<RequestResp>("/api/client/request", new { minutes });
                        return Reply(id, true, new { ok = resp is not null && resp.Ok, reason = resp?.Reason ?? "服务器无响应" });
                    }
                    finally { _gate.Sem.Release(); }
                }

                default:
                    return Reply(id, false, new { reason = "未知命令" });
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { id, ok = false, data = new { reason = ex.Message } });
        }
    }

    private static string Reply(int id, bool ok, object data) =>
        JsonSerializer.Serialize(new { id, ok, data });
}
