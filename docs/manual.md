# willBeyond TimeBank — Documentation

> 版本：v1.2（P1 完成 + 滑动窗口限额）｜ 更新：2026-10-20
> 本文档是项目的唯一权威参考，整合需求、设计、实现与部署。历史演进细节见 `requirements.md` / `design.md`。

---

## 1. 项目概述

### 1.1 一句话定位

参考网吧"万象网管"的**锁屏 → 刷卡上机 → 计时 → 下机**模式，做成家庭儿童场景的局域网电脑管控系统。

### 1.2 场景与规模

| 项 | 值 |
|---|---|
| 受控机 | 3 台 Windows 10 Pro（共用一个标准账户 + 自动登录） |
| 账户 | 10 个孩子账户（卡号 + PIN），时长按账户跨机累计 |
| 服务器 | 家庭服务器（Docker，固定 IP） |
| 网络 | 纯局域网，无公网依赖 |

### 1.3 当前能力总览（截至 v1.2）

| 能力 | 状态 |
|---|---|
| 万象式锁屏、卡号+PIN 上机、服务端权威计时 | ✅ |
| 到点提醒（10/5/1 分钟）→ 锁屏 → 宽限 → 强制下机 | ✅ |
| 家长 Web 控制台（总览/账户/审批/策略/机器/记录） | ✅ |
| 策略：每日额度（工作日/周末）、可用时段（宵禁）、单次上限 | ✅ |
| 策略：滑动窗口限额（最近 N 分钟最多用 M 分钟） | ✅ |
| 加时申请（锁屏按钮）+ 家长审批 | ✅ |
| 远程指令：锁定/解锁/下机/消息/关机/重启 | ✅ |
| 审计日志、时间流水、数据保留清理 | ✅ |

---

## 2. 需求分析

### 2.1 背景与目标

孩子使用家庭电脑需要"时长可控、时段可控、家长可干预"，且孩子不能轻易绕过。核心目标按优先级：

1. **不依赖孩子自觉**：控制由系统强制执行，改本机时间、杀进程等手段无效或代价极高
2. **正向激励**：做任务/申请可获得时长（P2 扩展积分兑换）
3. **家长省心**：手机浏览器即可查看与操作，无需到每台机器前

### 2.2 角色

| 角色 | 说明 |
|---|---|
| 孩子 | 受控账户，上机需认证，时长/时段受限 |
| 家长 | 管理员，Web 控制台操作 |
| 系统 | 服务端 + 各机 Agent，执行策略 |

### 2.3 范围边界

**做**：时间与权限管控、远程控制、审批流、统计记录。
**不做**（明确排除）：网站/游戏内容过滤、屏幕录像、键盘记录、公网访问、手机原生 App。

### 2.4 功能需求清单（含实现状态）

| 编号 | 需求 | 状态 |
|---|---|---|
| FR-01 | 账户管理（卡号/PIN/启停/策略分配） | ✅ |
| FR-02 | 上机认证（校验状态/余额/时段/额度/单会话） | ✅ |
| FR-03 | 会话计时（服务端权威，锁屏/空闲策略见限制章节） | ✅ |
| FR-04 | 时间账本（发放/充值/扣减/消费全流水） | ✅ |
| FR-05 | 策略：每日额度、可用时段（含跨零点）、宵禁、单次上限 | ✅ |
| FR-05b | 策略：滑动窗口限额（最近 N 分钟最多 M 分钟，跨会话累计） | ✅ v1.2 |
| FR-06 | 全屏锁屏（生产不可关闭，被结束 60s 内停表） | ✅ |
| FR-07 | 到点处置：提醒→锁屏→宽限→强制注销 | ✅ |
| FR-08 | 主动下机（锁屏按钮/小组件按钮） | ✅ |
| FR-09 | 远程控制（锁定/解锁/注销/关机/重启/消息） | ✅ |
| FR-10 | 状态总览（在线/上机/余额） | ✅ |
| FR-11a | 加时申请（锁屏 +15/+30 按钮）+ 家长审批 API | ✅ |
| FR-11b | 加时申请的 Web 端体验 | ✅（并入控制台审批页） |
| FR-12/13 | 任务、积分、兑换 | 预留（表结构 persons.points / points_ledger） |
| FR-14 | 报表（会话记录已具备；图表化 P2） | ◐ |
| FR-15 | 机器管理（注册/密钥/在线状态/远程指令/删除） | ✅ |
| FR-16 | 防绕过（标准用户前提 + SYSTEM 服务 + 指令 TTL + 看门狗） | ✅（进程拉活 P2） |
| FR-17 | 离线降级（断网按缓存额度放行） | 未做 |
| FR-18 | 审计日志（登录/下机/审批/异常 + 保留清理） | ✅ |
| FR-19 | 空闲暂停（N 分钟无输入暂停计费） | 未做 |
| FR-20 | 应急解锁码 | 未做 |
| FR-21 | Kiosk/安全桌面 | P3 |

