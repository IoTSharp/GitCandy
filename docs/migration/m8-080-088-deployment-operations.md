# M8 #080-#088 部署、运维和文档

记录日期：2026-07-10

## 验收结论

- 正式部署方式收敛为 Docker Compose、Linux systemd 服务和 Windows Service；IIS 不再受支持。
- Release CI 生成 `linux-x64`、`win-x64` 两个自包含服务包、SQLite/SQL Server migration SQL、Compose 包和 linux/amd64 镜像归档。
- 同一版本镜像发布到 GHCR `ghcr.io/iotsharp/gitcandy` 和 Docker Hub `iotsharp/gitcandy`，镜像归档可从 GitHub Release 下载。
- `GitCandy --migrate` 是唯一应用内显式 migration 入口；普通启动不自动改 schema。Compose 和服务安装器都在首次启动前明确调用该命令。
- `/health/live` 提供无依赖 liveness；`/health/ready` 检查数据库、repository、cache、Git backend 和 SSH listener。
- repository、cache、SQLite、SSH host key、Data Protection keys 和 logs 都有固定持久化路径与权限边界。
- `docs/deployment.md` 给出旧配置对照、旧数据策略、备份恢复、SQL 审阅和版本回滚步骤。

## 对应 ROADMAP

| 编号 | 实现与证据 |
| --- | --- |
| #080 | `Dockerfile`、`docker-compose.yml`、`deploy/linux`、`deploy/windows`；README 明确不支持 IIS |
| #081 | `docs/deployment.md` 记录 `Web.config`/`config.xml` 到 JSON/环境变量对照 |
| #082 | 明确不兼容旧用户认证数据，旧 metadata 只能由独立导入工具处理 |
| #083 | 记录三种部署的 database/repository/cache/host key/key ring/logs 路径 |
| #084 | 五项 readiness check 与独立 liveness endpoint |
| #085 | 一致性停止、备份集、恢复顺序、ACL 和协议 smoke test 指南 |
| #086 | tag workflow 使用固定版本 `dotnet-ef` 生成 SQLite 与 SQL Server SQL |
| #087 | 基于固定版本、升级前备份和 schema 快照恢复的可执行回滚流程 |
| #088 | README、README.zh-cn、CHANGES、ROADMAP 和部署指南同步更新 |

## 发布和权限前提

GitHub repository 必须允许 Actions 写 packages 和 releases。Docker Hub 需要：

```text
DOCKERHUB_USERNAME
DOCKERHUB_TOKEN
```

该凭据必须有 `iotsharp/gitcandy` push 权限，且 GHCR package 与 Docker Hub repository 都必须设置为 public。workflow 在 push 后验证匿名 pull；缺少凭据或镜像不可公开拉取时会明确失败，不会只发布其中一个 registry 后假装完整成功。

## 兼容性、迁移和回滚

- 删除 IIS 发布支持属于部署兼容性变更。旧 IIS 站点必须迁移到 Compose 或对应 OS 服务包，回滚只能恢复旧 MVC5/IIS 版本及其完整备份。
- 服务模板默认 HTTP 8080、SSH 2222；公开 Git HTTP/Web 入口的 URL 应由 TLS reverse proxy 保持，SSH 外部端口可由防火墙映射为 22。
- 新增持久化 Data Protection key ring。升级前备份 key ring；丢失不会破坏数据库，但会让所有 Identity cookie 立即失效。
- 执行 migration 前必须备份。若回滚到旧二进制，必须同时恢复旧数据库 schema 快照，不能只替换程序。

## 验证记录

本里程碑验证应覆盖：

```powershell
dotnet tool restore
dotnet build GitCandy.slnx -c Release
dotnet test GitCandy.slnx -c Release
docker compose config --quiet
docker build -t gitcandy:m8 .
```

Release workflow 的 registry push 和 GitHub Release 创建只在 `v*` tag 环境执行；本地验证不使用或输出生产 registry 凭据。

本地最终验证结果：

- `dotnet build`：0 warning / 0 error。
- `GitCandy.Data.Tests`：41/41；`GitCandy.Tests`：62/62；总计 103/103。
- NuGet transitive vulnerability audit：全部 9 个项目无已知 vulnerable package。
- SQLite 与 SQL Server migration SQL 实际生成成功。
- `linux-x64`、`win-x64` self-contained publish 成功。
- Docker image 构建成功；Compose migration 成功退出，非 root 主容器 readiness/liveness 均为 HTTP 200。
