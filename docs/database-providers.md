# GitCandy EF Core Database Providers

GitCandy defaults to SQLite. The Web host can also select SonnetDB explicitly for the M12.6 production profile; the composition root registers only the configured provider. SQL Server remains a migration validation provider, while PostgreSQL stays an optional extension without production migration validation.

The design still mirrors IoTSharp's provider-neutral direction: keep the base `DbContext` provider-neutral, while the active host registration and business implementation stay on SQLite for the current vertical slices.

Current implementation status:

| Provider | Status | Notes |
| --- | --- | --- |
| SQLite | Active implementation provider | Default local provider and current smoke-test target |
| SQL Server | Migration validation provider | Independent provider/migrations assembly; idempotent Identity/domain SQL is covered without requiring a live server |
| PostgreSQL | Optional implemented extension | Existing optional provider work stays visible but is not expanded during the first vertical slices |
| SonnetDB | Production-profile provider | Independent migration assembly, Identity/repository smoke coverage, and the `gitcandy.com` deployment profile |

## Configuration

Preferred configuration shape:

```json
{
  "GitCandy": {
    "Database": {
      "Provider": "Sqlite",
      "ConnectionStringName": "GitCandy",
      "DbContextPoolSize": 128
    }
  },
  "ConnectionStrings": {
    "GitCandy": "Data Source=App_Data/GitCandy.db"
  }
}
```

For IoTSharp-style deployment profiles, `DataBase` is also accepted as a top-level provider key:

```json
{
  "DataBase": "PostgreSql",
  "ConnectionStrings": {
    "GitCandy": "Host=localhost;Database=GitCandy;Username=gitcandy;Password=change-me;Include Error Detail=true"
  }
}
```

SonnetDB profile example:

```json
{
  "GitCandy": {
    "Database": {
      "Provider": "SonnetDB"
    }
  },
  "ConnectionStrings": {
    "GitCandy": "Data Source=sonnetdb+http://sonnetdb:5080/gitcandy;Token=change-me;Timeout=100"
  }
}
```

Supported provider aliases:

| Provider | Accepted values |
| --- | --- |
| SQLite | `sqlite`, `sqlite3` |
| SQL Server | `sqlserver`, `mssql`, `sql-server` |
| PostgreSQL | `pgsql`, `postgres`, `postgresql`, `npgsql` |
| SonnetDB | `sonnet`, `sonnetdb` |

## Project Layout

| Project | Responsibility |
| --- | --- |
| `GitCandy.Data` | Provider-neutral `GitCandyDbContext`, `GitCandyUser`, configuration reader, and DI builder |
| `GitCandy.Data.Sqlite` | SQLite `UseSqlite` registration and future SQLite migrations |
| `GitCandy.Data.SqlServer` | SQL Server `UseSqlServer` registration and SQL Server migrations |
| `GitCandy.Data.PostgreSql` | Optional PostgreSQL `UseNpgsql` registration and future PostgreSQL migrations |
| `GitCandy.Data.SonnetDB` | SonnetDB `UseSonnetDB` registration and independent migrations |
| `external/SonnetDB` | Source submodule for the patched ADO.NET/EF provider and database engine |

EF Core only scaffolds migrations for the active provider. Every first-stage schema change must add SQLite and SQL Server migrations. Optional provider migrations are required only when that provider is included in the release scope.

## Migration Commands

Restore the repository-pinned EF Core tool before running migration commands:

```powershell
dotnet tool restore
```

SQLite:

```powershell
dotnet ef migrations add InitialCreate --project src/GitCandy.Data.Sqlite --startup-project src/GitCandy.Data.Sqlite --context GitCandyDbContext --output-dir Migrations
```

SQL Server:

```powershell
dotnet ef migrations has-pending-model-changes --project src/GitCandy.Data.SqlServer --startup-project src/GitCandy.Data.SqlServer --context GitCandyDbContext
dotnet ef migrations script --project src/GitCandy.Data.SqlServer --startup-project src/GitCandy.Data.SqlServer --context GitCandyDbContext --idempotent --output artifacts/migrations/sqlserver/InitialIdentitySchema.sql
```

Optional PostgreSQL:

```powershell
dotnet ef migrations add InitialCreate --project src/GitCandy.Data.PostgreSql --startup-project src/GitCandy.Data.PostgreSql --context GitCandyDbContext --output-dir Migrations
```

SonnetDB:

```powershell
dotnet ef migrations has-pending-model-changes --project src/GitCandy.Data.SonnetDB --startup-project src/GitCandy.Data.SonnetDB --context GitCandyDbContext
dotnet ef database update --project src/GitCandy.Data.SonnetDB --startup-project src/GitCandy.Data.SonnetDB --context GitCandyDbContext --connection "Data Source=path/to/test-database"
```

The GitCandy host checks pending migrations and applies them before Web, SSH, and background services start. A failed migration prevents the process from listening. Do not use `EnsureCreated` for production. Initialize submodules before restore/build because the SonnetDB project uses the pinned source submodule rather than the published package.