### 2.5 非功能需求

| 类别 | 要求 |
|---|---|
| 可靠性 | 服务器重启不丢数据；机器断电 5 分钟内会话自动结算 |
| 时间权威 | 全部计时以服务端 UTC 为准；策略日期按时区偏移（默认 +8） |
| 性能 | 心跳 10s；解锁响应 <1s；3 机器 10 账户规模 SQLite 绰绰有余 |
| 安全 | 局域网 + 机器 HMAC 签名 + PIN PBKDF2 + 家长令牌 |
| 可运维 | Docker 一键部署；客户端一键安装脚本；备份=拷贝单文件 |

### 2.6 关键决策记录（浓缩版，完整版见 design.md）

| # | 决策 |
|---|---|
| D1 | 万象式应用级认证：共用 Windows 标准账户，系统层锁屏 |
| D2 | 时长按账户跨机累计；单账户同时仅一个活跃会话 |
| D8 | 强制层（SYSTEM 服务）与表现层（锁屏 UI）分离，为 Kiosk 升级留路 |
| D13 | 计费完全服务端推算，客户端不参与计时 |
| D14 | 指令带 TTL；充值/审批撤销未执行的 lock/logoff |
| D16 | Agent 内信号量串行化所有服务端交互，防状态交错 |
| D17 | 有效余额 = min(账户余额, 每日剩余, 单次剩余, 滑动窗口剩余) |
| D18 | EF Core Migrations + 启动种子默认策略 |
| D19 | 滑动窗口按"会话与窗口的墙钟重叠"计算，旧用量随窗口滑动自动释放 |

---

## 3. 系统设计

### 3.1 总体架构

```
┌─────────────────────────── 家庭服务器（Docker, 固定 IP）───────────────────────┐
│  TimeBank.Server (ASP.NET Core 8)                                             │
│   ├─ Web 控制台（wwwroot/index.html，手机浏览器）                              │
│   ├─ API：客户端接口(HMAC) + 管理接口(Bearer)                                  │
│   ├─ 计费/策略引擎（PolicyEngine）+ 指令队列 + 审批                             │
│   ├─ SessionReaper（僵尸会话回收 + 数据保留清理，60s 周期）                     │
│   └─ EF Core + SQLite（/data/timebank.db，Migrations）                        │
└────────────────────────────────▲──────────────────────────────────────────────┘
                                 │ HTTP + HMAC（心跳 10s）
┌────────────────────────────────┴───────────────┐
│ 受控机 ×3（Win10 Pro）                          │
│  TimeBank.Agent（Windows 服务, SYSTEM）          │
│   ├─ 心跳：上报状态，取会话/有效余额/指令并执行    │
│   ├─ 命名管道 IPC（timebank.agent）              │
│   └─ UI 看门狗（断开 60s → 自动结束会话停表）     │
│  TimeBank.LockScreen（WPF，共享账户自启）         │
│   ├─ 锁屏：登录面板 / 余额耗尽面板（申请加时）     │
│   └─ 小组件：剩余时间走秒 + 下机 + 到点 Toast     │
└─────────────────────────────────────────────────┘
```

