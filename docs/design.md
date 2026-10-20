# HomeCtrl 设计文档

> v0.1 ｜ 2026-10-20 ｜ 配套需求见 [requirements.md](requirements.md)

## 1. 决策记录（在需求 D1~D7 基础上新增）

| # | 决策 |
|---|---|
| D8 | 强制层与表现层分离：计费/指令在 SYSTEM 服务，锁屏 UI 可随时替换（二期 Kiosk 的前提） |
| D9 | P0 余额由家长直接控制（充值/扣减秒数）；积分、任务、兑换仅建表预留 |
| D10 | 客户端服务名/显示名/描述为安装参数，可伪装成系统服务样式；代码不含死名字 |
| D11 | 下机/切换默认注销；做成可配置项 |
| D12 | P0 不做进程拉活守卫；UI 与服务断开管道 60 秒未恢复 → 自动结束会话（停表 + 锁机），比重启更强的兜底 |
| D13 | 计费完全在服务端（心跳推算），客户端不参与计时决策，改本机时间无效 |
| D14 | 指令带 TTL（lock/unlock/message 10 分钟，logoff/shutdown/reboot 5 分钟），过期不下发；充值/审批通过时撤销未执行的 lock/logoff |
| D15 | FR-11a 加时申请最小版：锁屏按钮 → 服务端 requests 表 → 家长 API 审批（批准=加时长+撤销lock/logoff+解锁；拒绝=message 反馈）；Web 体验为 FR-11b（P1） |
| D16 | Agent 内以信号量串行化心跳/登录/下机/申请/看门狗的服务端交互，防状态交错覆盖 |
| D17 | 策略引擎（FR-05）：有效余额 = min(账户余额, 每日剩余额度, 单次剩余)；任一 ≤0 或在时段外（宵禁）→ 锁屏+宽限+下机流程；日期/星期按 `Policy:TzOffsetHours`（默认 +8）本地化计算 |
| D18 | 数据库改为 EF Core Migrations（`Database.Migrate()` + 启动种子默认策略），后续改表用 `dotnet ef migrations add` |
| D19 | 滑动窗口限额（FR-05b）：窗口内用量 = 各会话与 [now−N, now] 的墙钟重叠之和（含进行中会话）；到量 → `sliding_quota` 锁定；旧用量随窗口滑出自动释放额度。跨会话累计，刷上下机无法绕过 |

## 2. 组件模型

| 组件 | 技术 | 运行身份 | 职责 |
|---|---|---|---|
| `HomeCtrl.Server` | ASP.NET Core 8 Minimal API + EF Core + SQLite | Docker（家庭服务器） | 唯一权威：账户、余额、会话、计费、指令队列、审计 |
| `HomeCtrl.Agent` | .NET Worker Service | Windows 服务（SYSTEM） | 心跳上报、执行指令（锁定/解锁/注销/关机/消息）、与 UI 的 IPC |
| `HomeCtrl.LockScreen` | WPF | 用户会话（共享账户开机自启） | 全屏锁屏、上机认证、剩余时间组件、下机按钮 |

```
┌─────────────────────────────  家庭服务器（Docker）─────────────────────────────┐
│  HomeCtrl.Server   API + 计费 + 指令队列 + SQLite                            │
└───────▲───────────────────────────────────────────────────────────▲──────────┘
        │ HTTP + HMAC（局域网）                                       │ HTTP（家长浏览器）
┌───────┴──────────┐  ┌──────────────┐      ┌──────────────┐  ┌────┴─────────┐
│ PC-1/2/3         │  │ Agent(SYSTEM)│◄────►│ LockScreen UI│  │ 家长 Web/手机 │
│  Agent ◄─心跳──── │  │  管道 IPC     │ 管道 │  锁屏+小组件  │  │ (P0: API/后续 │
└──────────────────┘  └──────────────┘      └──────────────┘  │  Web 控制台)  │
                                                                └──────────────┘
```

## 3. 核心时序

### 3.1 开机

```
Windows 启动
 ├─ Agent 服务自启（SYSTEM）→ 心跳循环（10s）
 └─ 共享账户自动登录 → LockScreen 自启 → 连接命名管道 → 显示锁屏
```

### 3.2 上机

```
UI ──login(cardNo,pin)──▶ Agent ──POST /api/client/login──▶ Server
                              Server: 校验 卡号/PIN/状态/余额/全局单会话 → 建会话
UI ◀──state(unlocked)─── Agent ◀───────session/balance──────┘
     隐藏锁屏 → 显示剩余时间小组件
```

