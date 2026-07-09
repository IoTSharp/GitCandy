# M2 #020 配置迁移

记录日期：2026-07-09

## 验收结论

- 旧 `Web.config appSettings` 中属于 GitCandy 的配置已迁到 `src/GitCandy/appsettings.json` 的 `GitCandy:Application` 节，并通过 `IOptions<GitCandyApplicationOptions>` 暴露。
- `LogPathFormat` 和 `UserConfiguration` 旧键保留为迁移期别名，便于现有部署用环境变量或临时配置覆盖。
- MVC5 专用的 `webpages:*` 键没有迁入新宿主；ASP.NET Core MVC 不需要这些配置。
- 本任务只迁移配置读取形态，不读取旧用户 XML、不导入 host key 私钥、不改变公开路由、认证语义、数据库 schema 或 Git HTTP/SSH 协议行为。

## 配置键对照

| 旧键 | 新键 | 说明 |
| --- | --- | --- |
| `LogPathFormat` | `GitCandy:Application:LogPathFormat` | 日志文件路径格式，`{0}` 为日期字符串 |
| `UserConfiguration` | `GitCandy:Application:UserConfigurationPath` | 旧 `App_Data/config.xml` 的保留路径，仅用于后续导入或兼容读取 |
| `webpages:Version` | 不迁移 | MVC5 Razor host 专用 |
| `webpages:Enabled` | 不迁移 | MVC5 Razor host 专用 |

`GitCandy:Application` 还补齐了旧 `UserConfiguration` XML 中的非密钥业务默认值，例如 public server、注册开关、仓库创建开关、分页数量、repository/cache 路径、Git core 路径和 SSH 端口/开关。SSH host key 私钥内容不得写入 `appsettings.json`，后续 SSH 切片会单独处理 host key 文件和密钥管理。

## 本任务验证

已运行：

- `dotnet test .\GitCandy.slnx`：通过，覆盖 options 绑定、旧键别名和启动期校验。
- `dotnet build .\GitCandy.slnx`：通过，Debug 构建 0 警告/0 错误。

未运行：

- SQLite 数据读取/写入 smoke test：#020 不改变数据层行为，现有测试随 `dotnet test` 覆盖。
- MVC 登录和主要页面 smoke test：真实登录页面迁移属于后续 M4/M5。
- Git HTTP clone/fetch/push：#020 不改变 Git Smart HTTP 运行时代码，M6 单独验收。
- SSH clone/fetch/push：#020 不改变 SSH 运行时代码，M7 单独验收。

## 兼容性影响

- 配置键发生迁移：新宿主优先读取 `GitCandy:Application:*`，并暂时接受旧根级 `LogPathFormat`、`UserConfiguration` 作为别名。
- 旧 XML 中的 SSH host key 私钥不迁入 JSON 配置，避免把密钥内容提交到仓库或普通部署配置。
- repository、cache、日志路径仍保留为可配置字符串；content root/web root 解析已由 M2 #021 统一，路径边界检查在 M2 #029 继续完成。