### 3.2 核心流程

**上机**：锁屏输入卡号+PIN → Agent（经串行闸）→ 服务端校验（PIN→启停→时段→额度→单会话）→ 建会话 → 解锁。

**计费（每次心跳，10s）**：
```
settle 到当前时刻并落库 → 策略引擎计算有效余额：
  有效余额 = min(账户余额,
                 每日额度 − 当日已消费,
                 单次上限 − 本次已用,
                 滑动限额 − 窗口内用量)
时段外 或 有效余额 ≤ 0 → lock 指令（宽限 300s）→ logoff 指令
```

**下机**：小组件按钮 / 耗尽宽限到期 / 家长指令 / UI 看门狗 → 结算 → 会话 ended → 写消费流水。

**申请加时**：锁屏「+15/+30」→ requests 表 → 控制台审批 →（批准：充值+撤销 lock/logoff+unlock；拒绝：message 反馈）。

### 3.3 数据模型（核心表）

```sql
persons(id, card_no UNIQUE, display_name, pin_hash, enabled,
        balance_seconds, points,            -- points 预留
        policy_id DEFAULT 1, created_at, updated_at)

policies(id, name,
         weekday_quota_seconds,             -- 每日额度（工作日），0=不限
         weekend_quota_seconds,             -- 每日额度（周末），0=不限
         allowed_windows_json,              -- [{"start":"08:00","end":"21:30"}]，空=全天
         max_session_seconds,               -- 单次连续上限，0=不限
         sliding_window_seconds,            -- 滑动窗口长度，0=不启用
         sliding_quota_seconds,             -- 窗口内最大累计用时
         enabled, created_at, updated_at)

machines(id, name UNIQUE, secret, last_seen_at, created_at)

sessions(id, person_id, machine_id, started_at, ended_at,
         charged_seconds, exhausted_at, lock_sent_at, logoff_sent_at,
         end_reason, status)                -- active|ended

commands(id, machine_id, action, payload,   -- lock|unlock|logoff|shutdown|reboot|message
         status, created_at, expires_at, acked_at)   -- pending|ack|expired|cancelled

time_requests(id, person_id, machine_id, minutes, reason,
              status, created_at, decided_by, decided_at)  -- pending|approved|rejected

time_ledger(id, person_id, delta_seconds, kind, note, created_at)
           -- kind: initial|parent_grant|parent_deduct|session_charge
points_ledger(...)                          -- 预留 P2
audit_log(id, ts, actor, action, detail)
```

**保留策略**（reaper 自动清理）：已完成指令 7 天，审计日志与已审批申请 90 天。

### 3.4 API 一览

