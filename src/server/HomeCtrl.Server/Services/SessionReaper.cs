using HomeCtrl.Server.Data;
using HomeCtrl.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeCtrl.Server.Services;

/// <summary>
/// 兜底回收：机器心跳丢失超过 5 分钟 → 结束其活跃会话，
/// 计费截止于 lastSeenAt + 30s（心跳 10s 一跳，留 3 跳余量）。
/// </summary>
public class SessionReaper : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SessionReaper> _log;

    public SessionReaper(IServiceScopeFactory scopes, ILogger<SessionReaper> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var cutoff = DateTime.UtcNow.AddMinutes(-5);

                var active = db.Sessions
                    .Include(s => s.Person)
                    .Where(s => s.Status == SessionStatus.Active)
                    .ToList();

                foreach (var s in active)
                {
                    var machine = db.Machines.Find(s.MachineId);
                    if (machine?.LastSeenAt is not DateTime seen || seen >= cutoff) continue;

                    Billing.Settle(db, s, seen.AddSeconds(30));
                    Billing.End(db, s, EndReason.MachineOff);
                    _log.LogInformation("reaped session {SessionId} person {PersonId}", s.Id, s.PersonId);
                }
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "session reaper failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
