using System.Diagnostics;
using System.Text.Json;

namespace HomeCtrl.Agent;

/// <summary>心跳循环：上报状态 → 拉取会话/余额/指令 → 执行并确认。</summary>
public class HeartbeatWorker : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ServerApiClient _api;
    private readonly AgentState _state;
    private readonly ILogger<HeartbeatWorker> _log;
    private readonly int _intervalSeconds;

    public HeartbeatWorker(ServerApiClient api, AgentState state, IConfiguration cfg, ILogger<HeartbeatWorker> log)
    {
        _api = api;
        _state = state;
        _log = log;
        _intervalSeconds = Math.Clamp(cfg.GetValue("HeartbeatSeconds", 10), 5, 60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var resp = await _api.PostAsync<HeartbeatResp>("/api/client/heartbeat", new { });
                if (resp is not null)
                {
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
            catch (Exception ex)
            {
                _state.SetOnline(false);
                _log.LogWarning(ex, "心跳失败（服务器不可达？）");
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
