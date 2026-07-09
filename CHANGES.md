## GitCandy Changes

---
### Unreleased
#### Added
 - Added the ASP.NET Core migration data-layer baseline with provider-neutral `GitCandyDbContext` and `GitCandyUser`.
 - Added EF Core provider registration projects for SQLite, PostgreSQL/pgsql, and SonnetDB, with separate migration assembly boundaries.
 - Added SQLite data-layer smoke tests for database creation and Identity user read/write.
 - Added the M0 #000 migration branch and legacy MVC5 freeze baseline record.
 - Added the M0 #001 repeatable sample data and bare repository fixture generator.

#### Migration
 - New database configuration reads `GitCandy:Database:Provider` and also accepts IoTSharp-style top-level `DataBase`.
 - Calibrated the ASP.NET Core migration roadmap so `GitCandy.slnx` is the active migration solution while the legacy `GitCandy.sln` remains behavior reference only.
 - Calibrated the database migration gate to require SQLite by default plus a viable SQL Server schema/migration path; PostgreSQL and SonnetDB are optional provider extensions.
 - Standardized the ASP.NET Core migration roadmap to use a single Milestone label set (`M0`-`M10`).

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
