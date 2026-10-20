using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WillBeyond.TimeBank.Server.Data;
using WillBeyond.TimeBank.Server.Models;
using WillBeyond.TimeBank.Server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=timebank.db"));
builder.Services.AddSingleton<AdminAuth>();
builder.Services.AddHostedService<SessionReaper>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    // 种子：默认策略（不限额度、全天可用），家长可在控制台调整或新建
    if (!db.Policies.Any())
    {
        db.Policies.Add(new Policy
        {
            Name = "Default (unlimited)",
            AllowedWindowsJson = "[{\"start\":\"00:00\",\"end\":\"24:00\"}]",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }
}

// 家长 Web 控制台（wwwroot/index.html）
app.UseDefaultFiles();
app.UseStaticFiles();

if ((app.Configuration["Admin:Password"] ?? "changeme") == "changeme")
{
    app.Logger.LogWarning("==========================================================");
    app.Logger.LogWarning(" Admin password is still the default 'changeme' - set Admin__Password!");
    app.Logger.LogWarning("==========================================================");
}

var loginFailures = new ConcurrentDictionary<string, FailState>();

// ---------- 辅助函数 ----------

static string Hmac(string secret, string data) =>
    Convert.ToHexString(new HMACSHA256(Encoding.UTF8.GetBytes(secret))
        .ComputeHash(Encoding.UTF8.GetBytes(data)));

static Machine? AuthMachine(HttpContext ctx, AppDbContext db)
{
    var idText = ctx.Request.Headers["X-Machine-Id"].ToString();
    var tsText = ctx.Request.Headers["X-Timestamp"].ToString();
    var sigText = ctx.Request.Headers["X-Signature"].ToString();
    if (idText.Length == 0 || tsText.Length == 0 || sigText.Length == 0) return null;
    if (!int.TryParse(idText, out var id) || !long.TryParse(tsText, out var ts)) return null;
    if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts) > 120) return null;

    var machine = db.Machines.FirstOrDefault(m => m.Id == id);
    if (machine is null) return null;

    var expected = Hmac(machine.Secret, $"{ts}:{ctx.Request.Path}");
    return string.Equals(expected, sigText, StringComparison.OrdinalIgnoreCase) ? machine : null;
}

static string HashPin(string pin)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);
    return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}";
}

static bool VerifyPin(string pin, string stored)
{
    var parts = stored.Split(':');
    if (parts.Length != 2) return false;
    var salt = Convert.FromHexString(parts[0]);
    var expected = Convert.FromHexString(parts[1]);
    var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);
    return CryptographicOperations.FixedTimeEquals(actual, expected);
}

static string? Bearer(HttpContext ctx)
{
    var header = ctx.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? header["Bearer ".Length..].Trim()
        : null;
}

static Command NewCmd(int machineId, string action, string payload, int ttlMinutes) => new()
{
    MachineId = machineId,
    Action = action,
    Payload = payload,
    CreatedAt = DateTime.UtcNow,
    ExpiresAt = DateTime.UtcNow.AddMinutes(ttlMinutes)
};

static void CancelPending(AppDbContext db, int machineId, params string[] actions)
{
    var rows = db.Commands
        .Where(c => c.MachineId == machineId && c.Status == CommandStatus.Pending && actions.Contains(c.Action))
        .ToList();
    foreach (var r in rows) r.Status = CommandStatus.Cancelled;
}

// ---------- 健康检查 ----------

app.MapGet("/health", () => "TimeBank Server");

// ================= 客户端（受控机）接口 =================

// 心跳/补账共用的负载构造：结算 + 策略 + 锁定指令 + 待执行指令 + 离线 kit
object BuildHeartbeatPayload(AppDbContext db, Machine machine)
{
    object? sessionInfo = null;
    var balance = 0L;
    var session = db.Sessions.Include(s => s.Person)
        .FirstOrDefault(s => s.MachineId == machine.Id && s.Status == SessionStatus.Active);

