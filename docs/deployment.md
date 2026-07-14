# GitCandy 部署与运维指南

本指南对应 ROADMAP Milestone 8。当前正式支持 Docker Compose、Linux systemd 服务和 Windows Service 三种部署方式，不支持 IIS in-process、IIS out-of-process 或旧 ASP.NET MVC5 的 IIS 发布流程。

## 发布产物和镜像

推送 `v*` tag 后，GitHub Actions 创建 Release 并发布：

| 产物 | 用途 |
| --- | --- |
| `gitcandy-{version}-linux-x64.tar.gz` | Linux x64 自包含应用、systemd unit 和安装脚本 |
| `gitcandy-{version}-win-x64.zip` | Windows x64 自包含应用和 Windows Service 脚本 |
| `gitcandy-{version}-migration-sql.zip` | SQLite 建库 SQL 和 SQL Server idempotent migration SQL |
| `gitcandy-{version}-compose.zip` | `docker-compose.yml` 与镜像、宿主机端口变量样例 |
| `gitcandy-{version}-linux-amd64-image.tar.gz` | 可离线下载并通过 `docker load` 导入的镜像 |

在线镜像使用相同版本 tag：

```bash
docker pull ghcr.io/iotsharp/gitcandy:latest
docker pull iotsharp/gitcandy:latest
```

离线镜像导入：

```bash
gzip -dc gitcandy-1.0.0-linux-amd64-image.tar.gz | docker load
```

Docker Hub 发布需要仓库 secrets `DOCKERHUB_USERNAME` 和 `DOCKERHUB_TOKEN`，该账户必须有 `iotsharp/gitcandy` push 权限。GHCR 使用 GitHub Actions 的 `GITHUB_TOKEN` 发布到 `ghcr.io/iotsharp/gitcandy`。首次正式发布前必须把两个 container repository 都设为 public；workflow 在 push 后注销凭据并实际执行两个匿名 pull，私有或不可拉取会让发布失败。

## Docker Compose

1. 解压 Release 中的 Compose 包，或使用仓库根目录的文件。
2. 将 `.env.example` 复制为 `.env`，把 `GITCANDY_IMAGE` 固定到明确版本，生产环境不要长期使用 `latest`。
3. 启动服务并检查 readiness。

```bash
docker compose pull
docker compose up -d
docker compose ps
curl --fail http://127.0.0.1:8080/health/ready
```

GitCandy 主程序会在 Web、SSH、Quartz 和其他 hosted service 启动前检查 EF Core pending migrations；有待应用版本时自动迁移，没有时直接继续。迁移失败会让进程以失败状态退出，不会在旧 schema 上开始监听。`GitCandy --migrate` 仍可用于只执行迁移后退出，但 Compose 不再需要单独的迁移服务。

源码仓库使用标准 `docker-compose.override.yml` 保存 `build.context` 和 `build.dockerfile`，执行 `docker compose up --build -d` 时会自动加载。Release Compose 包不包含该重载文件；Release 部署先执行 `docker compose pull`，随后使用基础 `docker-compose.yml` 启动预构建镜像。

默认端口为 HTTP `8080`、SSH `2222`，持久状态位于 `gitcandy-data` volume。可在 `.env` 修改宿主机映射端口，不要修改容器内 SSH 端口而不同步 `GitCandy__Application__SshPort`。

容器内端口、SQLite、repository、cache、SSH host key 和 Data Protection key ring 的生产默认值统一定义在主程序的 `appsettings.json`，`docker-compose.yml` 不再重复列出这些应用配置。需要修改时可通过 Compose override 文件把自定义 `appsettings.Production.json` 只读挂载到 `/app/appsettings.Production.json`，也可继续使用 ASP.NET Core 环境变量；环境变量把 `:` 替换为 `__`，并覆盖 JSON 配置。不要把密码、token 或私钥写入 Compose 文件或提交到仓库。

