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
                    // 最后联系时间：有心跳用心跳；从未心跳的机器（只调过 login 等接口）
                    // 用会话开始时间兜底，同样超时回收，防止僵尸会话永久挂起
                    var lastContact = machine?.LastSeenAt ?? s.StartedAt;
                    if (lastContact >= cutoff) continue;

                    Billing.Settle(db, s, lastContact.AddSeconds(30));
                    Billing.End(db, s, EndReason.MachineOff);
                    _log.LogInformation("reaped session {SessionId} person {PersonId}", s.Id, s.PersonId);
                }

                // ---- 数据保留清理（防表无限增长）----
                var now = DateTime.UtcNow;

                // 过期的 pending 指令标记为 expired（心跳已不再下发）
                var expired = db.Commands
                    .Where(c => c.Status == CommandStatus.Pending && c.ExpiresAt != null && c.ExpiresAt < now)
                    .ToList();
                foreach (var c in expired) c.Status = CommandStatus.Expired;

                // 已完成指令保留 7 天
                db.Commands.RemoveRange(db.Commands
                    .Where(c => c.Status != CommandStatus.Pending && c.CreatedAt < now.AddDays(-7)));

                // 审计日志保留 90 天
                db.AuditLogs.RemoveRange(db.AuditLogs.Where(a => a.Ts < now.AddDays(-90)));

                // 已处理的加时申请保留 90 天
                db.TimeRequests.RemoveRange(db.TimeRequests
                    .Where(r => r.Status != RequestStatus.Pending && r.DecidedAt != null && r.DecidedAt < now.AddDays(-90)));
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