    if (session is not null)
    {
        // 服务端权威计费：按 startedAt 推算增量，并立即落库（下方额度计算需要读到最新计费）
        Billing.Settle(db, session, DateTime.UtcNow);
        db.SaveChanges();
        sessionInfo = new { id = session.Id, person = session.Person!.DisplayName, startedAt = session.StartedAt };

        // 策略叠加：可用时段 / 每日额度 / 单次上限 → 取最小为有效余额
        var tz = app.Configuration.GetValue("Policy:TzOffsetHours", 8);
        var (effective, outside, limitReason) = PolicyEngine.Evaluate(db, session.Person!, session, DateTime.UtcNow, tz);
        balance = Math.Max(effective, 0);

        if (outside || effective <= 0)
        {
            session.ExhaustedAt ??= DateTime.UtcNow;
            if (session.LockSentAt is null)
            {
                session.LockSentAt = DateTime.UtcNow;
                var lockReason = outside ? "curfew" : limitReason ?? "exhausted";
                db.Commands.Add(NewCmd(machine.Id, "lock", JsonSerializer.Serialize(new { reason = lockReason }), 10));
            }
            var grace = app.Configuration.GetValue("Policy:GraceSeconds", 300);
            if (session.LogoffSentAt is null && DateTime.UtcNow - session.ExhaustedAt > TimeSpan.FromSeconds(grace))
            {
                session.LogoffSentAt = DateTime.UtcNow;
                db.Commands.Add(NewCmd(machine.Id, "logoff", "{\"reason\":\"grace_expired\"}", 5));
            }
        }
        else
        {
            // 有效余额恢复（充值/额度重算）→ 重置标记，下次耗尽重新走 提醒→锁→宽限 流程
            session.ExhaustedAt = null;
            session.LockSentAt = null;
            session.LogoffSentAt = null;
        }
    }

    // Save BEFORE querying pending commands: commands added above (lock/logoff)
    // must be visible to this heartbeat's response, not the next one.
    db.SaveChanges();

    var commands = db.Commands
        .Where(c => c.MachineId == machine.Id
                    && c.Status == CommandStatus.Pending
                    && (c.ExpiresAt == null || c.ExpiresAt > DateTime.UtcNow))
        .Select(c => new { id = c.Id, action = c.Action, payload = c.Payload })
        .ToList();

    // 离线 kit：全部启用账户的离线档案（FR-17，Agent 断网时用于受限上机）
    object? kit = null;
    if (app.Configuration.GetValue("Offline:Enabled", true))
    {
        var polCache = db.Policies.ToList().ToDictionary(p => p.Id);
        var defQuota = app.Configuration.GetValue("Offline:PersonDailySeconds", 1800L);
        var kitPersons = db.Persons.Where(p => p.Enabled).ToList()
            .Select(p =>
            {
                polCache.TryGetValue(p.PolicyId, out var pol);
                return new
                {
                    cardNo = p.CardNo,
                    displayName = p.DisplayName,
                    pinHash = p.PinHash,
                    balance = p.BalanceSeconds,
                    quota = pol?.OfflineQuotaSeconds ?? defQuota
                };
            })
            .ToList();
        kit = new
        {
            generatedAt = DateTime.UtcNow,
            maxAgeHours = app.Configuration.GetValue("Offline:KitMaxAgeHours", 72),
            tzOffsetHours = app.Configuration.GetValue("Policy:TzOffsetHours", 8),
            machineDailySeconds = app.Configuration.GetValue("Offline:MachineDailySeconds", 5400L),
            persons = kitPersons
        };
    }

    return new { serverTime = DateTime.UtcNow, session = sessionInfo, balanceSeconds = balance, commands, offlineKit = kit };
}

app.MapPost("/api/client/heartbeat", (HttpContext ctx, AppDbContext db) =>
{
    var machine = AuthMachine(ctx, db);
    if (machine is null) return Results.Unauthorized();

    machine.LastSeenAt = DateTime.UtcNow;
    db.SaveChanges();
    return Results.Json(BuildHeartbeatPayload(db, machine));
});

