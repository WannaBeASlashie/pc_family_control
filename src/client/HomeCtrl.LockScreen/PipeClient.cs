using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace HomeCtrl.LockScreen;

/// <summary>连接 Agent 的命名管道客户端：JSON 行协议，请求/响应 + 状态推送。</summary>
public class PipeClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private NamedPipeClientStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly object _writeLock = new();
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private int _nextId = 1;
    private volatile bool _connected;
    private volatile bool _connecting;

    public event Action<StateSnapshot>? OnState;
    public event Action<string>? OnMessage;

    public bool IsConnected => _connected;

    public async Task ConnectLoopAsync()
    {
        if (_connecting) return;
        _connecting = true;
        try
        {
            while (true)
            {
                try
                {
                    var s = new NamedPipeClientStream(".", "homectrl.agent", PipeDirection.InOut);
                    await s.ConnectAsync(3000);
                    _stream = s;
                    _reader = new StreamReader(s, Encoding.UTF8);
                    _writer = new StreamWriter(s, Encoding.UTF8) { AutoFlush = true };
                    _connected = true;
                    _ = ReadLoopAsync();
                    return;
                }
                catch
                {
                    _connected = false;
                    await Task.Delay(1500);
                }
            }
        }
        finally
        {
            _connecting = false;
        }
    }

    public async Task<bool> WaitConnectedAsync(TimeSpan timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        try
        {
            while (!_connected)
                await Task.Delay(200, cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (_reader is not null)
            {
                var line = await _reader.ReadLineAsync();
                if (line is null) break;
                if (line.Length > 0) HandleLine(line);
            }
        }
        catch { }

        _connected = false;
        _ = ConnectLoopAsync(); // 断线重连
    }

    private void HandleLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (root.TryGetProperty("event", out var ev))
            {
                var name = ev.GetString();
                if (name == "state" && root.TryGetProperty("data", out var d))
                {
                    var st = JsonSerializer.Deserialize<StateSnapshot>(d.GetRawText(), Json);
                    if (st is not null) OnState?.Invoke(st);
                }
                else if (name == "message" && root.TryGetProperty("text", out var t))
                {
                    OnMessage?.Invoke(t.GetString() ?? "");
                }
                return;
            }

            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
            {
                var id = idEl.GetInt32();
                lock (_pending)
                {
                    if (_pending.Remove(id, out var tcs))
                        tcs.SetResult(root.Clone()); // Clone：避免 JsonDocument 释放后失效
                }
            }
        }
        catch { }
    }

    public async Task<JsonElement?> CallAsync(string cmd, Dictionary<string, object?>? args = null)
    {
        if (!_connected || _writer is null) return null;

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[id] = tcs;

        var payload = new Dictionary<string, object?> { ["id"] = id, ["cmd"] = cmd };
        if (args is not null)
            foreach (var kv in args)
                payload[kv.Key] = kv.Value;

        try
        {
            var json = JsonSerializer.Serialize(payload);
            lock (_writeLock) _writer.WriteLine(json);
        }
        catch
        {
            lock (_pending) _pending.Remove(id, out _);
            return null;
        }

        var done = await Task.WhenAny(tcs.Task, Task.Delay(8000));
        lock (_pending) _pending.Remove(id, out _);
        return done == tcs.Task ? await tcs.Task : null;
    }

    public async Task<StateSnapshot?> GetStateAsync()
    {
        var r = await CallAsync("getState");
        if (r is not null && r.Value.TryGetProperty("data", out var d))
            return JsonSerializer.Deserialize<StateSnapshot>(d.GetRawText(), Json);
        return null;
    }

    public async Task<(bool Ok, string? Reason)> LoginAsync(string cardNo, string pin)
    {
        var r = await CallAsync("login", new Dictionary<string, object?> { ["cardNo"] = cardNo, ["pin"] = pin });
        if (r is null) return (false, "无法连接本机服务，请稍候再试");

        var root = r.Value;
        var ok = root.TryGetProperty("data", out var d)
                 && d.TryGetProperty("ok", out var okEl)
                 && okEl.ValueKind == JsonValueKind.True;
        string? reason = null;
        if (!ok && d.TryGetProperty("reason", out var rEl))
            reason = rEl.GetString();
        return (ok, reason);
    }

    public async Task LogoutAsync()
    {
        await CallAsync("logout");
    }

    public async Task<(bool Ok, string? Reason)> RequestTimeAsync(int minutes)
    {
        var r = await CallAsync("requestTime", new Dictionary<string, object?> { ["minutes"] = minutes });
        if (r is null) return (false, "无法连接本机服务，请稍候再试");

        var root = r.Value;
        var ok = root.TryGetProperty("data", out var d)
                 && d.TryGetProperty("ok", out var okEl)
                 && okEl.ValueKind == JsonValueKind.True;
        string? reason = null;
        if (!ok && d.TryGetProperty("reason", out var rEl))
            reason = rEl.GetString();
        return (ok, reason);
    }
}
