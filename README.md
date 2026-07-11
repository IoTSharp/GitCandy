# GitCandy

GitCandy is a lightweight self-hosted Git service built on ASP.NET Core 10 MVC and EF Core. One GitCandy process hosts the Web UI, Git Smart HTTP, the built-in SSH server, Quartz jobs, and background entry points.

## Supported deployments

- Docker Compose using `ghcr.io/iotsharp/gitcandy` or `iotsharp/gitcandy` from Docker Hub.
- Linux x64 as a self-contained systemd service.
- Windows x64 as a self-contained Windows Service.

IIS deployment is not supported. Put a TLS reverse proxy in front of GitCandy when exposing the Web UI because Identity cookies are Secure-only.

## Docker Compose

```bash
cp .env.example .env
docker compose pull
docker compose up -d
docker compose ps
```

From a source checkout, `docker compose up --build -d` automatically loads the build settings from `docker-compose.override.yml`. The Release Compose package omits that override and pulls the prebuilt image.

GitCandy checks for pending EF Core migrations and upgrades the SQLite/Identity database before Web, SSH, and background services start. Persistent state is stored in the `gitcandy-data` volume. HTTP and SSH default to host ports `8080` and `2222`.

Images are published to both registries:

```bash
docker pull ghcr.io/iotsharp/gitcandy:latest
docker pull iotsharp/gitcandy:latest
```

Tagged GitHub Releases also contain Linux and Windows service packages, migration SQL, Compose files, and a loadable image archive.

## Operations

- Liveness: `/health/live`
- Readiness: `/health/ready`
- OpenTelemetry tracing, metrics, and logging with optional OTLP export
- Optional migration-only command: `GitCandy --migrate`
- Detailed deployment, configuration, backup, restore, and rollback guide: [docs/deployment.md](docs/deployment.md)
- Database provider notes: [docs/database-providers.md](docs/database-providers.md)
- Migration roadmap: [ROADMAP.md](ROADMAP.md)
- Changes: [CHANGES.md](CHANGES.md)

## Development

.NET 10 SDK and Node.js 20 or later are required. Node is used only to bundle the self-hosted CSS, JavaScript, and Lucide icons; published deployments do not require Node or a CDN.

```bash
dotnet tool restore
dotnet restore GitCandy.slnx
dotnet build GitCandy.slnx
dotnet test GitCandy.slnx
```

The client bundle is rebuilt incrementally by MSBuild from `src/GitCandy/ClientApp`. See [the M9 UI implementation record](docs/design/m9-ui-implementation.md) for asset, theme, visual baseline, and rollback details.

`GitCandy.slnx` is the active ASP.NET Core solution. The legacy `GitCandy.sln` and MVC5 source remain only as migration behavior references.

## License

MIT. See [LICENSE.md](LICENSE.md).
