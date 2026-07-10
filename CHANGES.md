## GitCandy Changes

---
### Unreleased
#### Added
 - Added the ASP.NET Core migration data-layer baseline with provider-neutral `GitCandyDbContext` and `GitCandyUser`.
 - Added EF Core provider registration projects for SQLite, PostgreSQL/pgsql, and SonnetDB, with separate migration assembly boundaries.
 - Added SQLite data-layer smoke tests for database creation and Identity user read/write.
 - Added the M0 #000 migration branch and legacy MVC5 freeze baseline record.
 - Added the M0 #001 repeatable sample data and bare repository fixture generator.
 - Added the M0 #002 legacy MVC5 Web behavior checklist for account, team, repository, and repository browsing migration coverage.
 - Added the M0 #003 legacy Git Smart HTTP behavior checklist for clone, fetch, push, authentication failure, authorization failure, missing repository, and unsupported service coverage.
 - Added the M0 #004 legacy SSH behavior checklist for clone, fetch, push, host key, port, public key authentication, Git command dispatch, and authorization coverage.
 - Added the M0 #005 Identity and domain schema smoke-test skeleton with repository/team role tables and a reusable repository permission query entry point.
 - Added the M0 #006 repository permission service test baseline for anonymous, public, private, owner, team, administrator, and no-role access semantics.
 - Added the M0 #007 legacy MVC smoke test baseline script and documentation for home, repository list, login, main forms, and error page coverage.
 - Added the M0 #008 repeatable Git HTTP integration script for local clone, fetch, push, and authorization failure smoke coverage.
 - Added the M0 #009 migration security baseline and pull request validation template for Identity, cookie, security stamp, private repository access, and compatibility checks.
 - Added the M1 #010 `src/GitCandy` SDK-style ASP.NET Core 10 MVC host as the single GitCandy main process project.
 - Added the M1 #012 `GitCandy.slnx` validation script and GitHub Actions workflow so migration restore/build/test commands use the active `.slnx` solution.
 - Added the M1 #015 standard ASP.NET Core MVC pipeline with controller views, routing, static assets, error handling, HSTS, and HTTPS redirection.
 - Added the M1 #016 ASP.NET Core Identity, authentication cookie, authorization policy, session, and localization placeholders without migrating legacy authentication.
 - Added the M1 #017 ASP.NET Core compatibility placeholder routes for `/`, `/Repository`, `/Account/Login`, and legacy Git Smart HTTP URL shapes.
 - Added the M1 #018 `System.Web` entry-gate tests so new migration projects cannot reference legacy ASP.NET MVC5 or EF6 APIs.
 - Added the M1 #019 shell build validation record for `dotnet build GitCandy.slnx`.
 - Added the M2 #020 `GitCandy:Application` options model for migrated application configuration.
 - Added the M2 #021 `IGitCandyApplicationPaths` abstraction for content-root and web-root based application path resolution.
 - Added M2 #022 standard ASP.NET Core logging through dependency-injected `ILogger<T>` instances and configured logging providers.
 - Added the M2 #023 ASP.NET Core `IMemoryCache` registration and `IApplicationCache` wrapper for replacing legacy `HttpRuntime.Cache` usage.
 - Added the M2 #025 Quartz.NET in-memory scheduler hosted service, including a bridge for DI-registered `ISchedulerJob` tasks.
 - Added the M2 #026 SSH lifecycle placeholder hosted service, including an injectable `ISshServerRuntime` and tests for enabled/disabled startup and graceful shutdown token flow.
 - Added the M2 #027 ASP.NET Core request profiler middleware and DI accessor for replacing legacy `Application_BeginRequest` profiler startup.
 - Added the M2 #028 host startup/shutdown diagnostics, including clearer SSH runtime and Quartz scheduler startup failure logs.
 - Added the M2 #029 startup path validation for ASP.NET Core content/web root based configuration and repository/cache boundary checks.
 - Added the M3 #032 SQLite EF Core `InitialIdentitySchema` migration and model snapshot for ASP.NET Core Identity standard tables.
 - Added a migration-backed SQLite Identity schema smoke test that uses `MigrateAsync` and verifies `AspNetUsers`, `AspNetRoles`, claims, logins, roles, tokens, Identity indexes, and `__EFMigrationsHistory`.
 - Added the M3 #033 GitCandy domain table model for repositories, teams, user/team repository roles, team membership roles, and SSH public keys.
 - Added migration-backed SQLite domain table smoke coverage for table/index creation, SSH key persistence, Identity user id foreign keys, and duplicate SSH fingerprint rejection.
 - Added the M3 #034 Identity user id foreign key validation for `UserRepositoryRoles.UserId`, `UserTeamRoles.UserId`, and `SshKeys.UserId` against `AspNetUsers.Id`, including orphan user rejection and cascade cleanup.
 - Added the M3 #035 explicit EF Core schema constraints and index validation for Identity/domain key lengths, required fields, PK/FK metadata, cascade delete, unique indexes, and case-insensitive repository/team names.
 - Added the M3 #036 explicit no-lazy-loading policy and query behavior tests for the new EF Core domain model.
 - Added the M3 #037 pooled `IDbContextFactory<GitCandyDbContext>` boundary while retaining scoped `GitCandyDbContext` injection for request and application services.
 - Added the M3 #038 SQL Server provider project, separate initial migration/snapshot, and offline idempotent migration SQL validation for Identity and GitCandy domain tables.
 - Added the M3 #039 data-layer closure tests for scoped/factory context isolation, Identity password persistence, and SQLite/SQL Server schema coverage.
 - Added the M4 MVC account flow for Identity registration, username/email login, POST logout, password changes, and access-denied handling.
 - Added scoped `ICurrentUser` claims access and resource authorization policies for repository read/write/owner, team administrator, current user, and system administrator behavior.
 - Added the independent `GitCandy.GitBasic` authentication scheme with Identity password, failure-count, lockout, role-claim, and Basic challenge behavior.
 - Added M4 Kestrel/SQLite account integration tests and authentication/authorization tests covering security-stamp invalidation, stale cookies, public/private repository access, owner, team, and administrator semantics.
 - Added M5 ASP.NET Core MVC controllers and Razor Views for account, team, and repository metadata CRUD, member/collaborator management, account SSH keys, About, and read-only settings.
 - Added M5 application services and strongly typed view models so MVC controllers do not contain complex EF Core queries.
 - Added standard `Resources/SharedResource*.resx` localization for English, Simplified Chinese, and French.
 - Added Bootstrap 3, bootstrap-switch, jQuery, highlight.js, marked, common.js, and Glyphicon assets under `wwwroot` with direct static references.
 - Added Kestrel/SQLite MVC page smoke tests for form validation, antiforgery-protected CRUD, public/private repository visibility, localization, and static assets.
 - Added the ASP.NET Core Git Smart HTTP endpoint for both `/git/{repository}.git` and legacy no-suffix repository URLs.
 - Added a shared `IGitTransportBackend` with structured Git arguments, full-duplex streaming, cancellation, bounded diagnostics, and repository root/symlink boundary checks.
 - Added Git Smart HTTP protocol/header/gzip/authentication tests and real Kestrel/SQLite/Git client clone, fetch, authenticated push, and 24 MiB pack coverage.
 - Added the real in-process SSH listener for the ASP.NET Core host, with public-key Identity authentication, repository authorization, and shared `IGitTransportBackend` streaming.
 - Added persistent RSA SSH host-key generation and one-time import from the legacy user configuration XML.
 - Added real Git/OpenSSH clone, fetch, and push coverage plus listener lifecycle, occupied-port, host-key migration, and Quartz shutdown tests.
 - Added Docker Compose deployment with automatic startup migration and a persistent application data volume.
 - Added self-contained Linux systemd and Windows Service release packages and installation scripts.
 - Added liveness and readiness endpoints covering the database, repository/cache storage, Git backend, and built-in SSH listener.
 - Added tag-based release automation for Linux/Windows packages, migration SQL, a downloadable image archive, GHCR, and Docker Hub.
 - Added a persistent ASP.NET Core Data Protection key ring so Identity cookies survive container and service restarts.
 - Added M9 #091 account security management with TOTP authenticator setup, recovery codes, remembered browsers, and safe authenticator reset/disable flows.
 - Added optional generic OpenID Connect sign-in, external account registration, account linking/unlinking, and password setup for external-only accounts.
 - Added M9 #092 modern SSH protocol configuration tests and real OpenSSH coverage without legacy algorithm overrides.

