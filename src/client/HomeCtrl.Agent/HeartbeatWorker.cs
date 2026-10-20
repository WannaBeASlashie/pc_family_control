using System.Diagnostics;
using System.Text.Json;

namespace HomeCtrl.Agent;

/// <summary>
/// 心跳循环：上报状态 → 拉取会话/余额/指令 → 执行并确认。
/// 离线集成（FR-17）：连续失败 ≥2 次进入离线模式（本地倒计时由 OfflineManager.Tick 驱动）；
/// 恢复连接时先补账（sync-offline，其响应即为最新心跳负载），再回到在线流程。
/// </summary>
public class HeartbeatWorker : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ServerApiClient _api;
    private readonly AgentState _state;
    private readonly AgentGate _gate;
    private readonly OfflineManager _offline;
    private readonly ILogger<HeartbeatWorker> _log;
    private readonly int _intervalSeconds;

    public HeartbeatWorker(ServerApiClient api, AgentState state, AgentGate gate, OfflineManager offline,
        IConfiguration cfg, ILogger<HeartbeatWorker> log)
    {
        _api = api;
        _state = state;
        _gate = gate;
        _offline = offline;
        _log = log;
        _intervalSeconds = Math.Clamp(cfg.GetValue("HeartbeatSeconds", 10), 5, 60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 与登录/下机/看门狗串行化，防止旧会话状态覆盖新登录
                await _gate.Sem.WaitAsync(stoppingToken);
                try
                {
                    var resp = await _api.PostAsync<HeartbeatResp>("/api/client/heartbeat", new { });
                    if (resp is not null)
                    {
                        _offline.OnHeartbeatOk(resp.OfflineKit);

                        // 离线补账：有待补账记录时先 sync-offline，用其响应（含补建会话）作为本轮流载
                        if (_offline.HasPending)
                        {
                            try
                            {
                                var payload = _offline.BuildSyncPayload();
                                if (payload is not null)
                                {
                                    var sync = await _api.PostAsync<HeartbeatResp>("/api/client/sync-offline", payload);
                                    if (sync is not null)
                                    {
                                        resp = sync;
                                        _offline.OnSyncAccepted();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _log.LogWarning(ex, "离线补账失败，稍后重试");
                            }
                        }

                        // 离线期间被本地结束的在线会话：向服务器补 logout（按补账时刻结算，略保守）
                        if (_offline.ConsumeLocalEndedFlag())
                        {
                            try { await _api.PostAsync("/api/client/logout", new { }); } catch { }
                        }

                        _state.SetOffline(false);
                        _state.SetOnline(true);
                        _state.ApplyServerState(resp.Session, resp.BalanceSeconds);

                        foreach (var cmd in resp.Commands ?? new List<HeartbeatCommand>())
                        {
                            try
                            {
                                await ExecuteCommandAsync(cmd);
                            }
                            catch (Exception ex)
                            {
                                _log.LogError(ex, "执行指令 {Action} 失败", cmd.Action);
                            }
                            try
                            {
                                await _api.PostAsync("/api/client/command-ack", new { commandId = cmd.Id });
                            }
                            catch (Exception ex)
                            {
                                _log.LogWarning(ex, "指令 {Id} 确认失败", cmd.Id);
                            }
                        }
                    }
                }
                finally
                {
                    _gate.Sem.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _state.SetOnline(false);
                _offline.OnHeartbeatFail();
                if (_offline.ShouldEnterOffline())
                {
                    _offline.EnterOffline();
                    _state.SetOffline(true);
                }
                _log.LogWarning(ex, "心跳失败（服务器不可达？离线模式：{Offline}）", _offline.IsOfflineMode);
            }

            // 离线模式的本地倒计时/耗尽处置（与管道操作串行化）
            if (_offline.IsOfflineMode)
            {
                try
                {
                    await _gate.Sem.WaitAsync(stoppingToken);
                    try { _offline.Tick(); }
                    finally { _gate.Sem.Release(); }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ExecuteCommandAsync(HeartbeatCommand cmd)
    {
        switch (cmd.Action)
        {
            case "lock":
                _state.AdminLock(true);
                break;

            case "unlock":
                _state.AdminLock(false);
                break;

            case "logoff":
                // 强制下机：注销 Windows 会话 + 结束服务端计费
                Win32.LogoffActiveSession();
                try { await _api.PostAsync("/api/client/logout", new { }); } catch { }
                _state.ApplyServerState(null, 0);
                break;

            case "shutdown":
                Process.Start("shutdown", "/s /t 5");
                break;

            case "reboot":
                Process.Start("shutdown", "/r /t 5");
                break;

            case "message":
                var text = ExtractText(cmd.Payload);
                if (!string.IsNullOrEmpty(text)) _state.SetMessage(text);
                break;
        }
    }

    private static string? ExtractText(string? payload)
    {
        try
        {
            if (string.IsNullOrEmpty(payload)) return null;
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : payload;
        }
        catch
        {
            return payload;
        }
    }
}