### 3.3 心跳计费（每 10 秒，服务端权威）

```
Agent ──POST /api/client/heartbeat──▶ Server
        charge = now - startedAt - alreadyCharged   ← 扣余额
        余额 ≤ 0：记 exhaustedAt → 入队 lock 指令（立即）
                  宽限 GraceSeconds(默认300s) 后 → 入队 logoff 指令
Agent ◀── session / balanceSeconds / commands ───────┘
Agent ──POST /api/client/command-ack──▶ Server（执行完逐条确认）
```

### 3.4 下机（D11：默认注销）

```
UI ──logout──▶ Agent ──POST /api/client/logout──▶ Server
                 Server: 结算到此刻 → 结束会话 → 写账本/审计
              UI 回到锁屏
家长/到点强制下机 = logoff 指令：Agent 调 WTSLogoffSession 注销 Windows 会话
                 + 调 logout 结束计费（自动登录会重新回到锁屏）
```

### 3.5 家长加时（锁屏中自动恢复）

```
家长 ──POST /admin/persons/{id}/balance {delta:+1800}──▶ Server
        记账；若该会话因余额耗尽被锁且未注销 → 入队 unlock 指令
Agent ◀── unlock（下一次心跳取到）──▶ 解锁，孩子继续
```

## 4. 计费模型

- 时间源：服务端 UTC（D13）。客户端只显示。
- 扣费粒度：心跳到账即扣（秒级），无需客户端上报用量。
- 单账号全局单活跃会话（跨机器互斥）；单机器同时只允许一个会话。
- 会话结束（logout / exhausted / admin / machine_off）时写一条 `session_charge` 汇总流水。
- 不变量：会话进行中允许「余额 < 流水合计」的暂差（心跳已扣、汇总未写），会话结束即对平。
- 兜底：
  - 机器心跳丢失 > 5 分钟 → reaper 结束会话，计费截止于 `lastSeenAt + 30s`；
  - UI 管道断开 > 60 秒未重连 → Agent 主动调 logout（防杀 UI 白嫖，D12）。

## 5. 数据库 DDL（参考，实际由 EF `EnsureCreated` 生成）

```sql
CREATE TABLE persons (          -- 账户
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  card_no TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  pin_hash TEXT NOT NULL,
  enabled INTEGER NOT NULL DEFAULT 1,
  balance_seconds INTEGER NOT NULL DEFAULT 0,
  points INTEGER NOT NULL DEFAULT 0,          -- 预留：积分
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE machines (         -- 受控机
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL UNIQUE,
  secret TEXT NOT NULL,                       -- HMAC 密钥
  last_seen_at TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE sessions (         -- 上机会话
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  person_id INTEGER NOT NULL,
  machine_id INTEGER NOT NULL,
  started_at TEXT NOT NULL,
  ended_at TEXT,
  charged_seconds INTEGER NOT NULL DEFAULT 0,
  exhausted_at TEXT,                          -- 余额耗尽时刻（宽限起点）
  lock_sent_at TEXT,
  logoff_sent_at TEXT,
  end_reason TEXT,                            -- logout|exhausted|admin|machine_off
  status TEXT NOT NULL DEFAULT 'active'
);

CREATE TABLE commands (         -- 下行指令队列
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  machine_id INTEGER NOT NULL,
  action TEXT NOT NULL,                       -- lock|unlock|logoff|shutdown|reboot|message
  payload TEXT NOT NULL DEFAULT '{}',
  status TEXT NOT NULL DEFAULT 'pending',     -- pending|ack|expired|cancelled
  created_at TEXT NOT NULL,
  expires_at TEXT,                            -- 过期不再下发（D14）
  acked_at TEXT
);

CREATE TABLE policies (        -- 策略（FR-05）
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  weekday_quota_seconds INTEGER NOT NULL DEFAULT 0,   -- 0=不限
  weekend_quota_seconds INTEGER NOT NULL DEFAULT 0,   -- 0=不限
  allowed_windows_json TEXT NOT NULL DEFAULT '[{"start":"00:00","end":"24:00"}]',
  max_session_seconds INTEGER NOT NULL DEFAULT 0,     -- 单次连续上限，0=不限
  sliding_window_seconds INTEGER NOT NULL DEFAULT 0,  -- 滑动窗口长度，0=不启用（FR-05b）
  sliding_quota_seconds INTEGER NOT NULL DEFAULT 0,   -- 窗口内最大累计用时，0=不启用
  enabled INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
-- persons.policy_id 关联（无外键约束，应用层保证，默认 1）

CREATE TABLE time_requests (    -- 加时申请（FR-11a）
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  person_id INTEGER NOT NULL,
  machine_id INTEGER NOT NULL,
  minutes INTEGER NOT NULL,
  reason TEXT,
  status TEXT NOT NULL DEFAULT 'pending',     -- pending|approved|rejected
  created_at TEXT NOT NULL,
  decided_by TEXT,
  decided_at TEXT
);

CREATE TABLE time_ledger (      -- 分钟流水
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  person_id INTEGER NOT NULL,
  delta_seconds INTEGER NOT NULL,
  kind TEXT NOT NULL,           -- initial|parent_grant|parent_deduct|session_charge
  note TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE points_ledger (    -- 预留：积分流水
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  person_id INTEGER NOT NULL,
  delta INTEGER NOT NULL,
  kind TEXT NOT NULL,
  note TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE audit_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts TEXT NOT NULL,
  actor TEXT NOT NULL,          -- machine:1 / server
  action TEXT NOT NULL,
  detail TEXT NOT NULL DEFAULT '{}'
);
```

