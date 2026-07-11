## GitCandy Changes

---
### Unreleased
#### Added
 - Added M12 #133 inline Pull Request review threads with server-validated old/new line ranges, replies, resolve/reopen, immutable original anchors, and unique hunk-context remapping that explicitly marks missing or ambiguous matches as outdated.
 - Added SQLite and SQL Server review-thread migrations plus real bare-repository anchor tests and migration-backed thread lifecycle coverage.
 - Added M12 #132 Pull Request Conversation, paged Commits, and collapsible Files changed views backed by immutable SHA snapshots and merge-base diffs, including rename detection, binary/large-diff degradation, and fixed-SHA commit links.
 - Added the first M12 Pull Request vertical slice at `/{namespace}/{repository}/pulls`, including same-repository branch comparison, shared Issue/PR numbering, draft/ready, edit, close/reopen, timeline, and private-repository suppression.
 - Added SQLite and SQL Server `PullRequests` and `PullRequestTimelineEvents` migrations with original/current base/head snapshots, merge-result placeholders, optimistic concurrency, and one-open-PR-per-branch-pair enforcement.
 - Added server-maintained, fetchable `refs/pull/{number}/head` refs with additive `receive.hideRefs` protection, real bare-repository coverage, and Kestrel create/detail/private-denial integration coverage.
 - Added the M11 repository Issue workflow at `/{namespace}/{repository}/issues`, including create/edit/open/close/reopen, comments, timeline, labels, milestones, assignees, subscriptions, relations, discussion locking, and a persistent notification inbox.
 - Added repository-scoped shared work-item numbering, optimistic concurrency, edit history, references, notification, and metadata tables with SQLite and SQL Server migrations.
 - Added restricted CommonMark rendering with fenced code, task lists, mentions, work-item/commit links, disabled raw HTML, and final HTML allow-list sanitization through Markdig and HtmlSanitizer.
 - Added repository Issue templates from `.gitcandy/ISSUE_TEMPLATE/{name}.md`, with `default.md` fallback, bounded content, validated names, and query-string prefill support.
 - Added idempotent `fixes`, `closes`, and `resolves #N` processing after successful Smart HTTP and built-in SSH pushes to the default branch HEAD.
 - Added SQLite concurrency/XSS/notification tests, SQL Server migration SQL checks, and a Kestrel Issue create/list/detail/private-denial smoke test.
 - Added M10 stable user/team namespaces, namespace and repository aliases, global name claims, rename audit events, explicit legacy repository mappings, and immutable physical repository storage names.
 - Added canonical `/{namespace}/{repository}[.git]` Web and Git Smart HTTP routes plus matching SSH paths, all resolved to stable repository IDs before authorization and transport execution.
 - Added configurable 365-day alias retention, rolling three-per-seven-day namespace rename limits, idempotent background expiry cleanup, administrator extension/override controls, and rename/alias management pages.
 - Added canonical-address warnings for historical SSH remotes through OpenSSH stderr and built-in SSH RFC 4254 extended data without modifying Git transport stdout.
 - Added SQLite concurrency and alias lifecycle tests, SQL Server migration SQL coverage, and real Git HTTP/SSH canonical, alias, legacy clone/fetch/push coverage.
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
 - Added M9 #094 architecture dependency gates for the Core, Data, Git, SSH, provider, and Web projects.
 - Added M9 #095 OpenTelemetry tracing, metrics, and structured logging providers for ASP.NET Core requests, .NET runtime, Git transport, and Quartz jobs.
 - Added optional OTLP and diagnostic Console exporters with configuration validation and sanitized Git telemetry tags.
 - Added LibGit2Sharp 0.31.0 managed repository operations for bare initialization, repository validation, and HEAD/commit/branch/tag snapshots.
 - Added an optional, disabled-by-default OpenSSH AuthorizedKeysCommand and key-specific forced-command adapter that reuses Identity SSH keys, repository permissions, path validation, and the shared Git transport backend.
 - Added the M9 npm lockfile + esbuild frontend asset pipeline with self-hosted Lucide icons and a Docker-only Node build stage.
 - Added System/Light/Dark theme selection with first-paint Razor rendering, `.GitCandy.Theme` persistence, and responsive application navigation.
 - Added Light/Dark desktop/mobile Playwright screenshot baselines and MVC smoke coverage for the new production assets.
 - Added M9 #106-#108 repository lifecycle and code workspace services for bare creation, credential-free import, fork networks, default branches, safe deletion, tree/blob/raw, commit/diff/blame/compare, and streamed ZIP archives.
 - Added fixed-commit code permalinks, `#Lx-Ly` line selection/copying, bundled syntax highlighting, and explicit binary, unknown-encoding, large-file, symlink, submodule, diff, archive, and path boundaries.
 - Added M9 #109 Git LFS v2 basic batch, upload, download, existence, and verify endpoints with repository authorization, quotas, temporary SHA-256 validation, atomic object commits, and range downloads.
 - Added real `git lfs` push/fetch/clone integration coverage and repository browser/lifecycle boundary tests.

