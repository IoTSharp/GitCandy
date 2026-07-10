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

新增的 `GitCandy:Application:DataProtectionKeysPath` 保存 Identity cookie 加密 key ring。它必须位于持久化、仅应用账户可写的目录；丢失后已有 cookie 全部失效。

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