// 离线补账（FR-17）：Agent 重连时上报离线会话；按历史时间建会话行并扣费。
// 已结束的补建 ended 会话；进行中的补建 active 会话（心跳结算自然接管，含回溯时长）。
// 幂等：ClientRecordId 唯一；防篡改：早于 48h 的开始时间钳到当前，单条计费钳到额度×1.5。
app.MapPost("/api/client/sync-offline", (SyncOfflineReq req, HttpContext ctx, AppDbContext db) =>
{
    var machine = AuthMachine(ctx, db);
    if (machine is null) return Results.Unauthorized();
    machine.LastSeenAt = DateTime.UtcNow;

    var defQuota = app.Configuration.GetValue("Offline:PersonDailySeconds", 1800L);
    var polCache = db.Policies.ToList().ToDictionary(p => p.Id);

    foreach (var c in req.Completed ?? new List<OfflineCompletedReq>())
    {
        if (string.IsNullOrEmpty(c.RecordId) || db.Sessions.Any(s => s.ClientRecordId == c.RecordId)) continue;
        var person = db.Persons.FirstOrDefault(p => p.CardNo == c.CardNo);
        if (person is null)
        {
            db.AuditLogs.Add(new AuditLog { Ts = DateTime.UtcNow, Actor = $"machine:{machine.Id}", Action = "offline_sync", Detail = $"unknown card {c.CardNo}, skipped" });
            continue;
        }

        var start = c.StartedAtUtc;
        var end = c.EndedAtUtc;
        var clamped = false;
        if (start < DateTime.UtcNow.AddDays(-2)) { start = DateTime.UtcNow; clamped = true; }
        if (end < start) end = start;
        if (end > DateTime.UtcNow) end = DateTime.UtcNow;

        polCache.TryGetValue(person.PolicyId, out var pol);
        var quota = pol?.OfflineQuotaSeconds ?? defQuota;
        var chargeCap = Math.Max(quota, defQuota) * 3 / 2;
        var span = (long)(end - start).TotalSeconds;
        var charged = Math.Min(span, chargeCap);
        if (c.ChargedSeconds > 0) charged = Math.Min(charged, c.ChargedSeconds);

        db.Sessions.Add(new Session
        {
            PersonId = person.Id, MachineId = machine.Id,
            StartedAt = start, EndedAt = end, ChargedSeconds = charged,
            EndReason = "offline_" + (c.EndReason ?? "sync"),
            Status = SessionStatus.Ended, ClientRecordId = c.RecordId
        });
        person.BalanceSeconds = Math.Max(person.BalanceSeconds - charged, 0);
        db.TimeLedger.Add(new TimeLedger
        {
            PersonId = person.Id, DeltaSeconds = -charged, Kind = LedgerKind.OfflineCharge,
            Note = $"offline sync {c.RecordId}", CreatedAt = DateTime.UtcNow
        });
        db.AuditLogs.Add(new AuditLog
        {
            Ts = DateTime.UtcNow, Actor = $"machine:{machine.Id}", Action = "offline_sync",
            Detail = $"person:{person.Id} charged:{charged} clamped:{clamped}"
        });
    }

    if (req.Active is { } a && !string.IsNullOrEmpty(a.RecordId)
        && !db.Sessions.Any(s => s.ClientRecordId == a.RecordId))
    {
        var person = db.Persons.FirstOrDefault(p => p.CardNo == a.CardNo);
        if (person is not null)
        {
            var start = a.StartedAtUtc;
            var clamped = false;
            if (start < DateTime.UtcNow.AddDays(-2)) { start = DateTime.UtcNow; clamped = true; }
            db.Sessions.Add(new Session
            {
                PersonId = person.Id, MachineId = machine.Id,
                StartedAt = start, Status = SessionStatus.Active, ClientRecordId = a.RecordId
            });
            db.AuditLogs.Add(new AuditLog
            {
                Ts = DateTime.UtcNow, Actor = $"machine:{machine.Id}", Action = "offline_sync",
                Detail = $"person:{person.Id} active session, clamped:{clamped}"
            });
        }
    }

    db.SaveChanges();
    return Results.Json(BuildHeartbeatPayload(db, machine));
});

