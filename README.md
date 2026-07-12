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

For automatic HTTPS with Caddy, set `GITCANDY_DOMAIN` to a DNS name that points at the host and start the TLS overlay:

```bash
GITCANDY_DOMAIN=git.example.com docker compose -f docker-compose.yml -f docker-compose.tls.yml up -d
```

From a source checkout, `docker compose up --build -d` automatically loads the build settings from `docker-compose.override.yml`. The Release Compose package omits that override and pulls the prebuilt image.

GitCandy checks for pending EF Core migrations and upgrades the SQLite/Identity database before Web, SSH, and background services start. Persistent state is stored in the `gitcandy-data` volume. HTTP and SSH default to host ports `8080` and `2222`.

Images are published to both registries:

```bash
docker pull ghcr.io/iotsharp/gitcandy:latest
docker pull iotsharp/gitcandy:latest
```

Tagged GitHub Releases also contain Linux and Windows service packages, migration SQL, Compose files, and a loadable image archive.

## Repository URLs

The canonical repository page is `/{namespace}/{repository}`. Git Smart HTTP and LFS use `/{namespace}/{repository}.git`; SSH uses `ssh://git@host:port/{namespace}/{repository}.git`. A namespace belongs to an Identity user or team and is case-insensitively unique across both owner types.

Repository routes are a direct cutover. Legacy `/git/{project}[.git]`, `/Repository/...` browsing routes, no-suffix HTTP/SSH Git remotes, and retained rename aliases are not served or redirected. They return not found; clients must update their remote to the current namespace/repository `.git` URL.

Name history defaults are configured under `GitCandy:Namespaces`:

```json
{
  "AliasRetentionDays": 365,
  "RenameLimit": 3,
  "RenameWindowDays": 7
}
```

The rename limit applies to successful user/team namespace slug changes in a rolling window. Display-name changes do not create aliases or consume the limit. See [the M10 migration record](docs/migration/m10-stable-namespaces.md) before upgrading an existing database.

## Repository Issues

Each readable repository exposes an Issue workspace at `/{namespace}/{repository}/issues`. Authenticated readers can create and discuss Issues; authors, assignees, repository owners, and administrators receive the corresponding edit and state-management permissions. Private repository Issue routes, references, and inbox notifications always recheck repository read access.

Issue descriptions and comments use restricted CommonMark with fenced code blocks and task lists. Raw HTML is disabled and rendered HTML is sanitized before storage. Repository owners can manage labels, milestones, assignees, relations, subscriptions, and discussion locks. Templates live at `.gitcandy/ISSUE_TEMPLATE/{name}.md`; `default.md` is used when no name is supplied. Successful Smart HTTP or built-in SSH pushes apply `fixes #N`, `closes #N`, and `resolves #N` from the default branch HEAD idempotently.

See [the M11 migration record](docs/migration/m11-issues.md) for schema, dependency, backup, and rollback details.

Canonical repositories expose `/branches`, `/tags`, and `/contributors`. Writers can delete non-default branches and tags through antiforgery-protected forms; ref validation and default-branch protection are enforced again by the Git service.

## Operations

- Liveness: `/health/live`
- Readiness: `/health/ready`
- OpenTelemetry tracing, metrics, and logging with optional OTLP export
- Optional migration-only command: `GitCandy --migrate`
- Detailed deployment, configuration, backup, restore, and rollback guide: [docs/deployment.md](docs/deployment.md)
- Database provider notes: [docs/database-providers.md](docs/database-providers.md)
- `gitcandy.com` on the existing sonnet.vip host: [deploy/sonnet-vip/README.md](deploy/sonnet-vip/README.md)
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

`GitCandy.slnx` is the only active solution. The retired MVC5 source remains available through Git history, while its behavior baselines remain under `docs/migration`.

## Acknowledgements

Thanks to [sonnet.vip](https://sonnet.vip/) for providing the server resources used by the GitCandy deployment.

## License

MIT. See [LICENSE.md](LICENSE.md).