**机器接口**（`X-Machine-Id` / `X-Timestamp` / `X-Signature=HMAC-SHA256(secret,"ts:path")`，±120s 防重放）：

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/client/heartbeat` | 心跳：返回会话/有效余额/待执行指令 |
| POST | `/api/client/login` | `{cardNo,pin}` 上机 |
| POST | `/api/client/logout` | 结束本机会话 |
| POST | `/api/client/request` | `{minutes}` 加时申请 |
| POST | `/api/client/command-ack` | `{commandId}` 指令确认 |

**管理接口**（`Authorization: Bearer <token>`，POST `/api/admin/login` 换取，12h）：

persons（增删改查 + `{id}/balance` 调整）、policies（增改查，分钟单位）、machines（增删查 + `{id}/command` 指令）、requests（列表 + `{id}/decide` 审批）、overview、sessions、audit、`ledger/{personId}`。

### 3.5 IPC 协议（命名管道 `timebank.agent`，UTF-8 JSON 行）

请求：`{"id":1,"cmd":"getState|login|logout|requestTime"}`；
响应：`{"id":1,"ok":true,"data":{...}}`；
推送：`{"event":"state","data":{locked,sessionId,personName,balanceSeconds,serverOnline,message}}`。

### 3.6 安全设计

- PIN：PBKDF2-SHA256 100k 轮 + 盐；同卡号 5 次失败锁 1 分钟（10 分钟衰减窗口）
- 机器密钥：注册时生成 64 位 hex，仅安装时下发一次；API 全签名
- 家长端：密码换 token（内存，重启失效）；默认密码启动告警
- 指令：TTL 过期作废，防止隔天执行旧指令
- 锁屏：无标题栏+置顶+禁 Alt+F4；单实例互斥

### 3.7 防绕过层次

| 层 | 机制 |
|---|---|
| 账户层 | 共享 Windows 账户必须是标准用户（安装前提） |
| 进程层 | Agent 为 SYSTEM 服务 + 崩溃自动重启；UI 被杀 → 60s 看门狗停表 |
| 时间层 | 计时在服务端，改本机时钟无效 |
| 网络层 | 断网 = 无法上机（离线额度 P2）；HMAC 防伪造请求 |
| 数据层 | 余额/会话全在服务端，本机无持久化可篡改；额度计算按账户全量会话（轻量投影、无截断），刷登录无法把旧用量"挤出"统计 |
| 物理层 | 依赖 BIOS 密码/禁 U 盘启动（文档建议，软件不可覆盖） |

---

## 4. 实现方法

### 4.1 代码结构

```
src/server/TimeBank.Server          服务端（ASP.NET Core Minimal API）
  Program.cs                        全部端点 + HMAC/PIN/指令辅助
  Services/PolicyEngine.cs          策略引擎（有效余额计算）
  Services/Billing.cs               结算（幂等增量扣费）+ 会话结束流水
  Services/SessionReaper.cs         僵尸会话回收 + 保留清理
  Services/AdminAuth.cs             家长令牌
  Migrations/                       EF Core 迁移
  wwwroot/index.html                Web 控制台（单文件 SPA，原生 JS）
src/client/TimeBank.Agent           客户端服务（Worker Service）
  HeartbeatWorker.cs                心跳循环 + 指令执行
  PipeServerService.cs              管道 IPC（接受循环 + 看门狗）
  AgentGate.cs                      信号量：串行化服务端交互
  AgentState.cs                     状态机 locked=无会话|超限|家长锁定
  ServerApiClient.cs                HMAC 签名 HTTP 客户端
  Win32.cs                          WTSGetActiveConsoleSessionId/Logoff
src/client/TimeBank.LockScreen      锁屏 UI（WPF）
  MainWindow                        登录面板 / 耗尽面板（申请加时）
  WidgetWindow                      右上角剩余时间小组件
  ToastWindow                       10/5/1 分钟提醒
  PipeClient.cs                     管道客户端（请求-响应 + 事件分发）
scripts/
  install-client.ps1                客户端一键安装（服务名可伪装）
  smoke-test.ps1                    冒烟测试（32 项，含三种策略路径）
  e2e-agent-test.ps1                真实 Agent 端到端（13 项）
  e2e-agent-commands-test.ps1       指令执行+看门狗（9 项）
  demo-ui.ps1                       本地可视化调试（测试模式 ESC 退出）
