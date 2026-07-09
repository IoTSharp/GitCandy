# M1 #012 Solution 迁移

记录日期：2026-07-09

## 验收结论

- `GitCandy.slnx` 是 ASP.NET Core 迁移主线的活动 solution。
- 旧 `GitCandy.sln` 保留为 MVC5 行为参考，不作为新 SDK-style 项目的构建入口，也不加入新项目。
- 本地迁移验证命令固定到 `GitCandy.slnx`：`tools/migration/m1-012/Invoke-M1SolutionValidation.ps1`。
- GitHub Actions workflow 固定调用同一个验证脚本，CI 不依赖自动 solution 发现。

## Solution 范围

当前 `GitCandy.slnx` 包含：

- `src/GitCandy/GitCandy.csproj`
- `src/GitCandy.Data/GitCandy.Data.csproj`
- `src/GitCandy.Data.Sqlite/GitCandy.Data.Sqlite.csproj`
- `src/GitCandy.Data.PostgreSql/GitCandy.Data.PostgreSql.csproj`
- `src/GitCandy.Data.SonnetDB/GitCandy.Data.SonnetDB.csproj`
- `tests/GitCandy.Data.Tests/GitCandy.Data.Tests.csproj`

旧 `GitCandy.sln` 仍只包含 MVC5 项目，后续迁移期间只用于行为参考。

## 本地命令

```powershell
.\tools\migration\m1-012\Invoke-M1SolutionValidation.ps1
```

该脚本从仓库根目录执行：

- `dotnet restore .\GitCandy.slnx`
- `dotnet build .\GitCandy.slnx --configuration Debug --no-restore`
- `dotnet test .\GitCandy.slnx --configuration Debug --no-build`

需要 CI 口径时使用：

```powershell
.\tools\migration\m1-012\Invoke-M1SolutionValidation.ps1 -Configuration Release
```

## CI 入口

`.github/workflows/aspnet-core-migration.yml` 在 `master` push 和 PR 中对以下迁移输入变更运行：

- `GitCandy.slnx`
- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`
- `src/**`
- `tests/**`
- workflow 自身

CI 使用 `global.json` 安装 SDK，并调用 `Invoke-M1SolutionValidation.ps1 -Configuration Release`。

## 兼容性影响

本任务只固定迁移 solution 和验证入口，不改变：

- 公开路由和 Git URL。
- 数据库 schema、索引、默认值或 seed 行为。
- Identity cookie、Basic Auth、SSH public key 或权限语义。
- 配置键、环境变量或部署方式。
- Git HTTP/SSH 协议行为和响应 header。
- repository、cache、App_Data、host keys、logs 等文件系统布局。

## 本任务验证

已运行：

- `.\tools\migration\m1-012\Invoke-M1SolutionValidation.ps1`：通过，Debug build 0 警告/0 错误，25 个测试通过。
- `.\tools\migration\m1-012\Invoke-M1SolutionValidation.ps1 -Configuration Release`：通过，Release build 0 警告/0 错误，25 个测试通过。

未运行：

- SQLite 数据读写 smoke test。原因：本任务只固定 solution/CI 入口，不改变数据层行为。
- MVC 登录和主要页面 smoke test。原因：M1 #012 不改变 Web 运行时代码。
- Git HTTP clone/fetch/push。原因：M1 #012 不改变 Git HTTP 运行时代码。
- SSH clone/fetch/push。原因：M1 #012 不改变 SSH 运行时代码。
