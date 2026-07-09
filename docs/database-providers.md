# GitCandy EF Core Database Providers

GitCandy's ASP.NET Core migration uses EF Core provider selection from configuration. The first migration gate is SQLite as the default local provider plus a viable SQL Server schema/migration path. PostgreSQL/pgsql and SonnetDB are optional provider extensions: they can remain in the solution, but they do not replace the SQLite + SQL Server baseline required for the ASP.NET Core migration.

The design still mirrors IoTSharp's provider-neutral direction: keep the base `DbContext` provider-neutral, register the active provider from configuration, and keep provider-specific migrations in provider projects.

Current implementation status:

| Provider | Status | Notes |
| --- | --- | --- |
| SQLite | Implemented | Default local provider and current smoke-test target |
| SQL Server | Planned migration gate | Provider project and migration SQL still need to be added before M3 exits |
| PostgreSQL | Optional implemented extension | Useful for pgsql deployments, not a substitute for SQL Server verification |
| SonnetDB | Optional implemented extension | Useful for SonnetDB deployment profiles, not a substitute for SQL Server verification |

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
| SQL Server | Planned: `sqlserver`, `mssql`, `sql-server` |
| PostgreSQL | `pgsql`, `postgres`, `postgresql`, `npgsql` |
| SonnetDB | `sonnet`, `sonnetdb` |

## Project Layout

| Project | Responsibility |
| --- | --- |
| `GitCandy.Data` | Provider-neutral `GitCandyDbContext`, `GitCandyUser`, configuration reader, and DI builder |
| `GitCandy.Data.Sqlite` | SQLite `UseSqlite` registration and future SQLite migrations |
| `GitCandy.Data.SqlServer` | Planned SQL Server `UseSqlServer` registration and SQL Server migrations |
| `GitCandy.Data.PostgreSql` | Optional PostgreSQL `UseNpgsql` registration and future PostgreSQL migrations |
| `GitCandy.Data.SonnetDB` | Optional SonnetDB `UseSonnetDB` registration and future SonnetDB migrations |

EF Core only scaffolds migrations for the active provider. Every first-stage schema change must add SQLite and SQL Server migrations. Optional provider migrations are required only when that provider is included in the release scope.

## Migration Commands

SQLite:

```powershell
dotnet ef migrations add InitialCreate --project src/GitCandy.Data.Sqlite --startup-project src/GitCandy.Data.Sqlite --context GitCandyDbContext --output-dir Migrations
```

SQL Server, once `GitCandy.Data.SqlServer` is added:

```powershell
dotnet ef migrations add InitialCreate --project src/GitCandy.Data.SqlServer --startup-project src/GitCandy.Data.SqlServer --context GitCandyDbContext --output-dir Migrations
dotnet ef migrations script --project src/GitCandy.Data.SqlServer --startup-project src/GitCandy.Data.SqlServer --context GitCandyDbContext --idempotent --output artifacts/migrations/sqlserver/InitialCreate.sql
```

Optional PostgreSQL:

```powershell
dotnet ef migrations add InitialCreate --project src/GitCandy.Data.PostgreSql --startup-project src/GitCandy.Data.PostgreSql --context GitCandyDbContext --output-dir Migrations
```

Optional SonnetDB:

```powershell
dotnet ef migrations add InitialCreate --project src/GitCandy.Data.SonnetDB --startup-project src/GitCandy.Data.SonnetDB --context GitCandyDbContext --output-dir Migrations
```

Do not run production schema changes automatically on application startup. Generate and review migration SQL during release preparation. `EnsureCreated` is acceptable for early smoke tests only; it is not a release migration strategy.