```

### 4.2 关键实现要点

**计费**：会话计费按 `now - startedAt - chargedSeconds` 增量推算（幂等），每次心跳先结算**落库**再做策略计算（否则额度读到旧值——曾因此出 bug）。

**策略引擎**：全量加载该账户会话的**轻量投影**（仅开始/结束/计费三字段，单账户终身万条约 1MB），在内存中分别按"本地日期过滤"（每日额度）和"与窗口墙钟重叠"（滑动窗口）计算；按账户跨机器累计，无条数截断（杜绝"刷登录挤掉旧用量"的绕过）。心跳路径先结算落库再计算，读到的计费恒为最新。

**滑动窗口**（v1.2）：`窗口内用量 = Σ overlap(会话, [now-window, now])`，含进行中会话；到量 → `sliding_quota` 锁定；窗口滑过旧用量后额度自动恢复，无需手工重置。跨会话累计，刷"上下机"无法绕过（重叠按墙钟算）。

**Agent 并发**：心跳/登录/下机/申请/看门狗全部经 `AgentGate` 信号量串行，防止心跳的旧会话响应覆盖刚登录的状态。

**指令可靠性**：入库队列 + 心跳拉取 + 执行后 ack；TTL 过期不下发；充值/审批会撤销未执行的 lock/logoff（防止"批了还被注销"）。

**看门狗**：UI 断开管道 60s（可配）且会话活跃 → 自动下机停表。孩子杀 UI 换不来白嫖时间。

**测试模式**：`HOMECTRL_TEST=1` 环境变量启用 ESC 退出锁屏（仅开发机）。

### 4.3 测试体系（54 项自动化）

| 脚本 | 项数 | 覆盖 |
|---|---|---|
| smoke-test.ps1 | 32 | 认证/计费/耗尽锁定/申请批准/拒绝/充值/下机/流水/每日额度/时段/滑动窗口 |
| e2e-agent-test.ps1 | 13 | 真实 Agent：心跳/管道/登录/下机/断线重连 |
| e2e-agent-commands-test.ps1 | 9 | lock/unlock/message 指令执行 + UI 看门狗自动下机 |

> 测试脚本约定：全 ASCII（PS 5.1 读无 BOM 脚本的中文会乱码）；用 `127.0.0.1` 不用 `localhost`（IPv6 回退延迟会杀死短超时）。

### 4.4 已知限制与边界行为

1. 锁屏仅覆盖主屏；Alt+Tab/任务栏仍可操作（应用级遮罩固有弱点，Kiosk P3）
2. UI 被结束后不自动重启（进程拉活 P2）；期间桌面无遮罩但计费已停
3. 断网无法上机（离线额度 P2）
4. 手动注销 Windows ≤60s 延迟停表；Win+L 锁屏期间照常计费（空闲暂停 P2）
5. 单次上限 vs 加时：临近上限时获批加时，解锁后可能立即被 session_limit 再锁（下机重登即重置）
6. 耗尽后的宽限期（默认 5 分钟）计入每日/滑动额度
7. 控制台编辑 00:00-24:00 策略会存成 23:59（HTML time 控件不支持 24:00）
8. 策略时区为固定偏移（默认 +8），不处理夏令时
9. 空/非法时段配置 = 全天允许（fail-open，防误锁）

---

## 5. 部署指引

### 5.0 部署前检查清单

- [ ] 家庭服务器已装 Docker + docker compose；首次构建需联网拉取 node/dotnet 镜像与 npm 包
- [ ] 服务器固定 IP（如 192.168.1.10），8080 端口仅对局域网开放
- [ ] 仓库已到服务器（`git clone` 或打包上传）
- [ ] 已确定家长管理密码（不要用 changeme）
- [ ] 3 台受控机均为 Win10 Pro，且能登录管理员账户
- [ ] 已确定共享 Windows 账户（**标准用户**）与开机自动登录方案

### 5.1 前置条件

| 组件 | 要求 |
|---|---|
| 服务器 | Docker + docker compose，固定 IP；x64/arm64 均可 |
| 受控机 | Windows 10 Pro；安装脚本需 .NET 8 SDK（仅安装时使用） |
| 网络 | 受控机与服务器同网段；8080 仅内网，**不要映射公网** |
| 浏览器 | 现代浏览器（控制台为 Vue SPA） |

> 服务器**不需要 Node**：管理控制台在镜像构建阶段由 node:20 编译并打包进镜像。
> 内网拉取 npm 慢时，可在仓库根 `.env` 写 `NPM_REGISTRY=https://registry.npmmirror.com` 后重新构建。

### 5.2 服务端部署

```bash
git clone <repo> timebank && cd timebank
# 必改：docker-compose.yml -> Admin__Password
docker compose up -d --build        # 首次约 2-5 分钟（含前端构建）
docker compose logs -f timebank     # 应看到 Now listening on http://0.0.0.0:8080
```

首次启动自动：建库（Migrations）+ 种子默认策略（不限额度）。

**验证（部署完成的判定标准）**：

