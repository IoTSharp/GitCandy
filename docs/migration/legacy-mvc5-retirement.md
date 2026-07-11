# 旧 MVC5 项目退役

## 变更点

- 删除根目录旧 `GitCandy/` ASP.NET MVC5 / .NET Framework 4.5 项目和 `GitCandy.sln`。
- `GitCandy.slnx` 成为仓库唯一 solution；restore、build、test、publish、Docker 和 CI 入口不变。
- 删除 `Directory.Build.props` 中仅服务旧项目的 analyzer 例外，并删除 `.dockerignore` 中旧目录规则。
- 保留 `Sql/` 旧建库脚本和 `docs/migration/` 行为基线；旧源码仍可通过 Git 历史查阅。

## 对应 ROADMAP

- M0-M8 迁移主线完成后的旧项目退休垂直切片。

## 兼容与迁移

- 本变更不修改 ASP.NET Core 运行代码、公开 URL、Git HTTP/SSH 协议、数据库 schema、认证、权限或文件系统布局。
- 旧 MVC5 应用不再能从当前工作树构建。仍需运行旧版本时，应检出删除前的提交，并使用与该版本匹配的数据库、配置和部署环境。
- 旧密码 hash、`_gc_auth` cookie、`AuthorizationLog` 和旧用户数据库仍不兼容；该策略不因源码退役而改变。

## 回滚

- 回退本次退休变更可恢复旧项目文件，但不会改变任何数据库或 repository 数据。
- 不应把旧项目重新加入 `GitCandy.slnx`，也不应让活动 CI 恢复旧 MVC5 构建入口。

## 验证

- `dotnet build GitCandy.slnx --configuration Release`
- `dotnet test GitCandy.slnx --configuration Release --no-build --no-restore`
