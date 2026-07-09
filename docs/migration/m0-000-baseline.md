# M0 #000 迁移分支与旧项目冻结基线

记录日期：2026-07-09

## 验收结论

- 迁移分支已建立：`migration/aspnet-core-10`。
- 分支基点为 `dev` 的 `83e15685583ee9f5c812d7341a92fdbb23a854a8`（`docs: standardize roadmap milestones`）。
- 旧 ASP.NET MVC 5 项目保留为行为参考，不作为 ASP.NET Core 迁移主线构建入口。
- 工作区基线已记录，后续迁移 PR 应继续先检查 `git status --short`，避免覆盖无关改动。

## 迁移分支

本地迁移分支从当前 `dev` HEAD 创建：

```powershell
git switch -c migration/aspnet-core-10
```

后续 M0/M1 迁移工作默认在 `migration/aspnet-core-10` 上推进。若需要回滚本任务，只需切回 `dev` 并删除该本地分支，同时撤销本文件、`ROADMAP.md` 和 `CHANGES.md` 的文档变更。

## 冻结范围

以下旧项目内容冻结为行为参考：

- `GitCandy.sln`
- `GitCandy/GitCandy.csproj`
- `GitCandy/` 下的 ASP.NET MVC 5 / .NET Framework 4.5 代码、Razor views、SQL 脚本、Git HTTP、SSH、scheduler 和配置文件

冻结含义：

- 不把新 SDK-style 项目加入旧 `GitCandy.sln`。
- 不把旧 MVC5 项目作为新迁移 CI 或本地默认构建入口。
- 不为了迁移外壳、数据层或测试基础设施改动旧业务代码。
- 后续只有在对应 `ROADMAP.md` 垂直切片明确要求迁移某个行为时，才修改旧代码或从旧代码搬迁实现。

ASP.NET Core 迁移主线以 `GitCandy.slnx` 为准。当前 `GitCandy.slnx` 包含：

- `src/GitCandy.Data/GitCandy.Data.csproj`
- `src/GitCandy.Data.Sqlite/GitCandy.Data.Sqlite.csproj`
- `src/GitCandy.Data.PostgreSql/GitCandy.Data.PostgreSql.csproj`
- `src/GitCandy.Data.SonnetDB/GitCandy.Data.SonnetDB.csproj`
- `tests/GitCandy.Data.Tests/GitCandy.Data.Tests.csproj`

## 工作区基线

创建迁移分支前后，`git status --short` 仅显示：

```text
?? .vscode/
```

`.vscode/` 是既有未跟踪本机目录，本任务未读取或修改其中内容，也不把它纳入迁移基线。

## 构建与测试基线

已运行：

```powershell
dotnet --version
dotnet build GitCandy.slnx
dotnet test GitCandy.slnx
```

结果：

- .NET SDK：`10.0.301`
- `dotnet build GitCandy.slnx`：通过，0 个警告，0 个错误
- `dotnet test GitCandy.slnx`：通过，12 个测试，0 个失败，0 个跳过

未运行：

- 旧 `GitCandy.sln` / MVC5 项目构建。原因：M0 #000 只冻结旧项目为行为参考，不改变旧项目，也不把旧 solution 作为迁移主线验证入口。
- MVC 登录、Git HTTP clone/fetch/push、SSH clone/fetch/push。原因：这些分别由 M0 #002 到 #008 建立行为清单和可重复脚本。

## 兼容性影响

本任务只新增和更新迁移基线文档，不改变：

- 公开路由和 Git URL
- 数据库 schema、索引、默认值或 seed 数据
- Identity cookie、Basic Auth 或权限语义
- 配置键、环境变量或部署方式
- Git HTTP/SSH 协议行为和响应 header
- repository、cache、App_Data、host keys、logs 等文件系统布局