1. 浏览器打开 `http://<服务器IP>:8080` → 出现 TimeBank 登录页
2. 用配置的密码登录 → 总览页正常（机器 0/N、默认策略 1 条）
3. 直接访问 `http://<服务器IP>:8080/policies` 并刷新 → 页面正常（history 路由 fallback）
4. `curl http://<服务器IP>:8080/health` → `TimeBank Server`

### 5.3 受控机安装（每台）

1. 控制台 Machines 页注册机器，**记下 id 和 secret**（只显示一次）
2. 把仓库拷到受控机，管理员 PowerShell：

```powershell
.\scripts\install-client.ps1 -ServiceName WksHealthSvc `
  -DisplayName "Workstation Health Service" `
  -BaseUrl http://<服务器IP>:8080 -MachineId <id> -Secret <secret>
```

脚本自动：发布 Agent/UI → 写配置 → 注册服务（**名称即在此伪装**，含崩溃自动重启）→ UI 加入当前用户自启。

3. 配置共享 Windows 账户**自动登录**（`netplwiz`），账户类型必须是**标准用户**（管理员账户会破坏全部防护）
4. 确认受控机时间与服务器相差 **< 2 分钟**（HMAC 时间窗，Windows 默认自动同步即可）
5. 重启验证：开机自动登录 → 全屏锁屏（TimeBank）→ 测试账户上机

### 5.4 初始化（约 10 分钟）

1. 「策略」：调整默认策略或新建（示例 A 上学日：工作日 30 分钟/天 + 19:00-21:00；示例 B 周末：120 分钟/天 + 滑动窗口 120 分钟内最多 60 分钟）
2. 「账户」：每个孩子建卡号 + PIN + 初始时长 + 策略
3. 「机器」：逐台注册并完成 5.3 安装
4. **端到端试跑**：锁屏上机 → 控制台看到在线/会话/计时 → 锁屏下机 → 记录页出现会话
5. 安全检查：孩子使用标准账户；家长密码已修改；服务器 8080 未映射公网

### 5.5 日常使用（家长）

| 场景 | 操作 |
|---|---|
| 看谁在用/还剩多少 | 总览页（自动刷新） |
| 充值 | 账户页 +15m/+30m/+1h 或编辑里自定义（锁屏中充值自动解锁） |
| 孩子申请加时 | 总览徽标提醒 → 审批页批准/拒绝 |
| 临时锁住某台机 | 机器页「锁定」 |
| 提醒吃饭 | 机器页「消息」 |
| 下班关机 | 机器页「关机」（有确认） |
| 查历史 | 记录页（最近 50 条会话） |

### 5.6 升级与回滚

```bash
# 1. 先备份（见 5.7）
# 2. 拉新代码并重建
git pull
docker compose up -d --build
```

- 数据库迁移随容器启动自动应用（**仅向前**）
- 受控机升级：重跑 `install-client.ps1`（先停服务再发布，幂等）
- 回滚：`git checkout <上一个提交>` → `docker compose up -d --build`，并用升级前备份恢复 `data/`（迁移不会自动回退）

### 5.7 备份与恢复

- 全部数据在 `./data`（`timebank.db` + `-wal`/`-shm`）
- 一致性备份（推荐）：

```bash
docker compose stop timebank
cp -r data "backup-$(date +%F)"
docker compose start timebank
```

- 恢复：停容器 → 用备份覆盖 `data/` → 起容器
- 建议：每周一次 + **每次升级前必做**

### 5.8 故障排查