#### Changed
 - Moved Linux/container production defaults for HTTP, SQLite, repository/cache storage, SSH host key, Data Protection keys, and SSH port into the main application configuration; Docker Compose no longer duplicates application settings as environment variables.
 - Moved source-image build settings from the release Compose definition into the automatically loaded `docker-compose.override.yml`.
 - GitCandy now detects and applies pending EF Core migrations before Web, SSH, Quartz, and other hosted services start; `--migrate` remains available as a migration-only compatibility command.
 - Hardened the Identity application cookie as `.GitCandy.Identity` with `HttpOnly`, `SecurePolicy=Always`, `SameSite=Lax`, an eight-hour lifetime, and sliding expiration.
 - Removed the unused ASP.NET Core Session registration and middleware; the migrated host and Git Basic authentication no longer emit or depend on `.GitCandy.Session`.
 - Private repositories now reject anonymous access even if inconsistent data enables anonymous read/write flags.
 - Changed `/Account/Logout` to an antiforgery-protected POST action.
 - Changed account, team, and repository delete operations from query-driven GET requests to antiforgery-protected POST forms.
 - Restored the legacy start-page behavior so `/` redirects to `/Repository`.
 - Repository, account, and team pages now filter private repository names using the current viewer's repository permissions.
 - Replaced the Git Smart HTTP 501 placeholder with streaming upload-pack and receive-pack behavior, including Git protocol v2 forwarding.
 - Git HTTP now returns 401 for missing/invalid Basic credentials, 403 for authenticated users without permission, and 404 for missing metadata or physical repositories.
 - Git pack authorization now follows the actual URL verb, closing the legacy query-service/verb authorization mismatch.
 - SSH now allows only `git-upload-pack`, `git-receive-pack`, and `git-upload-archive`; password login, shell, SFTP, forwarding, and arbitrary environment variables remain disabled.
 - Replaced the migrated custom SSH protocol stack with Microsoft Dev Tunnels SSH 3.12.36, using SHA-2 KEX/signatures, AES-GCM or AES-CTR, and SHA-2 MACs without CBC or SHA-1 negotiation.
 - Quartz now interrupts cancellation-aware jobs during host shutdown and waits for their cleanup to complete.
 - Deployment support now targets Docker Compose, Linux systemd, and Windows Service only; IIS is no longer supported.
 - Pinned the SQLite native runtime to `SQLitePCLRaw.lib.e_sqlite3` 3.53.3 because fresh release restores reject the vulnerable 2.1.11 transitive version.
 - Strengthened the default Identity password policy to 12 characters with at least four unique characters, uppercase, lowercase, digit, and non-alphanumeric requirements; the policy is configurable under `GitCandy:Identity:Password`.