app.MapPost("/api/client/login", (LoginReq req, HttpContext ctx, AppDbContext db) =>
{
    var machine = AuthMachine(ctx, db);
    if (machine is null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(req.CardNo) || string.IsNullOrWhiteSpace(req.Pin))
        return Results.Json(new { ok = false, reason = "Card number and PIN are required" });

    var st = loginFailures.GetOrAdd(req.CardNo, _ => new FailState(0, DateTime.MinValue, DateTime.MinValue));
    if (DateTime.UtcNow < st.LockedUntil)
        return Results.Json(new { ok = false, reason = "Too many attempts, try again in 1 minute" });

    // 失败计数衰减：10 分钟前的失败不再累计
    var count = DateTime.UtcNow - st.LastFailAt > TimeSpan.FromMinutes(10) ? 0 : st.Count;

    var person = db.Persons.FirstOrDefault(p => p.CardNo == req.CardNo);
    if (person is null || !VerifyPin(req.Pin, person.PinHash))
    {
        count += 1;
        var lockedUntil = count >= 5 ? DateTime.UtcNow.AddMinutes(1) : DateTime.MinValue;
        // 触发锁定时清零计数：锁定期满后重新获得完整 5 次机会
        loginFailures[req.CardNo] = new FailState(
            lockedUntil > DateTime.MinValue ? 0 : count,
            lockedUntil,
            DateTime.UtcNow);
        return Results.Json(new { ok = false, reason = "Invalid card number or PIN" });
    }
    loginFailures.TryRemove(req.CardNo, out _);

    if (!person.Enabled)
        return Results.Json(new { ok = false, reason = "Account disabled" });

    // 策略检查：可用时段 + 每日额度（含余额判断）
    var tz = app.Configuration.GetValue("Policy:TzOffsetHours", 8);
    var (eff, outsideWin, _) = PolicyEngine.Evaluate(db, person, null, DateTime.UtcNow, tz);
    if (outsideWin)
        return Results.Json(new { ok = false, reason = "Outside allowed hours" });
    if (eff <= 0)
        return Results.Json(new { ok = false, reason = person.BalanceSeconds <= 0 ? "Insufficient balance, contact your parent" : "Daily quota used up, come back tomorrow" });
    if (db.Sessions.Any(s => s.PersonId == person.Id && s.Status == SessionStatus.Active))
        return Results.Json(new { ok = false, reason = "Account already in use on another PC" });
    if (db.Sessions.Any(s => s.MachineId == machine.Id && s.Status == SessionStatus.Active))
        return Results.Json(new { ok = false, reason = "This PC already has an active session" });

    var session = new Session
    {
        PersonId = person.Id,
        MachineId = machine.Id,
        StartedAt = DateTime.UtcNow,
        Status = SessionStatus.Active
    };
    db.Sessions.Add(session);
    db.AuditLogs.Add(new AuditLog
    {
        Ts = DateTime.UtcNow, Actor = $"machine:{machine.Id}",
        Action = "login", Detail = $"person:{person.Id} card:{person.CardNo}"
    });
    db.SaveChanges();

    return Results.Json(new { ok = true, sessionId = session.Id, person = person.DisplayName, balanceSeconds = Math.Max(eff, 0) });
});

app.MapPost("/api/client/logout", (HttpContext ctx, AppDbContext db) =>
{
    var machine = AuthMachine(ctx, db);
    if (machine is null) return Results.Unauthorized();

    var session = db.Sessions.Include(s => s.Person)
        .FirstOrDefault(s => s.MachineId == machine.Id && s.Status == SessionStatus.Active);

    long charged = 0;
    if (session is not null)
    {
        Billing.Settle(db, session, DateTime.UtcNow);
        charged = session.ChargedSeconds;
        Billing.End(db, session, EndReason.Logout);
        db.SaveChanges();
    }
    return Results.Json(new { ok = true, chargedSeconds = charged });
});