| 现象 | 原因 | 处理 |
|---|---|---|
| 控制台 404 / 白屏 | 镜像里没有前端构建产物（旧镜像） | `docker compose build --no-cache && docker compose up -d` |
| Docker 构建卡在 npm install | 网络 | 设 `NPM_REGISTRY` 镜像源（见 5.1）后重建 |
| 锁屏显示"无法连接服务器" | 服务端/网络问题 | `docker ps`、ping 服务器、看容器日志 |
| 锁屏显示"本机管控服务未运行" | Agent 服务没起来 | `sc query <服务名>`；Windows 事件日志看 TimeBank.Agent 来源 |
| 机器一直离线 | Agent 心跳失败 / 时差 >2 分钟 / secret 错 | 校时；核对 `appsettings.json`；`curl .../health` |
| "在线 0/N 但上机 1"（持续） | 僵尸会话 | 已在 v1.2 修复自动回收；仍异常看容器日志 reaper |
| 充值了没解锁 | unlock 指令未达 | 机器在线则等 10s；离线则指令过期，孩子重登即可 |
| 登录一直密码错误 | 改了 compose 但没重建容器 | 改 env 后 `docker compose up -d` |
| 忘记孩子 PIN | — | 账户页编辑里重设 PIN |
| 忘记家长密码 | — | 改 compose 的 `Admin__Password` 后 `docker compose up -d` |

### 5.9 开发者指南

```bash
# 构建 & 全量测试
& "C:\Program Files\dotnet\dotnet.exe" build willBeyond-TimeBank.sln

# Web 控制台（Vue 3 + TS + Vite）
cd web
npm.cmd run dev          # 热更新开发（代理 /api → 8080）
npm.cmd run typecheck    # vue-tsc 类型检查
cd ..
powershell -File scripts\build-web.ps1   # 生产构建 → 服务端 wwwroot

# 起 dev 服务端（自动迁移）
& "C:\Program Files\dotnet\dotnet.exe" run --project src/server/TimeBank.Server
powershell -File scripts/smoke-test.ps1          # 35 项
powershell -File scripts/e2e-agent-test.ps1      # 13 项（真实 Agent）
powershell -File scripts/e2e-agent-commands-test.ps1   # 9 项
powershell -File scripts/e2e-offline-test.ps1    # 15 项（离线/补账）
powershell -File scripts/demo-ui.ps1             # 可视化调试（ESC 退出）

# 改表结构
$env:Path = "C:\Program Files\dotnet;$env:Path"
dotnet ef migrations add <名称> --project src/server/TimeBank.Server
```

管理控制台（`web/`）：Vue 3 + TypeScript + Vite + vue-router（history 模式，服务端 `MapFallback` 回 index.html）。
**UI 库与 Everything 完全同栈**：Element Plus 2.9 + 共享设计系统 `web/src/styles/brand.css`（天空蓝 #0ea5e9、18px 圆角、el-* 主题覆盖）+ `WbLogo` 品牌组件；应用层补充样式在 `app.css`。
**登录页复用生态标准外壳**：`WbAuthShell`（左品牌区 + 生态产品列表 + 右表单插槽 + 语言切换）连同 `brand.config.ts`、`components/index.ts` 按 `willBeyondEverything/docs/06-生态组件复用指引.md` 的**方式 A（源文件复制）**落地；外壳不含认证逻辑，登录表单通过 `#form` 插槽注入。
**国际化**：控制台支持中文 / English 切换（右上角，`localStorage` 持久化，默认跟随浏览器语言；Element Plus 组件语言同步切换）。语言包在 `web/src/i18n/en.ts`、`zh.ts`。
Docker 构建为多阶段（node 构建前端 → dotnet 发布 → 运行时镜像）；本地开发用 Vite 代理避免 CORS。
Windows PowerShell 下 npm 用 `npm.cmd`（`npm.ps1` 受执行策略限制）。

提交规范：每个功能/修复一个 commit，说明"做了什么+为什么"；破坏性决策记录进 design.md 决策表。

---

## 附：版本历史

| 版本 | 内容 |
|---|---|
| P0 (b160dc4) | 首版：锁屏/计时/指令/审批基础 |
| P0.1 (c3379c3) | 复盘修复 10 项 + 补齐提醒/申请加时 |
| P1 (1dd8bc6) | Migrations + 策略引擎 + Web 控制台 |
| P1.1 (07feed4) | 僵尸会话回收修复 + 机器删除 |
| v1.2 (本次) | 滑动窗口限额 + 本文档 |
