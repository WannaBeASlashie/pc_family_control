# HomeCtrl 家庭电脑管控

参考万象网管的「锁屏 → 刷卡上机 → 计时 → 下机」模式，面向家庭儿童场景的局域网电脑管控系统。

- 3 台 Windows 10 受控机（共用一个标准 Windows 账户，开机自动登录）
- 10 个孩子账户（卡号 + PIN），时长按账户跨机累计
- 家庭服务器部署中心服务（Docker），家长用浏览器/手机管理

详细文档：[需求说明](docs/requirements.md) ｜ [设计文档](docs/design.md)

## 架构

```
家庭服务器(Docker)                          受控机 ×3 (Win10 Pro)
┌──────────────────────┐   HTTP+HMAC    ┌──────────────────────────┐
│ HomeCtrl.Server       │◄──────────────►│ Agent (SYSTEM 服务)       │
│ API/计费/指令/SQLite   │               │   ▲ 命名管道 IPC          │
└──────────▲───────────┘                │   │                       │
           │ 浏览器                      │ LockScreen UI (WPF 锁屏)  │
       家长 Web/手机                     └──────────────────────────┘
```

## 目录

```
docs/                    需求与设计文档
src/server/HomeCtrl.Server     服务端（ASP.NET Core 8 + EF Core + SQLite）
src/client/HomeCtrl.Agent      客户端服务（Worker Service, SYSTEM）
src/client/HomeCtrl.LockScreen 锁屏 UI（WPF）
scripts/install-client.ps1     客户端一键安装（含服务名伪装）
docker-compose.yml             服务端部署
```

## 快速开始

### 1. 服务端（家庭服务器，需 Docker）

```bash
docker compose up -d --build      # 端口 8080，数据在 ./data/homectrl.db
```

开发模式（本机 .NET 8 SDK）：

```bash
cd src/server/HomeCtrl.Server
dotnet run
```

### 2. 初始化数据

```bash
SERVER=http://<服务器IP>:8080

# 家长登录拿 token
TOKEN=$(curl -s -X POST $SERVER/api/admin/login \
  -H "Content-Type: application/json" \
  -d '{"password":"changeme"}' | sed 's/.*"token":"\([^"]*\)".*/\1/')

# 新建机器（每台受控机一次），记下 id 和 secret
curl -s -X POST $SERVER/api/admin/machines \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"PC-1"}'

# 新建账户（initialSeconds 单位为秒，例：7200 = 2 小时）
curl -s -X POST $SERVER/api/admin/persons \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"cardNo":"001","displayName":"小明","pin":"1234","initialSeconds":7200}'
```

### 3. 受控机安装（每台）

1. 安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（仅安装时需要）
2. 拷贝本仓库到受控机
3. 管理员 PowerShell 运行：

```powershell
.\scripts\install-client.ps1 -ServiceName WksHealthSvc `
  -DisplayName "Workstation Health Service" `
  -BaseUrl http://<服务器IP>:8080 -MachineId <id> -Secret <secret>
```

4. 配置共享 Windows 账户自动登录（`netplwiz`），账户类型设为**标准用户**

### 4. 日常管理

```bash
# 总览：谁在哪台机上、剩余多少
curl -s $SERVER/api/admin/overview -H "Authorization: Bearer $TOKEN"

# 充值 30 分钟（锁屏中的账户会自动解锁恢复）
curl -s -X POST $SERVER/api/admin/persons/1/balance \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"deltaSeconds":1800,"note":"做家务奖励"}'

# 远程指令：lock | unlock | logoff | shutdown | reboot | message
curl -s -X POST $SERVER/api/admin/machines/1/command \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"action":"message","text":"还有 5 分钟"}'
```

## 验证

服务端跑起来后，用冒烟测试脚本验证完整业务链路（登录→计费→耗尽锁定→充值解锁→下机结算）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\smoke-test.ps1 -Server http://127.0.0.1:8080
```

端到端测试会启动**真实的 Agent 二进制**，通过命名管道（与 WPF 锁屏 UI 相同协议）验证心跳、上机、下机全链路：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\e2e-agent-test.ps1 -Server http://127.0.0.1:8080
```

全部 `[PASS]` 即链路正常。

> 测试脚本默认用 `127.0.0.1` 而非 `localhost`：PowerShell 5.1 对 localhost 先试 IPv6 `::1`（约 2 秒延迟），会杀死短超时的请求。

## 核心规则

| 规则 | 说明 |
|---|---|
| 计时 | 服务端权威（心跳 10s），改本机时间无效 |
| 余额耗尽 | 锁屏 → 宽限 5 分钟（可家长加时）→ 强制注销 |
| 单会话 | 一个账户同时只能在一台机上机 |
| 下机 | 点「下机」结束会话并注销（可配置） |
| UI 被关 | 管道断开 60s → 自动结束会话（防杀进程白嫖） |
| 断网 | P0 无法上机（离线额度 P1 实现） |

## 路线图

P0（当前）：上机/计费/锁屏/家长余额控制/远程指令
P1：时段与每日额度策略、到点提醒弹窗、Web 控制台、离线降级、空闲暂停、多屏覆盖
P2：任务、积分、兑换、报表（表结构已预留）
P3：Kiosk/安全桌面加固、进程守卫

## 安全须知

- 首次部署务必修改 `Admin__Password` 环境变量
- 机器 `secret` 等同于该机的钥匙，勿外泄
- 服务端仅限局域网访问，勿映射到公网（P0 为明文 HTTP）
- `data/homectrl.db` 是全部数据，定期备份（直接拷文件）