app.MapPost("/api/client/command-ack", (AckReq req, HttpContext ctx, AppDbContext db) =>
{
    var machine = AuthMachine(ctx, db);
    if (machine is null) return Results.Unauthorized();

    var cmd = db.Commands.FirstOrDefault(c => c.Id == req.CommandId && c.MachineId == machine.Id);
    if (cmd is not null)
    {
        cmd.Status = CommandStatus.Ack;
        cmd.AckedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
    return Results.Json(new { ok = true });
});

// 孩子发起加时申请（FR-11a：锁屏上的「申请加时」按钮走这里）
app.MapPost("/api/client/request", (RequestTimeReq req, HttpContext ctx, AppDbContext db) =>
{
    var machine = AuthMachine(ctx, db);
    if (machine is null) return Results.Unauthorized();

    var session = db.Sessions.Include(s => s.Person)
        .FirstOrDefault(s => s.MachineId == machine.Id && s.Status == SessionStatus.Active);
    if (session is null)
        return Results.Json(new { ok = false, reason = "No active session" });

    if (db.TimeRequests.Any(r => r.PersonId == session.PersonId && r.Status == RequestStatus.Pending))
        return Results.Json(new { ok = false, reason = "A time request is already pending" });

    var minutes = Math.Clamp(req.Minutes, 5, 120);
    var request = new TimeRequest
    {
        PersonId = session.PersonId,
        MachineId = machine.Id,
        Minutes = minutes,
        Status = RequestStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };
    db.TimeRequests.Add(request);
    db.AuditLogs.Add(new AuditLog
    {
        Ts = DateTime.UtcNow, Actor = $"person:{session.PersonId}",
        Action = "time_request", Detail = $"machine:{machine.Id} +{minutes}min"
    });
    db.SaveChanges();
    return Results.Json(new { ok = true, requestId = request.Id, minutes });
});

// ================= 管理端接口 =================

app.MapPost("/api/admin/login", (AdminLoginReq req, AdminAuth auth, IConfiguration cfg) =>
{
    var pwd = cfg["Admin:Password"] ?? "changeme";
    if (string.IsNullOrEmpty(req.Password) || req.Password != pwd)
        return Results.Unauthorized();
    return Results.Json(new { token = auth.Issue() });
});

app.MapGet("/api/admin/overview", (HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();

    var machines = db.Machines.ToList().Select(m => new
    {
        m.Id, m.Name, m.LastSeenAt,
        online = m.LastSeenAt is DateTime seen && (DateTime.UtcNow - seen).TotalSeconds < 30
    });
    var sessions = db.Sessions.Include(s => s.Person).Include(s => s.Machine)
        .Where(s => s.Status == SessionStatus.Active)
        .Select(s => new { s.Id, person = s.Person!.DisplayName, machine = s.Machine!.Name, s.StartedAt, s.ChargedSeconds })
        .ToList();
    var persons = db.Persons
        .Select(p => new { p.Id, p.CardNo, p.DisplayName, p.Enabled, p.BalanceSeconds, p.Points, p.PolicyId })
        .ToList();
    return Results.Json(new
    {
        machines,
        activeSessions = sessions,
        persons,
        pendingRequests = db.TimeRequests.Count(r => r.Status == RequestStatus.Pending)
    });
});

app.MapGet("/api/admin/persons", (HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    var persons = db.Persons
        .Select(p => new { p.Id, p.CardNo, p.DisplayName, p.Enabled, p.BalanceSeconds, p.Points, p.PolicyId, p.CreatedAt })
        .ToList();
    return Results.Json(persons);
});

app.MapPost("/api/admin/persons", (CreatePersonReq req, HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.CardNo) || string.IsNullOrWhiteSpace(req.Pin) || string.IsNullOrWhiteSpace(req.DisplayName))
        return Results.Json(new { ok = false, reason = "Card number, name and PIN are required" });
    if (db.Persons.Any(p => p.CardNo == req.CardNo))
        return Results.Json(new { ok = false, reason = "Card number already exists" });

    var person = new Person
    {
        CardNo = req.CardNo,
        DisplayName = req.DisplayName,
        PinHash = HashPin(req.Pin),
        BalanceSeconds = Math.Max(req.InitialSeconds, 0),
        PolicyId = req.PolicyId ?? 1,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    db.Persons.Add(person);
    db.SaveChanges();

    if (person.BalanceSeconds > 0)
    {
        db.TimeLedger.Add(new TimeLedger
        {
            PersonId = person.Id, DeltaSeconds = person.BalanceSeconds,
            Kind = LedgerKind.Initial, Note = "initial balance", CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }
    return Results.Json(new { ok = true, id = person.Id });
});

app.MapPut("/api/admin/persons/{id}", (int id, UpdatePersonReq req, HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    var person = db.Persons.Find(id);
    if (person is null) return Results.NotFound();

    if (req.DisplayName is not null) person.DisplayName = req.DisplayName;
    if (req.Pin is not null) person.PinHash = HashPin(req.Pin);
    if (req.Enabled is not null) person.Enabled = req.Enabled.Value;
    if (req.PolicyId is not null) person.PolicyId = req.PolicyId.Value;
    person.UpdatedAt = DateTime.UtcNow;
    db.SaveChanges();
    return Results.Json(new { ok = true });
});

app.MapDelete("/api/admin/persons/{id}", (int id, HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    var person = db.Persons.Find(id);
    if (person is null) return Results.NotFound();
    if (db.Sessions.Any(s => s.PersonId == id && s.Status == SessionStatus.Active))
        return Results.Json(new { ok = false, reason = "Account is in use, force logoff first" });

    db.Persons.Remove(person);
    db.SaveChanges();
    return Results.Json(new { ok = true });
});

/// <summary>家长直接充值/扣减时长（秒）。若该账户正因余额耗尽被锁屏，充值后自动下发解锁指令。</summary>
app.MapPost("/api/admin/persons/{id}/balance", (int id, BalanceReq req, HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    var person = db.Persons.Find(id);
    if (person is null) return Results.NotFound();
    if (req.DeltaSeconds == 0)
        return Results.Json(new { ok = true, balanceSeconds = person.BalanceSeconds });

    person.BalanceSeconds = Math.Max(person.BalanceSeconds + req.DeltaSeconds, 0);
    db.TimeLedger.Add(new TimeLedger
    {
        PersonId = person.Id,
        DeltaSeconds = req.DeltaSeconds,
        Kind = req.DeltaSeconds > 0 ? LedgerKind.ParentGrant : LedgerKind.ParentDeduct,
        Note = req.Note,
        CreatedAt = DateTime.UtcNow
    });

    var session = db.Sessions.FirstOrDefault(s => s.PersonId == person.Id && s.Status == SessionStatus.Active);
    if (session is not null && person.BalanceSeconds > 0 && session.ExhaustedAt is not null && session.LogoffSentAt is null)
    {
        // 撤销未执行的 lock/logoff（防止充值后仍被注销），并下发解锁
        CancelPending(db, session.MachineId, "lock", "logoff");
        db.Commands.Add(NewCmd(session.MachineId, "unlock", "{\"reason\":\"balance_granted\"}", 10));
    }

    db.SaveChanges();
    return Results.Json(new { ok = true, balanceSeconds = person.BalanceSeconds });
});

app.MapGet("/api/admin/ledger/{personId:int}", (int personId, HttpContext ctx, AppDbContext db, AdminAuth auth, int limit = 100) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    limit = Math.Clamp(limit, 1, 500);
    var rows = db.TimeLedger
        .Where(l => l.PersonId == personId)
        .OrderByDescending(l => l.CreatedAt)
        .Take(limit)
        .ToList();
    return Results.Json(rows);
});

app.MapGet("/api/admin/requests", (HttpContext ctx, AppDbContext db, AdminAuth auth, string? status, int limit = 50) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    limit = Math.Clamp(limit, 1, 200);
    var query = db.TimeRequests.Include(r => r.Person).AsQueryable();
    if (!string.IsNullOrEmpty(status))
        query = query.Where(r => r.Status == status);
    var rows = query.OrderByDescending(r => r.CreatedAt).Take(limit)
        .Select(r => new
        {
            r.Id, person = r.Person!.DisplayName, personId = r.PersonId, r.MachineId,
            r.Minutes, r.Reason, r.Status, r.CreatedAt, r.DecidedAt
        })
        .ToList();
    return Results.Json(rows);
});

