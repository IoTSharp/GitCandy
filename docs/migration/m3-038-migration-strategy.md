# M3 #038 Migration 策略

记录日期：2026-07-10

## 验收结论

- SQLite 与 SQL Server 分别由 `GitCandy.Data.Sqlite`、`GitCandy.Data.SqlServer` 持有独立 `InitialIdentitySchema` migration 和 model snapshot。
- 两个初始 migration 都代表 ASP.NET Core 迁移主线的新 Identity + GitCandy 领域 schema，不把旧 `Users`、`AuthorizationLog`、`PasswordVersion` 当作 baseline。
- SQL Server provider 可生成 idempotent migration SQL；smoke test 离线审阅 Identity/领域表、主外键、唯一索引和 provider 类型，不要求测试机运行 SQL Server。
- 主程序仍只注册 SQLite provider，不在应用启动时自动执行 production migration；切换运行 provider 属于后续部署切片。
- 旧仓库、团队和非密码资料若需要迁移，必须通过独立导入工具读取旧库并写入新 schema；旧密码、cookie 和授权日志不导入。

## Provider 差异

| 语义 | SQLite | SQL Server |
| --- | --- | --- |
| long 自增主键 | `INTEGER` + autoincrement | `bigint IDENTITY` |
| bool | `INTEGER` | `bit` |
| UTC 时间 | `TEXT` | `datetime2` |
| SSH fingerprint | fixed-length metadata，SQLite 不强制长度 | `nchar(47)` |
| 名称大小写不敏感唯一 | 应用写入 uppercase `NormalizedName` + unique index | 同一规范化字段 + unique index，不依赖数据库默认 collation |
| 发布 migration | 受控执行 SQLite migration | 先生成、审阅 idempotent SQL，再由部署流程执行 |

## 验证命令

```powershell
dotnet ef migrations has-pending-model-changes --project src/GitCandy.Data.Sqlite --startup-project src/GitCandy.Data.Sqlite --context GitCandyDbContext
dotnet ef migrations has-pending-model-changes --project src/GitCandy.Data.SqlServer --startup-project src/GitCandy.Data.SqlServer --context GitCandyDbContext
dotnet ef migrations script --project src/GitCandy.Data.SqlServer --startup-project src/GitCandy.Data.SqlServer --context GitCandyDbContext --idempotent
```

生成到文件的 SQL 属于发布产物，不提交仓库。M3 验收未连接真实 SQL Server；实际执行、备份和生产回滚演练留在 M8 发布闭环。

## 回滚边界

- 当前 migration 只针对全新的 ASP.NET Core 数据库，回滚应用不会修改旧 MVC5 数据库。
- 新库回滚使用已审阅的 EF migration down SQL 或恢复迁移前备份，不允许应用启动时自动降级 schema。
- 若导入工具后续写入新库，回滚前必须保留旧库只读副本和 repositories 文件系统备份。
