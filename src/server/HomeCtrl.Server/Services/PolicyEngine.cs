using System.Text.Json;
using HomeCtrl.Server.Data;
using HomeCtrl.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeCtrl.Server.Services;

/// <summary>
/// 策略引擎（FR-05）：可用时段（含宵禁）+ 每日额度（工作日/周末）+ 单次连续上限。
/// 有效余额 = min(账户余额, 每日剩余额度, 单次剩余)；有效余额 ≤ 0 或在时段外 → 走锁屏/下机流程。
/// 日期与星期按 Policy:TzOffsetHours（默认 +8）换算的本地时间计算。
/// </summary>
public static class PolicyEngine
{
    public static (long EffectiveBalance, bool OutsideWindow, string? LimitReason) Evaluate(
        AppDbContext db, Person person, Session? session, DateTime utcNow, int tzOffsetHours)
    {
        var effective = person.BalanceSeconds;
        string? reason = null;

        var policy = db.Policies.FirstOrDefault(p => p.Id == person.PolicyId && p.Enabled);
        if (policy is null)
            return (effective, false, effective <= 0 ? "exhausted" : null);

        var localNow = utcNow.AddHours(tzOffsetHours);

        // 1) 可用时段（时段外=宵禁）
        var outside = !InWindow(policy.AllowedWindowsJson, localNow);
        if (outside) reason = "curfew";

        // 2) 每日额度：当日（本地日期）所有会话的已计费总和
        var quota = localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? policy.WeekendQuotaSeconds
            : policy.WeekdayQuotaSeconds;
        if (quota > 0)
        {
            var localMidnightUtc = localNow.Date.AddHours(-tzOffsetHours);
            // 取最近 200 条会话在内存中按本地日期过滤：规避 SQLite DateTime SQL 比较的
            // provider 差异，同时 EF 的 identity resolution 会带回 ChangeTracker 里
            // 刚 Settle 但尚未保存的当前会话计费。
            // 200 条足以覆盖任何正常的一天（防"刷短会话挤掉消费记录"绕过每日额度）。
            var recent = db.Sessions
                .Where(s => s.PersonId == person.Id)
                .OrderByDescending(s => s.StartedAt)
                .Take(200)
                .ToList();
            var consumed = recent.Where(s => s.StartedAt >= localMidnightUtc).Sum(s => s.ChargedSeconds);
            var dailyRemaining = quota - consumed;
            if (dailyRemaining <= 0 && reason is null) reason = "daily_quota";
            effective = Math.Min(effective, dailyRemaining);
        }

        // 3) 单次连续上限（仅对进行中的会话）
        if (session is not null && policy.MaxSessionSeconds > 0)
        {
            var elapsed = (long)(utcNow - session.StartedAt).TotalSeconds;
            var sessionRemaining = policy.MaxSessionSeconds - elapsed;
            if (sessionRemaining <= 0 && reason is null) reason = "session_limit";
            effective = Math.Min(effective, sessionRemaining);
        }

        if (effective <= 0 && reason is null) reason = "exhausted";
        return (effective, outside, reason);
    }

    /// <summary>本地时间是否落在任一时段内；空列表=不限；支持跨零点时段（如 20:00-07:00）。</summary>
    public static bool InWindow(string windowsJson, DateTime localNow)
    {
        try
        {
            var windows = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(windowsJson);
            if (windows is null || windows.Count == 0) return true;

            var t = localNow.Hour * 60 + localNow.Minute;
            foreach (var w in windows)
            {
                if (!w.TryGetValue("start", out var s) || !w.TryGetValue("end", out var e)) continue;
                if (!TryParseHm(s, out var st) || !TryParseHm(e, out var et)) continue;
                if (st <= et ? t >= st && t < et : t >= st || t < et)
                    return true;
            }
            return false;
        }
        catch
        {
            return true; // 配置异常时不误伤
        }
    }

    /// <summary>解析 "HH:mm" 为分钟数（"24:00"=1440 表示一天结束）。</summary>
    public static bool TryParseHm(string value, out int minutes)
    {
        minutes = 0;
        var parts = (value ?? "").Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h < 0 || h > 24 || m < 0 || m > 59 || (h == 24 && m != 0)) return false;
        minutes = h * 60 + m;
        return true;
    }
}
