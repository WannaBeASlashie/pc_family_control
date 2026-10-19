using HomeCtrl.Server.Data;
using HomeCtrl.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeCtrl.Server.Services;

/// <summary>计费与会话结算（服务端权威，见 design.md §4）。</summary>
public static class Billing
{
    /// <summary>把会话计费到指定时刻（幂等：按 ChargedSeconds 增量扣）。</summary>
    public static long Settle(AppDbContext db, Session session, DateTime endUtc)
    {
        var charge = (long)(endUtc - session.StartedAt).TotalSeconds - session.ChargedSeconds;
        if (charge <= 0) return 0;

        session.Person ??= db.Persons.Find(session.PersonId)!;
        session.Person.BalanceSeconds -= charge;
        if (session.Person.BalanceSeconds < 0) session.Person.BalanceSeconds = 0;
        session.ChargedSeconds += charge;
        return charge;
    }

    /// <summary>结束会话：写汇总流水 + 审计。</summary>
    public static void End(AppDbContext db, Session session, string reason)
    {
        session.Status = SessionStatus.Ended;
        session.EndedAt = DateTime.UtcNow;
        session.EndReason = reason;
        db.TimeLedger.Add(new TimeLedger
        {
            PersonId = session.PersonId,
            DeltaSeconds = -session.ChargedSeconds,
            Kind = LedgerKind.SessionCharge,
            Note = $"session:{session.Id} reason:{reason}",
            CreatedAt = DateTime.UtcNow
        });
        db.AuditLogs.Add(new AuditLog
        {
            Ts = DateTime.UtcNow,
            Actor = "server",
            Action = "session_end",
            Detail = $"session:{session.Id} person:{session.PersonId} reason:{reason} charged:{session.ChargedSeconds}"
        });
    }
}
