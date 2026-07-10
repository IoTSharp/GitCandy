# M3 #039 数据层 smoke tests

记录日期：2026-07-10

## 验收矩阵

| 场景 | 结果 |
| --- | --- |
| SQLite `MigrateAsync` 新库 | 创建 Identity 标准表、领域表、索引和 migration history |
| Identity 存储 | `UserManager` 创建带密码用户，重新读取后正确密码通过、错误密码失败 |
| 领域 CRUD | 用户、团队、仓库、SSH key 和三类角色关系可创建、读取、写入 |
| 权限语义 | 匿名、公有/私有、owner、team、administrator 和无角色场景通过 |
| 数据完整性 | Identity user id FK、cascade、required、长度 metadata、唯一索引和大小写规范化通过 |
| 查询行为 | 禁止 lazy loading，未 `Include` 不隐式加载，显式查询行为可测 |
| 注入生命周期 | scoped context 仅在 scope 内共享，factory 并发创建独立 context |
| SQL Server migration | idempotent SQL 包含 Identity/领域 schema、SQL Server 类型与唯一索引，不含旧认证表 |

## 验证入口

```powershell
dotnet restore GitCandy.slnx
dotnet build GitCandy.slnx --no-restore
dotnet test tests/GitCandy.Data.Tests/GitCandy.Data.Tests.csproj --no-restore
dotnet test GitCandy.slnx --no-build --no-restore
```

M3 数据层测试不等同于 M4 浏览器 Identity cookie 登录测试；本阶段只验证 Identity store、密码 hash 校验和数据层权限查询。PostgreSQL/SonnetDB 的 migration SQL 与真实 SQL Server 部署验证也不在 M3 当前运行验收范围。

## 兼容性影响

本闭环只增加 ASP.NET Core 新数据库 schema、SQL Server migration 路径、DI 生命周期和测试，不迁移旧密码/认证数据，不改变旧 SQL 脚本、公开 URL、Git HTTP/SSH 协议或 repositories 文件系统布局。
