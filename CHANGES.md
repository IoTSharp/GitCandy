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
 - Added the M2 #022 legacy `GitCandy.Log.Logger` adapter for routing migrated static logger calls through ASP.NET Core logging.
 - Added the M2 #023 ASP.NET Core `IMemoryCache` registration and `IApplicationCache` wrapper for replacing legacy `HttpRuntime.Cache` usage.
 - Added the M2 #025 Quartz.NET in-memory scheduler hosted service, including a bridge for DI-registered `ISchedulerJob` tasks.
 - Added the M2 #026 SSH lifecycle placeholder hosted service, including an injectable `ISshServerRuntime` and tests for enabled/disabled startup and graceful shutdown token flow.
 - Added the M2 #027 ASP.NET Core request profiler middleware and DI accessor for replacing legacy `Application_BeginRequest` profiler startup.

#### Migration
 - Migrated legacy `Web.config appSettings` keys `LogPathFormat` and `UserConfiguration` to `appsettings.json` with temporary legacy aliases.
 - Replaced legacy `Server.MapPath`-style path assumptions in the ASP.NET Core host with `IWebHostEnvironment.ContentRootPath` and `WebRootPath` semantics.
 - Migrated the new host's legacy logger compatibility entry point to `ILoggerFactory`; log sinks are now controlled by ASP.NET Core `Logging` providers instead of the old static file writer.
 - New database configuration reads `GitCandy:Database:Provider` and also accepts IoTSharp-style top-level `DataBase`.
 - Migrated the ASP.NET Core host scheduler lifecycle to Quartz.NET `AddQuartz` / `AddQuartzHostedService`; Quartz persistence, clustering, and scheduler UI remain out of scope for the first migration slice.
 - Migrated the new host's SSH lifecycle entry point to an ASP.NET Core `IHostedService` placeholder; the real SSH protocol listener and clone/fetch/push behavior remain scoped to M7.
 - Migrated the new host's lightweight request profiler from `Application_BeginRequest` to ASP.NET Core middleware backed by `HttpContext.Items`.
 - Calibrated the ASP.NET Core migration roadmap so `GitCandy.slnx` is the active migration solution while the legacy `GitCandy.sln` remains behavior reference only.
 - Calibrated the database migration gate to require SQLite by default plus a viable SQL Server schema/migration path; PostgreSQL and SonnetDB are optional provider extensions.
 - Standardized the ASP.NET Core migration roadmap to use a single Milestone label set (`M0`-`M10`).
 - Renamed the planned ASP.NET Core host path from `src/GitCandy.Web` to `src/GitCandy` to reflect the single-process main-program architecture.

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
