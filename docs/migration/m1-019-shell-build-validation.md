# M1 #019 空壳构建验证

记录日期：2026-07-09

## 验收结论

- `dotnet build .\GitCandy.slnx` 已能构建 ASP.NET Core 10 MVC 迁移空壳。
- 验证入口固定为迁移主线 `GitCandy.slnx`，旧 `GitCandy.sln` 继续只作为 MVC5 行为参考。
- 本任务不迁移业务代码，不改变数据库 schema、认证语义、公开路由或 Git HTTP/SSH 协议行为。

## 本地命令

```powershell
dotnet build .\GitCandy.slnx
```

## 本任务验证

已运行：

- `dotnet build .\GitCandy.slnx`：通过，Debug 构建 0 警告/0 错误。
- `.\tools\migration\m1-012\Invoke-M1SolutionValidation.ps1`：通过，Debug build 0 警告/0 错误，`GitCandy.Tests` 5 个测试通过，`GitCandy.Data.Tests` 25 个测试通过。
- `.\tools\migration\m1-012\Invoke-M1SolutionValidation.ps1 -Configuration Release`：通过，Release build 0 警告/0 错误，`GitCandy.Tests` 5 个测试通过，`GitCandy.Data.Tests` 25 个测试通过。

未运行：

- SQLite 数据读取/写入 smoke test：M1 不涉及数据层行为变更，后续已由 M3 覆盖。
- MVC 登录和主要页面 smoke test：M1 当前只有占位路由，真实登录页面迁移属于后续 M4/M5。
- Git HTTP clone/fetch/push：#019 不改变 Git Smart HTTP 运行时代码，M6 单独验收。
- SSH clone/fetch/push：#019 不改变 SSH 运行时代码，M7 单独验收。

## 兼容性影响

本任务只记录空壳构建验证，不改变：

- 公开路由和 Git URL。
- 数据库 schema、索引、默认值或 seed 行为。
- Identity cookie、Basic Auth、SSH public key 或权限语义。
- 配置键、环境变量或部署方式。
- Git HTTP/SSH 协议行为和响应 header。
- repository、cache、App_Data、host keys、logs 等文件系统布局。