> 预留：`policies`（时段/每日额度/宵禁，P1）、`tasks`/`requests`（任务与审批，P2）。字段命名沿用本表风格。

## 6. API 契约

### 6.1 机器认证（所有 /api/client/*）

| 头 | 说明 |
|---|---|
| `X-Machine-Id` | 机器注册时的整数 id |
| `X-Timestamp` | Unix 秒，±120s 内有效（防重放） |
| `X-Signature` | `HEX( HMAC-SHA256( secret, "{ts}:{path}" ) )` |

> P0 未对 body 签名（局域网内风险可控），P1 补。

### 6.2 客户端接口

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/client/heartbeat` | 心跳：返回会话/余额/待执行指令 |
| POST | `/api/client/login` | `{cardNo,pin}` 上机 |
| POST | `/api/client/logout` | 结束本机当前会话 |
| POST | `/api/client/request` | `{minutes}` 加时申请（FR-11a） |
| POST | `/api/client/command-ack` | `{commandId}` 指令执行确认 |

心跳响应示例：

```json
{
  "serverTime": "2026-10-20T12:00:00Z",
  "session": { "id": 42, "person": "小明", "startedAt": "2026-10-20T11:30:00Z" },
  "balanceSeconds": 1800,
  "commands": [ { "id": 7, "action": "lock", "payload": "{\"reason\":\"exhausted\"}" } ]
}
```

### 6.3 管理端接口（`Authorization: Bearer <token>`）

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/admin/login` | `{password}` → `{token}`（12h 有效） |
| GET | `/api/admin/overview` | 机器在线状态/活跃会话/所有人余额 |
| GET/POST | `/api/admin/persons` | 账户列表 / 新建 `{cardNo,displayName,pin,initialSeconds}` |
| PUT/DELETE | `/api/admin/persons/{id}` | 改名/改PIN/启停；删除（上机中禁删） |
| POST | `/api/admin/persons/{id}/balance` | `{deltaSeconds,note}` 充值/扣减（锁屏中充值自动撤销 lock/logoff 并解锁） |
| GET | `/api/admin/ledger/{personId}` | 流水 |
| GET | `/api/admin/requests?status=pending` | 加时申请列表 |
| POST | `/api/admin/requests/{id}/decide` | `{approve}` 审批（批准=加时长+解锁；拒绝=message 反馈） |
| GET/POST | `/api/admin/policies` | 策略列表 / 新建（分钟为单位，windows=[{start,end}]） |
| PUT | `/api/admin/policies/{id}` | 修改策略 |
| GET/POST | `/api/admin/machines` | 机器列表 / 新建 → 返回 `secret` |
| POST | `/api/admin/machines/{id}/command` | `{action,text}` 远程指令 |
| GET | `/api/admin/sessions?limit=` | 会话记录 |
| GET | `/api/admin/audit?limit=` | 审计日志 |

## 7. IPC 协议（命名管道 `homectrl.agent`，UTF-8 JSON 行）

请求（UI → Agent）：

```json
{"id":1,"cmd":"getState"}
{"id":2,"cmd":"login","cardNo":"001","pin":"1234"}
{"id":3,"cmd":"logout"}
{"id":4,"cmd":"requestTime","minutes":15}
```