Web 登录 cookie 始终带 `Secure`。公网部署必须在 `8080` 前放置支持长请求和流式响应的 TLS reverse proxy，并正确转发 scheme、host 和客户端地址。Git Smart HTTP 的 request body 上限和 timeout 也要在代理层设置为不低于 GitCandy 的 `GitCandy:GitHttp` 配置。不要对 pack 响应启用代理缓冲。

仓库提供 Caddy 快速 TLS overlay。域名必须已解析到主机，80/443 必须可达：

```bash
export GITCANDY_DOMAIN=git.example.com
docker compose -f docker-compose.yml -f docker-compose.tls.yml config --quiet
docker compose -f docker-compose.yml -f docker-compose.tls.yml up -d
curl --fail https://git.example.com/health/ready
```

`GitCandy:Proxy` 默认关闭。overlay 只信任固定容器地址 `172.16.0.2`，`ForwardLimit=1`；自行更换网络时必须同步修改 `KnownProxies`，禁止清空 known proxies/networks 后信任任意来源。Forwarded Headers 在 HTTPS redirect、认证和链接生成之前执行，因此 clone URL 与 OIDC callback 使用外部 `https` scheme/host。

`deploy/sonnet-vip` 提供 `gitcandy.com` 的服务器专用 profile：复用该主机已有 Caddy 和内部 SonnetDB，不启动第二个 Caddy，也不公开数据库端口。该 profile、DNS、固定代理地址、SSH `2222`、secret 注入和验证步骤见 [sonnet.vip 部署说明](../deploy/sonnet-vip/README.md)。

密码恢复使用 `GitCandy:Identity:AccountRecovery`。启用时配置 SMTP `Host`、`Port`、`EnableSsl`、`UserName`、`Password` 和 `FromAddress`；密码应通过 secret store 或未提交的环境配置提供。默认 token 有效一小时，同一 IP/邮箱分区 15 分钟最多请求五次。未配置或投递失败不会在响应中泄漏账号是否存在，也不会记录 token 或邮箱。

## Linux systemd 服务

目标主机需要 Linux x64、systemd 和 Git CLI；自包含包不要求预装 .NET Runtime。解压后执行：

```bash
sudo ./install.sh
curl --fail http://127.0.0.1:8080/health/ready
sudo systemctl status gitcandy
sudo journalctl -u gitcandy -n 200 --no-pager
```

安装脚本执行以下受控操作：

- 创建无登录 shell 的 `gitcandy` 系统账户。
- 安装程序到 `/opt/gitcandy`，数据放到 `/var/lib/gitcandy`。
- 仅在配置文件不存在时安装 `appsettings.Production.json`，升级不会覆盖现有配置。
- 安装并启动 `gitcandy.service`；服务进程在开始监听前自动应用 pending migrations。

systemd unit 使用只读系统目录、独立临时目录和 `NoNewPrivileges`，仅允许写 `/var/lib/gitcandy`。默认 SSH 端口 `2222` 不需要授予低端口 capability；若对外使用 22，优先通过防火墙/NAT 转发 22 到 2222。

## Windows Service