#### Changed
 - Repository navigation now exposes canonical Pull Request lists, while creation requires authenticated repository write permission and rechecks the same permission in the application service.
 - Private repository Issue routes and notification reads now recheck repository read permission and suppress inaccessible work-item existence with a not-found result.
 - User and team display names are now independent from URL slugs; changing display text does not change repository URLs.
 - Repository deletion retains current and historical names as reserved tombstone claims while removing the physical Git/LFS data and legacy route target.
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
 - Split framework-independent contracts into `GitCandy.Core`, Git transport into `GitCandy.Git`, EF/Identity application implementations into `GitCandy.Data`, and the complete hosted SSH runtime into `GitCandy.Ssh`; `GitCandy` remains the single-process MVC host and composition root.
 - Git transport and scheduler operations now emit low-cardinality duration, active-operation, result, and error telemetry without repository, user, or physical-path attributes.
 - Git transport now delegates repository discovery and validity checks to the managed LibGit2Sharp repository service; external process launches remain limited to the three official Git wire-protocol helpers and readiness checking.
 - Modernized repository, account, Identity security, SSH key, team, user administration, and read-only settings Razor views while preserving routes, form fields, antiforgery, authentication, and authorization behavior.
 - Mobile navigation now uses inert content, trapped focus, Escape/backdrop dismissal, and focus return; shared UI states support keyboard focus, reduced motion, safe errors, empty results, and destructive confirmations.
 - Repository create/delete now coordinates EF metadata with physical Git and LFS storage instead of operating on metadata only.
 - Added nullable `ForkedFromRepository` and `ForkNetworkRoot` columns to SQLite and SQL Server repository schemas.

#### Removed
 - Removed the migrated host's static `GitCandy.Log.Logger` compatibility adapter, legacy log rotation job, and unused `LogPathFormat` setting. Runtime logging now uses only dependency-injected `ILogger<T>` instances and ASP.NET Core logging providers.
 - Removed Bootstrap 3, bootstrap-switch, jQuery 2, Glyphicons, marked, the legacy highlight.js bundle, and their production static references after the M9 visual regression pass.

#### Migration
 - The M11 SQLite and SQL Server migrations add Issue collaboration tables and backfill one `WorkItemSequences` row per existing repository without changing Identity, repository storage, Git URLs, or Git wire behavior. Back up the database before upgrade; rollback requires restoring that backup with the previous application version. See [the M11 migration record](docs/migration/m11-issues.md).
 - The M10 SQLite and SQL Server migrations create stable namespaces for existing users/teams, assign each existing repository to its first owner namespace (or the reserved `legacy` namespace when no owner exists), preserve the existing physical directory as `StorageName`, and create explicit `/git/{project}` mappings.
 - Upgrades now fail on user/team/reserved-slug collisions instead of silently rewriting public URLs. Resolve those conflicts before applying the migration. Rollback requires restoring the pre-M10 database backup together with the previous application version; repository directories are not renamed by this migration.
 - Web authentication no longer accepts the legacy `_gc_auth` cookie, password hashes, `PasswordVersion`, or `AuthorizationLog`; users must be recreated in the ASP.NET Core Identity schema or imported later without passwords.
 - Selected MVC `AccountController` plus Razor Views for the migrated account UI; Git Smart HTTP now uses the independent M6 endpoint and authentication scheme.
 - Migrated the M5 account/team/repository public URL shapes to real ASP.NET Core controllers and replaced the Git HTTP compatibility placeholder in M6.
 - Repository CRUD now uses dedicated lifecycle operations through the established Git backend boundary; existing `{name}` and `{name}.git` physical layouts remain readable.
 - Added `GitCandy:RepositoryBrowser` resource limits and `GitCandy:Lfs` object-size, quota, buffering, timeout, and enablement settings. `CachePath/lfs` is persistent object data and must be backed up.
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
 - OpenTelemetry providers are enabled by default, while network and Console exporters remain disabled until configured under `GitCandy:Observability`; deployments may enable OTLP without changing application routes or persistence.
 - Release deployments persist SQLite, repositories, SSH host keys, and Data Protection keys outside the application directory; backup and rollback must treat them as one versioned recovery set.
 - External OpenSSH remains opt-in under `GitCandy:OpenSsh`; deployments using it must configure a dedicated OS account and `AuthorizedKeysCommand`, while rollback only requires disabling the adapter and restoring the built-in SSH listener.

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