// 家长审批：通过 → 加时长 + 撤销 lock/logoff + 解锁；拒绝 → 给孩子回消息
app.MapPost("/api/admin/requests/{id}/decide", (int id, DecideReq req, HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    var request = db.TimeRequests.Include(r => r.Person).FirstOrDefault(r => r.Id == id);
    if (request is null) return Results.NotFound();
    if (request.Status != RequestStatus.Pending)
        return Results.Json(new { ok = false, reason = "Request already processed" });

    if (req.Approve)
    {
        var delta = request.Minutes * 60L;
        request.Person!.BalanceSeconds += delta;
        db.TimeLedger.Add(new TimeLedger
        {
            PersonId = request.PersonId, DeltaSeconds = delta,
            Kind = LedgerKind.ParentGrant, Note = $"request:{request.Id} approved",
            CreatedAt = DateTime.UtcNow
        });

        var session = db.Sessions.FirstOrDefault(s => s.PersonId == request.PersonId && s.Status == SessionStatus.Active);
        if (session is not null)
        {
            CancelPending(db, session.MachineId, "lock", "logoff");
            if (request.Person.BalanceSeconds > 0 && session.ExhaustedAt is not null)
                db.Commands.Add(NewCmd(session.MachineId, "unlock", "{\"reason\":\"request_approved\"}", 10));
        }
    }
    else
    {
        db.Commands.Add(NewCmd(request.MachineId, "message",
            JsonSerializer.Serialize(new { text = $"Time request declined ({request.Minutes} min)" }), 10));
    }

    request.Status = req.Approve ? RequestStatus.Approved : RequestStatus.Rejected;
    request.DecidedBy = "admin";
    request.DecidedAt = DateTime.UtcNow;
    db.AuditLogs.Add(new AuditLog
    {
        Ts = DateTime.UtcNow, Actor = "admin", Action = "request_decide",
        Detail = $"request:{request.Id} {(req.Approve ? "approved" : "rejected")}"
    });
    db.SaveChanges();
    return Results.Json(new { ok = true });
});

// ================= 策略管理（FR-05）=================

