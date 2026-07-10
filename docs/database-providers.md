# GitCandy EF Core Database Providers

GitCandy's ASP.NET Core migration uses SQLite first while the main Web, Identity, repository, Git HTTP, SSH, and scheduler slices are still being brought up. SQL Server has a separate provider and initial migration so its schema SQL can be generated and reviewed. PostgreSQL/pgsql and SonnetDB remain visible in the provider plan, but their migration SQL, schema differences, and deployment validation are handled after the main migration path works end to end.

The design still mirrors IoTSharp's provider-neutral direction: keep the base `DbContext` provider-neutral, while the active host registration and business implementation stay on SQLite for the current vertical slices.

Current implementation status:

| Provider | Status | Notes |
| --- | --- | --- |
| SQLite | Active implementation provider | Default local provider and current smoke-test target |
| SQL Server | Migration validation provider | Independent provider/migrations assembly; idempotent Identity/domain SQL is covered without requiring a live server |
| PostgreSQL | Optional implemented extension | Existing optional provider work stays visible but is not expanded during the first vertical slices |
| SonnetDB | Optional implemented extension | Existing optional provider work stays visible but is not expanded during the first vertical slices |

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
| `GitCandy.Data.SonnetDB` | Optional SonnetDB `UseSonnetDB` registration and future SonnetDB migrations |

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

Optional SonnetDB:

```powershell
dotnet ef migrations add InitialCreate --project src/GitCandy.Data.SonnetDB --startup-project src/GitCandy.Data.SonnetDB --context GitCandyDbContext --output-dir Migrations
```

Do not run production schema changes automatically on application startup. Generate and review migration SQL during release preparation, and do not commit generated `artifacts/` output. `EnsureCreated` is acceptable for early smoke tests only; it is not a release migration strategy.
