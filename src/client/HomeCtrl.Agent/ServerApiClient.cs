using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HomeCtrl.Agent;

/// <summary>与服务端通信：HMAC 签名（HMACSHA256(secret, "ts:path")，±120s 防重放）。</summary>
public class ServerApiClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // 受控机与服务器是局域网直连：禁用系统代理（Clash 等代理会拖慢故障检测，
    // 离线模式的心跳失败判定依赖"快速失败"）
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false });
    private readonly string _machineId;
    private readonly string _secret;

    public ServerApiClient(IConfiguration cfg)
    {
        _machineId = cfg["Server:MachineId"] ?? "";
        _secret = cfg["Server:Secret"] ?? "";
        _http.BaseAddress = new Uri(cfg["Server:BaseUrl"] ?? "http://localhost:8080");
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task<string> PostAsync(string path, object? body)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(body ?? new object());

        var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var sig = Convert.ToHexString(new HMACSHA256(Encoding.UTF8.GetBytes(_secret))
            .ComputeHash(Encoding.UTF8.GetBytes($"{ts}:{path}")));
        req.Headers.TryAddWithoutValidation("X-Machine-Id", _machineId);
        req.Headers.TryAddWithoutValidation("X-Timestamp", ts.ToString());
        req.Headers.TryAddWithoutValidation("X-Signature", sig);

        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    public async Task<T?> PostAsync<T>(string path, object? body) where T : class
    {
        var json = await PostAsync(path, body);
        return JsonSerializer.Deserialize<T>(json, Json);
    }
}
