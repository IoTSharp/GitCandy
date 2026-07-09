# M1 #018 System.Web 入口检查

记录日期：2026-07-09

## 验收结论

- 新增 `SystemWebEntryCheckTests`，在 `dotnet test GitCandy.slnx` 中持续检查迁移主线项目。
- 检查范围限定为 `GitCandy.slnx` 中的 `src/` 新项目，以及根构建入口文件；旧 MVC5 `GitCandy/` 目录继续作为行为参考，不纳入本门禁。
- 新项目不得引用或使用 `System.Web`、`System.Web.Mvc`、`System.Web.Optimization`、`System.Data.Entity`。

## 门禁范围

测试覆盖两类入口：

- 项目引用入口：扫描新 `src/` 项目的 `FrameworkReference`、`PackageReference`、`Reference`。
- 源码入口：扫描 `Directory.Build.props`、`Directory.Packages.props`、`GitCandy.slnx` 和 `src/` 下的 `.cs`、`.cshtml`、`.csproj`、`.props`、`.razor`、`.targets` 文件。

## 本地命令

```powershell
dotnet test .\tests\GitCandy.Tests\GitCandy.Tests.csproj --configuration Release --filter SystemWebEntryCheckTests
```

完整迁移验证仍使用：

```powershell
.\tools\migration\m1-012\Invoke-M1SolutionValidation.ps1
```

## 兼容性影响

本任务只增加迁移门禁和文档，不改变：

- 公开路由和 Git URL。
- 数据库 schema、索引、默认值或 seed 行为。
- Identity cookie、Basic Auth、SSH public key 或权限语义。
- 配置键、环境变量或部署方式。
- Git HTTP/SSH 协议行为和响应 header。
- repository、cache、App_Data、host keys、logs 等文件系统布局。

## 本任务验证

已运行：

- `dotnet test .\tests\GitCandy.Tests\GitCandy.Tests.csproj --configuration Release --filter SystemWebEntryCheckTests`：通过，2 个门禁测试通过。
- `.\tools\migration\m1-012\Invoke-M1SolutionValidation.ps1 -Configuration Release`：通过，Release build 0 警告/0 错误，`GitCandy.Tests` 5 个测试通过，`GitCandy.Data.Tests` 25 个测试通过。

未运行：

- Debug 配置完整验证。原因：本机已有 `GitCandy` Debug 进程正在运行并锁定 `src/GitCandy/bin/Debug/net10.0` 下的依赖 DLL；本任务已用 Release 配置完成等价构建与测试门禁验证。
- MVC 登录和主要页面 smoke test。原因：M1 #018 只增加引用/源码入口门禁，不改变 Web 运行时代码。
- Git HTTP clone/fetch/push。原因：M1 #018 不改变 Git HTTP 运行时代码。
- SSH clone/fetch/push。原因：M1 #018 不改变 SSH 运行时代码。