响应（Agent → UI）：`{"id":2,"ok":true,"data":{...}}`

推送（Agent → UI，状态变化时）：

```json
{"event":"state","data":{"locked":false,"sessionId":42,"personName":"小明","balanceSeconds":1800,"serverOnline":true,"message":null}}
{"event":"message","text":"还有 5 分钟"}
```

Agent 内部状态机：`locked = session==null || balance<=0 || adminLock`（adminLock 由 lock/unlock 指令置位）。

## 8. 安全

- PIN：PBKDF2-SHA256 100k 轮 + 随机盐；同卡号连续失败 5 次锁 1 分钟。
- 管理端：密码登录换 token；密码走环境变量（勿提交）。
- 网络：仅局域网；P0 明文 HTTP，P1 可加自签 TLS + 证书固定。
- 数据库：SQLite 落 Docker 卷，定期备份即拷文件。

## 9. 部署

### 服务器

```bash
# 仓库根目录
docker compose up -d --build     # 端口 8080，数据在 ./data/homectrl.db
```

### 客户端（每台受控机）

1. 装 .NET 8 Runtime/SDK，把仓库拉到本机
2. 服务器管理端新建机器，拿到 `id + secret`
3. 管理员 PowerShell 运行：

```powershell
.\scripts\install-client.ps1 -ServiceName WksHealthSvc `
  -DisplayName "Workstation Health Service" `
  -BaseUrl http://<服务器IP>:8080 -MachineId <id> -Secret <secret>
```

脚本动作：发布 Agent/UI → 写配置 → `sc create`（**服务名即在此伪装**）→ 配置崩溃自动重启 → UI 加入当前用户自启。

## 10. P0 范围对照

| FR | 状态 |
|---|---|
| FR-01/02 账户与上机认证 | ✅ |
| FR-03/04 计时与账本 | ✅（服务端心跳计费） |
| FR-06/07 锁屏与到点处置 | ✅（提醒弹窗 P1 细化） |
| FR-08/09/10 下机、远程控制、总览 | ✅（总览为 API，Web 控制台 P1） |
| FR-12/13 任务/积分 | 预留表 |
| FR-05 策略（时段/每日额度） | P1 |
| FR-17 离线降级 | P1（P0 断网=锁屏不可上机） |
| FR-19 空闲暂停 | P1 |
| FR-21 Kiosk | P3 |

## 11. P0 已知限制

1. 锁屏仅覆盖主屏（多屏 P1 用 Win32 遍历虚拟屏幕）
2. 孩子手动注销 Windows：≤60 秒延迟后停表（D12 兜底）
3. 断网时无法上机（P0 简化；FR-17 P1 实现离线额度）
4. `Alt+F4` 关闭锁屏 → 60 秒后会话被终止（可接受；P1 进程守卫）
5. HTTP 明文 + body 未签名（局域网内）
6. **锁屏 UI 被结束后不会自动重启**：会话在 60 秒内被看门狗终止（计费不受影响），但桌面上没有锁屏遮罩，直到下次登录。UI 进程守护（Agent 拉活 UI）为 P1。
7. 测试模式后门：`HOMECTRL_TEST=1` 环境变量启用 ESC 退出，仅供开发调试；标准用户理论上可修改自己的启动快捷方式注入该变量，但其效果与杀进程等价（限制 6 已覆盖），P1 进程守卫上线后此风险一并消除。

## 11a. 策略引擎边界行为（P1，有意为之）

1. **单次上限 vs 加时**：上限按会话起点连续计算。若临近上限时余额也耗尽 → 锁屏 → 家长批准加时 → 解锁后可能立刻因 `session_limit` 再次锁屏。孩子需下机重登（新会话重新计上限）。
2. **锁屏宽限期计入每日额度**：余额/额度耗尽后的 5 分钟宽限内心跳仍持续结算，每日消费会略超额度（上限 = 额度 + 宽限）。
3. **空时段 = 全天允许**（防止家长误配置把孩子完全锁死）；时段配置非法时同样放行（fail-open，见 InWindow 的 catch）。
4. **每日消费按"最近 200 条会话"聚合**：正常使用不可能触界；配合登录的 PIN 门槛，"刷短会话挤掉消费记录"的绕过成本极高。
5. 时区为固定偏移（`Policy:TzOffsetHours`，默认 8），不处理夏令时——中国无需。