#### Removed
 - Removed the migrated host's static `GitCandy.Log.Logger` compatibility adapter, legacy log rotation job, and unused `LogPathFormat` setting. Runtime logging now uses only dependency-injected `ILogger<T>` instances and ASP.NET Core logging providers.

#### Migration
 - Web authentication no longer accepts the legacy `_gc_auth` cookie, password hashes, `PasswordVersion`, or `AuthorizationLog`; users must be recreated in the ASP.NET Core Identity schema or imported later without passwords.
 - Selected MVC `AccountController` plus Razor Views for the migrated account UI; Git Smart HTTP now uses the independent M6 endpoint and authentication scheme.
 - Migrated the M5 account/team/repository public URL shapes to real ASP.NET Core controllers and replaced the Git HTTP compatibility placeholder in M6.
 - Repository CRUD remains metadata-only; bare repository creation/import/deletion must use dedicated lifecycle operations through the established Git backend boundary.
 - Migrated language selection to the standard ASP.NET Core culture cookie while temporarily retaining the legacy `Lang` cookie.
 - Kept settings read-only in M5; configuration persistence, process restart, and SSH host-key regeneration remain scoped to M8/M7.
 - Added optional `GitCandy:GitHttp` request-size, timeout, and stream-buffer settings; reverse proxies must configure matching body and timeout limits.
 - Physical Git repositories may retain the legacy `{name}` layout or use the M0 fixture `{name}.git` layout under the configured repository root.
 - Migrated the legacy `Web.config appSettings` key `UserConfiguration` to `appsettings.json` with a temporary legacy alias; `LogPathFormat` is intentionally not migrated because log destinations are provider-owned.
 - Replaced legacy `Server.MapPath`-style path assumptions in the ASP.NET Core host with `IWebHostEnvironment.ContentRootPath` and `WebRootPath` semantics.
 - Standardized the new host on constructor-injected `ILogger<T>`; log sinks are controlled by ASP.NET Core `Logging` providers instead of the old static file writer.
 - New database configuration reads `GitCandy:Database:Provider` and also accepts IoTSharp-style top-level `DataBase`.
 - Migrated the ASP.NET Core host scheduler lifecycle to Quartz.NET `AddQuartz` / `AddQuartzHostedService`; Quartz persistence, clustering, and scheduler UI remain out of scope for the first migration slice.
 - Migrated the new host's SSH lifecycle entry point to an ASP.NET Core `IHostedService` placeholder; the real SSH protocol listener and clone/fetch/push behavior remain scoped to M7.
 - Migrated the new host's lightweight request profiler from `Application_BeginRequest` to ASP.NET Core middleware backed by `HttpContext.Items`.
 - Migrated startup/shutdown diagnostics to ASP.NET Core hosted-service lifecycle logging for the new host.
 - Hardened the new host's path semantics so relative application paths must stay under the ASP.NET Core content/web root; external repository/cache locations should be configured as fully qualified paths.
 - Established the first migration-backed database creation path for the ASP.NET Core migration line; early `EnsureCreated` tests remain smoke coverage only, not release schema evidence.
 - Established separate SQLite and SQL Server migration assemblies for the new schema; the application host remains SQLite-first and does not migrate production databases automatically at startup.
 - Calibrated the ASP.NET Core migration roadmap so `GitCandy.slnx` is the active migration solution while the legacy `GitCandy.sln` remains behavior reference only.
 - Calibrated the database migration strategy so short-term business implementation and validation use SQLite first; SQL Server, PostgreSQL, and SonnetDB remain visible follow-up provider work after the main migration path is working end to end.
 - Standardized the ASP.NET Core migration roadmap to use a single Milestone label set (`M0`-`M10`).
 - Renamed the planned ASP.NET Core host path from `src/GitCandy.Web` to `src/GitCandy` to reflect the single-process main-program architecture.
 - Replaced the M2 SSH lifecycle placeholder with the M7 in-process listener. Configure `GitCandy:Application:EnableSsh`, `SshPort`, and `SshHostKeyPath`; existing RSA host keys can be imported from `UserConfigurationPath`.
 - Existing RSA host keys and SSH URLs remain valid after the protocol replacement. Modern OpenSSH clients no longer require legacy algorithm options; clients limited to SHA-1 SSH algorithms must be upgraded or temporarily use the previous stack during rollback.
 - OpenID Connect remains disabled by default. Enabling it requires provider authority/client settings and a protected client secret; rollback is configuration-only and does not require a database downgrade.
 - Release deployments persist SQLite, repositories, SSH host keys, and Data Protection keys outside the application directory; backup and rollback must treat them as one versioned recovery set.

---
### GitCandy v0.2 - [view diff](http://github.com/Aimeast/GitCandy/compare/v0.1...v0.2) - Jul 27, 2016
#### Features
 - SSH transport protocol

#### Changes
 - Improve cache layer
 - Improve code highlight
 - Update to bootstrap 3
 - Miscellaneous improvements
 - Minor bug fixes

#### How to update
 - [Download](http://github.com/Aimeast/GitCandy/releases/tag/v0.2) or [build](http://github.com/Aimeast/GitCandy/tree/v0.2) the version 0.2
 - Update database script [MsSql](http://github.com/Aimeast/GitCandy/commit/7d036b14ac74dd174350f38b155db0c320493997#diff-916bcc9941b9e4b725bcecdbf4e9fab8) or [Sqlite](http://github.com/Aimeast/GitCandy/commit/7d036b14ac74dd174350f38b155db0c320493997#diff-649512c4819b49503813847617535df6)

---
### GitCandy v0.1 - Apr 13, 2014
 - Initial release
