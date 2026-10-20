using System.Collections.Concurrent;

namespace WillBeyond.TimeBank.Server.Services;

/// <summary>管理端令牌（内存版，12 小时有效）。</summary>
public class AdminAuth
{
    private readonly ConcurrentDictionary<string, DateTime> _tokens = new();

    public string Issue()
    {
        var token = Guid.NewGuid().ToString("N");
        _tokens[token] = DateTime.UtcNow.AddHours(12);
        foreach (var kv in _tokens)
            if (kv.Value < DateTime.UtcNow)
                _tokens.TryRemove(kv.Key, out _);
        return token;
    }

    public bool Valid(string? token) =>
        token is not null && _tokens.TryGetValue(token, out var exp) && exp > DateTime.UtcNow;
}