app.MapGet("/api/admin/policies", (HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    var rows = db.Policies.OrderBy(p => p.Id)
        .Select(p => new
        {
            p.Id, p.Name,
            weekdayQuotaSeconds = p.WeekdayQuotaSeconds,
            weekendQuotaSeconds = p.WeekendQuotaSeconds,
            maxSessionSeconds = p.MaxSessionSeconds,
            slidingWindowSeconds = p.SlidingWindowSeconds,
            slidingQuotaSeconds = p.SlidingQuotaSeconds,
            offlineQuotaSeconds = p.OfflineQuotaSeconds,
            p.AllowedWindowsJson, p.Enabled
        })
        .ToList();
    return Results.Json(rows);
});

app.MapPost("/api/admin/policies", (PolicyReq req, HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    var err = ValidatePolicy(req);
    if (err is not null) return Results.Json(new { ok = false, reason = err });

    var p = new Policy
    {
        Name = req.Name.Trim(),
        WeekdayQuotaSeconds = Math.Max(req.WeekdayQuotaMinutes, 0) * 60,
        WeekendQuotaSeconds = Math.Max(req.WeekendQuotaMinutes, 0) * 60,
        MaxSessionSeconds = Math.Max(req.MaxSessionMinutes, 0) * 60,
        SlidingWindowSeconds = Math.Max(req.SlidingWindowMinutes, 0) * 60,
        SlidingQuotaSeconds = Math.Max(req.SlidingQuotaMinutes, 0) * 60,
        OfflineQuotaSeconds = req.OfflineQuotaMinutes is null ? null : Math.Max(req.OfflineQuotaMinutes.Value, 0) * 60,
        AllowedWindowsJson = WindowsJson(req.Windows),
        Enabled = req.Enabled,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    db.Policies.Add(p);
    db.SaveChanges();
    return Results.Json(new { ok = true, id = p.Id });
});

app.MapPut("/api/admin/policies/{id}", (int id, PolicyReq req, HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    var p = db.Policies.Find(id);
    if (p is null) return Results.NotFound();
    var err = ValidatePolicy(req);
    if (err is not null) return Results.Json(new { ok = false, reason = err });

    p.Name = req.Name.Trim();
    p.WeekdayQuotaSeconds = Math.Max(req.WeekdayQuotaMinutes, 0) * 60;
    p.WeekendQuotaSeconds = Math.Max(req.WeekendQuotaMinutes, 0) * 60;
    p.MaxSessionSeconds = Math.Max(req.MaxSessionMinutes, 0) * 60;
    p.SlidingWindowSeconds = Math.Max(req.SlidingWindowMinutes, 0) * 60;
    p.SlidingQuotaSeconds = Math.Max(req.SlidingQuotaMinutes, 0) * 60;
    p.OfflineQuotaSeconds = req.OfflineQuotaMinutes is null ? null : Math.Max(req.OfflineQuotaMinutes.Value, 0) * 60;
    p.AllowedWindowsJson = WindowsJson(req.Windows);
    p.Enabled = req.Enabled;
    p.UpdatedAt = DateTime.UtcNow;
    db.SaveChanges();
    return Results.Json(new { ok = true });
});

static string? ValidatePolicy(PolicyReq req)
{
    if (string.IsNullOrWhiteSpace(req.Name)) return "Name is required";
    if (req.Windows is not null)
        foreach (var w in req.Windows)
            if (!PolicyEngine.TryParseHm(w.Start, out _) || !PolicyEngine.TryParseHm(w.End, out _))
                return "Time windows must use HH:mm format";
    if ((req.SlidingWindowMinutes > 0) != (req.SlidingQuotaMinutes > 0))
        return "Sliding window length and quota must both be set (0 = disabled)";
    if (req.SlidingWindowMinutes > 0 && req.SlidingQuotaMinutes > req.SlidingWindowMinutes)
        return "Window quota cannot exceed window length";
    return null;
}

static string WindowsJson(List<PolicyWindowReq>? windows) =>
    JsonSerializer.Serialize((windows ?? new List<PolicyWindowReq>()).Select(w => new { start = w.Start, end = w.End }));

app.MapGet("/api/admin/machines", (HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    var machines = db.Machines.Select(m => new { m.Id, m.Name, m.Secret, m.LastSeenAt, m.CreatedAt }).ToList();
    return Results.Json(machines);
});

app.MapPost("/api/admin/machines", (CreateMachineReq req, HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.Json(new { ok = false, reason = "Name is required" });
    if (db.Machines.Any(m => m.Name == req.Name))
        return Results.Json(new { ok = false, reason = "Name already exists" });

    var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var machine = new Machine { Name = req.Name, Secret = secret, CreatedAt = DateTime.UtcNow };
    db.Machines.Add(machine);
    db.SaveChanges();
    return Results.Json(new { ok = true, id = machine.Id, secret });
});

// 删除机器：拒绝删除有进行中会话的机器；其指令与历史会话一并清除
app.MapDelete("/api/admin/machines/{id}", (int id, HttpContext ctx, AppDbContext db, AdminAuth auth) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    var machine = db.Machines.Find(id);
    if (machine is null) return Results.NotFound();
    if (db.Sessions.Any(s => s.MachineId == id && s.Status == SessionStatus.Active))
        return Results.Json(new { ok = false, reason = "Machine has an active session, force logoff first" });

    db.Commands.RemoveRange(db.Commands.Where(c => c.MachineId == id));
    db.Machines.Remove(machine);   // 历史会话由 FK 级联删除
    db.SaveChanges();
    return Results.Json(new { ok = true });
});

