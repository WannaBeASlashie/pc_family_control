using HomeCtrl.Agent;

var builder = Host.CreateApplicationBuilder(args);

// 服务名可在安装时通过 sc create 指定（支持伪装），此处仅作 Windows 服务宿主注册
builder.Services.AddWindowsService(o =>
    o.ServiceName = builder.Configuration["Agent:ServiceName"] ?? "HomeCtrlAgent");

builder.Services.AddSingleton<AgentState>();
builder.Services.AddSingleton<AgentGate>();
builder.Services.AddSingleton<ServerApiClient>();
builder.Services.AddHostedService<HeartbeatWorker>();
builder.Services.AddHostedService<PipeServerService>();

builder.Build().Run();
