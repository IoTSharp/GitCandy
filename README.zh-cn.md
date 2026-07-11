# GitCandy

GitCandy 是基于 ASP.NET Core 10 MVC 与 EF Core 的轻量自托管 Git 服务。单个 GitCandy 进程承载 Web UI、Git Smart HTTP、内置 SSH、Quartz 调度和后台任务入口。

## 支持的部署方式

- Docker Compose，可使用 GHCR 的 `ghcr.io/iotsharp/gitcandy` 或 Docker Hub 的 `iotsharp/gitcandy`。
- Linux x64 自包含包，以 systemd 服务运行。
- Windows x64 自包含包，以 Windows Service 运行。

不支持 IIS 部署。对外提供 Web UI 时必须在 GitCandy 前配置 TLS 反向代理，因为 Identity cookie 仅允许通过 HTTPS 发送。

## Docker Compose

```bash
cp .env.example .env
docker compose pull
docker compose up -d
docker compose ps
```

在源码目录可执行 `docker compose up --build -d`，Compose 会自动加载 `docker-compose.override.yml` 中的构建参数。Release Compose 包不包含该重载文件，默认直接拉取预构建镜像。

GitCandy 会在 Web、SSH 和后台服务启动前检查 EF Core pending migrations，并自动创建或升级 SQLite/Identity 数据库。持久状态位于 `gitcandy-data` volume；HTTP 和 SSH 默认映射到宿主机 `8080` 与 `2222` 端口。

两个镜像仓库都可以拉取：

```bash
docker pull ghcr.io/iotsharp/gitcandy:latest
docker pull iotsharp/gitcandy:latest
```

带 tag 的 GitHub Release 同时提供 Linux/Windows 服务包、migration SQL、Compose 文件和可通过 `docker load` 导入的镜像归档。

## 运维入口

- 存活检查：`/health/live`
- 就绪检查：`/health/ready`
- OpenTelemetry tracing、metrics、logging 与可选 OTLP 导出
- 可选的仅迁移命令：`GitCandy --migrate`
- 部署、配置、备份、恢复与回滚：[docs/deployment.md](docs/deployment.md)
- 数据库 provider 说明：[docs/database-providers.md](docs/database-providers.md)
- 迁移路线图：[ROADMAP.md](ROADMAP.md)
- 变更记录：[CHANGES.md](CHANGES.md)

## 开发

```bash
dotnet tool restore
dotnet restore GitCandy.slnx
dotnet build GitCandy.slnx
dotnet test GitCandy.slnx
```

`GitCandy.slnx` 是当前唯一活动 solution。已退役的 MVC5 源码可通过 Git 历史查阅，行为基线继续保留在 `docs/migration`。

## 协议

MIT，参见 [LICENSE.md](LICENSE.md)。