app.MapPost("/api/admin/machines/{id}/command", (int id, CommandReq req, HttpContext ctx, AppDbContext db, AdminAuth auth) =>{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    var machine = db.Machines.Find(id);
    if (machine is null) return Results.NotFound();

    var action = req.Action.Trim().ToLowerInvariant();
    var valid = new[] { "lock", "unlock", "logoff", "shutdown", "reboot", "message" };
    if (!valid.Contains(action))
        return Results.Json(new { ok = false, reason = "Unknown command" });

    var payload = action == "message"
        ? JsonSerializer.Serialize(new { text = req.Text ?? "" })
        : "{}";
    var ttl = action is "lock" or "unlock" or "message" ? 10 : 5;
    db.Commands.Add(NewCmd(id, action, payload, ttl));
    db.SaveChanges();
    return Results.Json(new { ok = true });
});

app.MapGet("/api/admin/sessions", (HttpContext ctx, AppDbContext db, AdminAuth auth, int limit = 100) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    limit = Math.Clamp(limit, 1, 500);
    var sessions = db.Sessions.Include(s => s.Person).Include(s => s.Machine)
        .OrderByDescending(s => s.StartedAt)
        .Take(limit)
        .Select(s => new
        {
            s.Id, person = s.Person!.DisplayName, machine = s.Machine!.Name,
            s.StartedAt, s.EndedAt, s.ChargedSeconds, s.Status, s.EndReason
        })
        .ToList();
    return Results.Json(sessions);
});

app.MapGet("/api/admin/audit", (HttpContext ctx, AppDbContext db, AdminAuth auth, int limit = 200) =>
{
    if (!auth.Valid(Bearer(ctx))) return Results.Unauthorized();
    limit = Math.Clamp(limit, 1, 1000);
    var logs = db.AuditLogs.OrderByDescending(a => a.Ts).Take(limit).ToList();
    return Results.Json(logs);
});

app.Run();

// ---------- 请求模型 ----------

record LoginReq(string CardNo, string Pin);
record AckReq(int CommandId);
record AdminLoginReq(string Password);
record CreatePersonReq(string CardNo, string DisplayName, string Pin, long InitialSeconds, int? PolicyId = null);
record UpdatePersonReq(string? DisplayName, string? Pin, bool? Enabled, int? PolicyId = null);
record BalanceReq(long DeltaSeconds, string? Note);
record CreateMachineReq(string Name);
record CommandReq(string Action, string? Text);
record RequestTimeReq(int Minutes);
record DecideReq(bool Approve, string? Note);
record SyncOfflineReq(OfflineActiveReq? Active, List<OfflineCompletedReq>? Completed);
record OfflineActiveReq(string RecordId, string CardNo, DateTime StartedAtUtc);
record OfflineCompletedReq(string RecordId, string CardNo, DateTime StartedAtUtc, DateTime EndedAtUtc, long ChargedSeconds, string? EndReason);
record PolicyReq(string Name, long WeekdayQuotaMinutes, long WeekendQuotaMinutes, List<PolicyWindowReq>? Windows, long MaxSessionMinutes, bool Enabled, long SlidingWindowMinutes = 0, long SlidingQuotaMinutes = 0, long? OfflineQuotaMinutes = null);
record PolicyWindowReq(string Start, string End);
record FailState(int Count, DateTime LockedUntil, DateTime LastFailAt);