目标主机需要 Windows x64 和 Git for Windows；自包含包不要求预装 .NET Runtime。以管理员 PowerShell 解压并执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Install-GitCandyService.ps1
Get-Service GitCandy
Invoke-WebRequest http://127.0.0.1:8080/health/ready
```

默认程序目录为 `%ProgramFiles%\GitCandy`，数据目录为 `%ProgramData%\GitCandy`。安装脚本先停止旧服务、保留现有配置和数据、复制新程序，然后以 `NT SERVICE\GitCandy` 虚拟账户启动服务并授予数据目录修改权限；服务进程在开始监听前自动应用 pending migrations。

移除服务但保留程序和数据：

```powershell
.\Uninstall-GitCandyService.ps1
```

## 配置迁移

主程序 `appsettings.json` 提供 Linux/容器生产默认值；本地 `Development` 环境由 `appsettings.Development.json` 覆盖为 content root 下的 `App_Data`。生产修改优先使用 `appsettings.Production.json` 或环境变量。环境变量把 `:` 替换为 `__`，例如 `GitCandy:Application:RepositoryPath` 对应 `GitCandy__Application__RepositoryPath`；`ASPNETCORE_HTTP_PORTS` 可覆盖默认 HTTP `8080` 监听端口。

| 旧 MVC5 配置 | ASP.NET Core 配置 | 说明 |
| --- | --- | --- |
| `connectionStrings/GitCandyContext` | `ConnectionStrings:GitCandy` | SQLite 不再使用 `BinaryGUID=True`；Identity 使用新 schema |
| provider name / 旧 EF6 provider | `GitCandy:Database:Provider` | 当前主 host 发布路径为 SQLite-first |
| `appSettings/LogPathFormat` | 不迁移 | 日志输出由 ASP.NET Core logging provider 和部署宿主管理 |
| `appSettings/UserConfiguration` | `GitCandy:Application:UserConfigurationPath` | 只用于保留配置和 RSA host key 导入 |
| `config.xml/RepositoryPath` | `GitCandy:Application:RepositoryPath` | 相对路径基于 content root；外部目录使用绝对路径 |
| `config.xml/CachePath` | `GitCandy:Application:CachePath` | cache 可重建，不应与 repository 混用 |
| `config.xml/GitCorePath` | `GitCandy:Application:GitCorePath` | 留空时从 `PATH` 查找 `git`；配置时必须指向包含 Git executable 的目录 |
| `config.xml/EnableSsh` | `GitCandy:Application:EnableSsh` | 内置 SSH 随主进程启停 |
| `config.xml/SshPort` | `GitCandy:Application:SshPort` | 容器和服务模板默认 2222 |
| `config.xml/HostKeys` | `GitCandy:Application:SshHostKeyPath` | 首次可导入旧 RSA key；新文件包含私钥 |
| `config.xml/IsPublicServer` 等页面设置 | `GitCandy:Application:*` 同名属性 | 设置页当前只读，生产配置由文件/环境变量管理 |
| IIS `requestLimits`/timeout | `GitCandy:GitHttp:MaxRequestBodySize`、`RequestTimeout` | reverse proxy 必须配置匹配的上限和流式行为 |

代码浏览的显示与计算边界位于 `GitCandy:RepositoryBrowser`：`MaxDisplayedBlobBytes`、`MaxDiffCharacters`、`MaxDiffFiles`、`MaxArchiveBytes`、`MaxArchiveEntries` 和 `OperationTimeout`。这些限制只影响 Web 展示/归档，不改变 Git Smart HTTP/SSH pack 传输。

Git LFS v2 由 `GitCandy:Lfs` 配置：`Enabled`、`MaxObjectBytes`、`RepositoryQuotaBytes`、`StreamBufferSize` 和 `OperationTimeout`。`RepositoryQuotaBytes=0` 表示不设置额外的仓库总量上限。reverse proxy 需要允许 `/{namespace}/{repository}.git/info/lfs/*` 的流式 PUT/GET 和 range response。

新增的 `GitCandy:Application:DataProtectionKeysPath` 保存 Identity cookie 加密 key ring。它必须位于持久化、仅应用账户可写的目录；丢失后已有 cookie 全部失效。

Pull Request review 策略也位于 `GitCandy:Application`：

| 配置键 | 默认值 | 行为 |
| --- | --- | --- |
| `AllowAuthorApproval` | `false` | 是否允许 PR 作者提交对本人 PR 的 approve；comment 和 request changes 不受此项影响 |
| `DismissStalePullRequestApprovals` | `true` | source head 更新后，旧 head 上的 approve 是否不再作为有效批准；历史 review 始终保留 |
| `RequiredPullRequestApprovals` | `1` | merge/squash 前所需的当前有效 approve 数；设为 `0` 可关闭该基础门禁，M13 branch policy 会在此基础上扩展 |

Webhook delivery 由 `GitCandy:Webhooks` 配置。生产默认只允许公网 HTTPS target，`AllowHttpTargets=false`、`AllowPrivateNetworkTargets=false`；DNS/IP 策略在 subscription 保存和实际 socket 连接时都会执行，且不会跟随 redirect。`RequestTimeout`、`ConnectTimeout`、`MaxAttempts`、`DeliveryBatchSize`、`MaxResponseBytes` 和 `MaxSubscriptionsPerRepository` 控制有界资源使用。`AllowHttpTargets` 与 `AllowPrivateNetworkTargets` 只应用于受控本地 fixture；生产启用会扩大 SSRF 和内网访问面，必须另行评审并限制应用出站网络。

Webhook signing secret 由 Data Protection key ring 加密保存且只在创建页显示一次，因此 `DataProtectionKeysPath` 与数据库必须一致备份/恢复。丢失 key ring 后旧 subscription 无法继续签名，应暂停并重新创建，而不是在日志或配置中回显 secret。

用户通知的 webhook target 复用相同 SSRF、连接和签名边界；通知邮件复用 `GitCandy:Identity:AccountRecovery:Smtp`。通知偏好不会关闭站内 inbox，只决定额外邮件/webhook 投递。后台投递前会重新检查 repository/team 权限，撤权后的 pending delivery 以 `permission_revoked` 失败结束，不再发送资源标题或 URL。

Release 附件由 `GitCandy:Releases` 控制：`MaxAssetBytes` 默认 100 MiB，`MaxTotalAssetBytes` 默认每个 Release 1 GiB，`MaxAssetsPerRelease` 默认 20，`OrphanRetention` 默认 24 小时。附件流式写入临时文件、计算 SHA-256 后原子提交，物理键只使用 repository/release/asset ID，不使用上传文件名。`CachePath/release-assets` 与 `CachePath/lfs` 一样是不可重建的持久内容，必须与数据库、repository、LFS 和 Data Protection keys 一致备份/恢复；普通 cache 仍可重建。

`CachePath/lfs` 与 `CachePath/release-assets` 虽位于 cache 根，但保存不可重建内容，必须纳入持久卷、备份/恢复和容量告警；其他普通 cache 仍可重建。

### Identity 和 OpenID Connect

新密码默认至少 12 位，并要求至少 4 个不同字符以及大写字母、小写字母、数字和非字母数字字符。可通过 `GitCandy:Identity:Password:RequiredLength`、`RequiredUniqueChars`、`RequireDigit`、`RequireLowercase`、`RequireUppercase`、`RequireNonAlphanumeric` 调整；降低策略前应完成安全评审。策略只影响新密码和密码变更，不会使已有密码立即失效。

通用 OpenID Connect 登录默认关闭。启用时在未提交到仓库的生产配置中设置：

```json
{
  "GitCandy": {
    "Identity": {
      "OpenIdConnect": {
        "Enabled": true,
        "DisplayName": "Company ID",
        "Authority": "https://identity.example.com",
        "ClientId": "gitcandy",
        "ClientSecret": "从生产 secret store 注入",
        "CallbackPath": "/signin-oidc",
        "RequireHttpsMetadata": true
      }
    }
  }
}
```

优先通过 secret store 或进程凭据提供 `GitCandy__Identity__OpenIdConnect__ClientSecret`，不要把真实 secret 写入镜像、Compose、systemd unit 或仓库。Identity provider 中登记的 redirect URI 是 GitCandy 公网 HTTPS origin 加 `CallbackPath`。GitCandy 不持久化上游 access/refresh token；本地退出不会结束 provider 的单点登录会话。关闭 `Enabled` 即可回滚外部登录入口，已绑定记录保留在标准 `AspNetUserLogins` 表中，不需要 schema 回滚。

TOTP 和恢复码只用于交互式 Web/外部登录的第二阶段认证。Git Smart HTTP Basic Auth 与 SSH public key 仍是独立认证方案，不能弹出 MFA challenge，本项不改变其现有协议行为或凭据要求。

不迁移旧 `_gc_auth` cookie、密码 hash、`Users`、`AuthorizationLog` 或 `PasswordVersion`。用户必须在新 Identity schema 中重新创建。旧 repository/team/role 元数据只能由后续独立导入工具导入；当前发布不包含自动导入器，也不会在启动时读取并改写旧数据库。物理 bare repositories 可备份后复制到新 repository 根目录，再在新系统重建对应 metadata 和权限。

### 团队企业身份连接

TeamOwner 或系统管理员可从团队详情进入 Enterprise connections。连接只持久化公开配置和 secret reference；支持以下两种运行时引用：

```text
env:GITCANDY_ENTRA_CLIENT_SECRET
config:GitCandy:EnterpriseSecrets:ContosoEntra
```

`env:` 后只允许 ASCII 字母、数字和下划线；`config:` 后是 ASP.NET Core configuration key。真实值应由进程环境、容器 secret、systemd credential 或未提交的生产配置注入。管理 UI 和诊断只显示引用及脱敏状态，不返回 secret。provider webhook 使用独立的 `Webhook secret reference`，不要与 OAuth client secret 共用。

| Provider | 公开连接字段 | secret 内容 | 说明 |
| --- | --- | --- | --- |
| Microsoft Entra ID | tenant ID、HTTPS authority、client ID | client secret | authority 通常为 tenant 专用 v2.0 authority；redirect URI 是公网 HTTPS origin 加 `/EnterpriseLogin/Callback`。JIT 还要求应用允许注册且非敏感 JSON 为 `{"allowJit":true}` |
| SCIM | 稳定组织 ID | secret reference 仍为必填占位引用 | 从管理页轮换独立 SCIM bearer；base URL 为公网 HTTPS origin 加 `/scim/v2/{connectionId}`，明文 bearer 只显示一次 |
| 企业微信 | CorpId、可选 API base URL | CorpSecret | 登录还需非敏感 JSON `{"agentId":"100001"}`；callback 同为 `/EnterpriseLogin/Callback` |
| 飞书 | tenant/组织稳定 ID、App ID、可选 API base URL | App Secret | event URL 为 `/enterprise-events/{connectionId}/Feishu`，签名 secret 单独配置 |
| 钉钉 | CorpId、AppKey、可选 API base URL | App Secret | event URL 为 `/enterprise-events/{connectionId}/DingTalk`，签名 secret 单独配置 |

先创建但保持 connection、login 和 provisioning disabled，注入 secret 后执行 Test connection，再在上游登记 callback、SCIM 或 event URL。上游 scope 只授予实际启用的登录、用户和部门读取能力。Microsoft Entra ID 连接测试使用 client credential 请求 Graph `.default` scope；生产 consent 必须与此一致。

SCIM 使用独立 Bearer authentication scheme，不接受浏览器 cookie、PAT 或 Git Basic credential。轮换 bearer 会立即使旧值失效；通过 `Authorization: Bearer <token>` 调用 Users/Groups create、query、PATCH 和分页接口。`active=false` 或完整目录对账发现用户缺失时，GitCandy 会锁定 Identity、刷新 security stamp、移除团队成员关系、撤销 PAT 并删除 SSH key。系统始终保护最后 TeamOwner 和至少一位本地、非企业托管的 break-glass TeamOwner。

企业微信、飞书和钉钉的主动目录同步由同进程 Quartz job 每 15 分钟执行。单个连接的失败不会阻止其他连接；游标会持久化以便重启恢复。只有完整 fresh scan 才停用缺失用户。飞书和钉钉 event endpoint 只在 1 MiB 限制、写入 rate limit、签名和 5 分钟时间窗通过后记录去重收据，不在 HTTP 请求内执行目录同步。

运行验收至少包括：连接测试、企业登录、同一 stable external ID 重复登录、SCIM create/query/PATCH、目录分页恢复、停用后的 Web/PAT/SSH 拒绝，以及 break-glass owner 仍可登录和修复配置。真实 Entra、企业微信、飞书和钉钉 tenant 的 consent、scope、回调域名、事件订阅和网络策略必须在部署环境验证；仓库测试 fixture 不包含生产 credential。

紧急功能回滚时先禁用对应 connection 或分别关闭 login/provisioning，并在 provider 侧撤销 client secret、SCIM bearer 或 event subscription。该方式保留 external identity mapping、游标和审计，便于修复后恢复。二进制回滚若跨越 M14 migrations，必须停止服务并恢复升级前数据库快照；不要让旧二进制连接含 M14 schema 的数据库。Data Protection keys 必须与数据库保持同一备份时间点，否则未完成的 enterprise login state 和现有 Identity cookie 会失效。

## 文件系统路径

| 数据 | Compose | Linux | Windows | 备份要求 |
| --- | --- | --- | --- | --- |
| SQLite database | `/var/lib/gitcandy/GitCandy.db` | `/var/lib/gitcandy/GitCandy.db` | `%ProgramData%\GitCandy\GitCandy.db` | 必须 |
| repositories | `/var/lib/gitcandy/repositories` | `/var/lib/gitcandy/repositories` | `%ProgramData%\GitCandy\repositories` | 必须 |
| cache | `/var/lib/gitcandy/cache` | `/var/lib/gitcandy/cache` | `%ProgramData%\GitCandy\cache` | 可选，可重建 |
| SSH host key | `/var/lib/gitcandy/ssh-host-key.xml` | 同左 | `%ProgramData%\GitCandy\ssh-host-key.xml` | 必须且按私钥保护 |
| Data Protection keys | `/var/lib/gitcandy/data-protection-keys` | 同左 | `%ProgramData%\GitCandy\data-protection-keys` | 必须 |

repository、cache、archive 和 delete 的子路径必须继续经过根目录边界检查。不要把 repository、cache、host key 或 key ring 放进程序安装目录；升级程序时只替换二进制。

GitCandy 不创建或轮转独立日志文件。运行时代码统一使用 `ILogger<T>`：容器从标准输出收集日志，systemd 使用 journald，Windows Service 使用已配置的 ASP.NET Core logging provider。日志级别由 `Logging` 配置节控制，保留与轮转策略由部署环境负责。

## OpenTelemetry

`GitCandy:Observability:Enabled` 默认开启 OpenTelemetry provider，但 OTLP 和诊断 Console exporter 默认关闭，因此未配置 collector 时不会发起 telemetry 网络连接，也不会重复打印 Console 日志。当前信号包括：

- tracing：ASP.NET Core 请求（排除高频 `/health/live`）、Git transport 和 Quartz job；
- metrics：ASP.NET Core、.NET runtime、Git transport 操作数/活跃数/耗时，以及 Quartz 执行数/活跃数/耗时；
- logging：现有结构化 `ILogger<T>` 日志，并携带可用的 trace/span correlation。

Git 和 scheduler 自定义标签只使用 service、result 等低基数字段，不加入 repository 名称、actor、物理路径、authorization header、token 或 key。ASP.NET Core 和现有应用日志仍可能包含普通请求路由或业务标识；collector 的访问控制、保留期和二次脱敏属于部署者责任，禁止把 OTLP headers 或其他 collector 凭据写入仓库配置。

推荐通过环境变量连接 OTLP collector：

```text
GitCandy__Observability__Otlp__Enabled=true
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
```

也可设置 `GitCandy:Observability:Otlp:Endpoint` 为绝对 HTTP/HTTPS 地址；留空时由 OpenTelemetry SDK 的标准环境变量或默认值决定。`TraceSamplingRatio` 接受 `0` 到 `1`，只影响 tracing；生产环境应根据流量降低采样率。`ConsoleExporterEnabled=true` 会把三类信号写到标准输出，仅用于本地诊断，不建议与生产日志长期同时启用。

GitCandy 不内置 collector、存储或 dashboard，也不暴露 Prometheus scrape endpoint。OTLP exporter 使用批量后台导出，collector 故障不会改变 Web/Git/SSH 协议响应。紧急回滚时先设置 `GitCandy__Observability__Otlp__Enabled=false`；若还需移除本地 provider，再设置 `GitCandy__Observability__Enabled=false` 并重启，不涉及数据库或文件系统迁移。

## Health checks

- `/health/live` 只确认 ASP.NET Core 进程可以处理请求，不访问依赖。
- `/health/ready` 验证数据库连接、repository 可写、cache 可写、`git --version` 和启用后的 SSH listener。

readiness 失败时返回 HTTP 503，默认响应不会暴露数据库连接串、物理路径或密钥内容。先查看应用日志，再检查目录 ACL、数据库文件、Git PATH 和 SSH 端口占用。

## Migration SQL

Release workflow 用固定在 `.config/dotnet-tools.json` 的 `dotnet-ef` 生成：

- `sqlite.sql`：从空数据库创建当前 SQLite schema。
- `sqlserver-idempotent.sql`：用于 SQL Server schema 审阅和后续独立部署验证；当前主 host 仍是 SQLite-first。

生产升级前先审阅对应版本 SQL 和 migration 差异，并完成备份。普通 GitCandy 启动会检测并自动应用 pending migrations；`GitCandy --migrate` 保留为只迁移后退出的运维入口。自动迁移只处理当前 EF Core schema，不导入或改写旧 MVC5 用户数据库。

## 备份和恢复

一致性备份必须阻止新 Web/SSH push，并停止 GitCandy 进程。SQLite 的 database、`-wal`、`-shm` 文件不能在写入期间随意分别复制。

1. 停止 `gitcandy` 容器或系统服务。
2. 备份数据库文件、完整 repositories、SSH host key、Data Protection keys 和生产配置。
3. cache 可跳过；恢复后由应用重建。
4. 记录备份对应的 GitCandy image/version 和 migration 版本。
5. 重新启动并验证 readiness、登录、clone/fetch/push 和 SSH host key 指纹。

发布门禁使用 `tools/operations/Invoke-RecoveryRehearsal.ps1` 对 `GitCandy.db`、repositories、LFS 和 Data Protection keys 建立同一 SHA-256 清单并恢复到隔离目录。生产演练仍必须先停止写入；脚本不会替代 SQLite 在线备份协议，也不会覆盖活动数据目录。版本回滚只能回到兼容当前 schema 的版本；否则先恢复与旧版本同时取得的完整一致性备份。

Compose 可在停止主服务后复制持久目录：

```bash
docker compose stop gitcandy
docker compose cp gitcandy:/var/lib/gitcandy ./backup/gitcandy
docker compose start gitcandy
```

恢复时先停止服务，把当前损坏状态另存为诊断副本，再将同一备份集整体恢复到原路径并修复 owner/ACL。不要把旧数据库与较新的 repository/配置状态随意混合。

## 升级和回滚

升级：

1. 固定当前运行版本并完成一致性备份。
2. 下载新版本产物，审阅 CHANGES 和 migration SQL。
3. 停止旧服务并启动新版本；新进程会在开放 Web/SSH 端口前自动应用 pending migrations。
4. 验证 `/health/ready`、登录、公开/私有权限、HTTP clone/fetch/push 和 SSH clone/fetch/push。

回滚：

1. 立即停止新版本，避免继续写入。
2. 恢复升级前的程序或 image tag。
3. 如果新版本执行过 database migration，必须恢复升级前数据库快照；不要让旧二进制连接新 schema。
4. 只有 repositories 在升级后发生写入或损坏时才恢复对应备份，并确认数据库 metadata 与物理 refs 一致。
5. 恢复原配置、SSH host key 和 Data Protection keys，启动旧版本并重复健康检查和 Git 协议 smoke tests。

Compose 回滚应把 `.env` 中 `GITCANDY_IMAGE` 改回明确旧 tag 后执行 `docker compose up -d`。不要使用浮动 `latest` 作为可审计回滚点。
