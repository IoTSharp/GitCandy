# M4 #040-#049 认证、授权和会话闭环

## 对应 ROADMAP

- Milestone 4：认证、授权和会话。
- 覆盖 #040 到 #049。
- 本切片不实现 Git Smart HTTP endpoint；`GitCandy.GitBasic` 绑定到 clone/fetch/push 路由属于 M6。

## 实现决策

### Web 账户

- Web UI 使用 ASP.NET Core Identity application cookie，cookie 名为 `.GitCandy.Identity`。
- 账户 UI 采用 MVC `AccountController` + Razor Views，不引入 Identity Razor Pages UI。
- 保留 `/Account/Login`、`/Account/Create`、`/Account/Change` 和 `/Account/Logout` 路径。
- 登录支持 Identity 用户名或邮箱；注册、登录、改密和登出 POST 都经过 antiforgery 校验。
- 登出只允许 POST。旧版 GET logout 不再执行登出，避免跨站请求触发会话退出。

### 旧认证不兼容

- 不读取或迁移旧 `_gc_auth` cookie。
- 不兼容旧密码 hash、`PasswordVersion` 或 `AuthorizationLog` token。
- 升级后账户必须在新 Identity schema 中重新创建，或由后续独立导入工具导入不含密码的用户资料。
- 新实现不会在启动时改写旧认证表，也没有新增 EF Core migration。

### 当前用户与授权

- scoped `ICurrentUser` 从 `HttpContext.User` claims 读取 user id、用户名、管理员角色和请求取消令牌，替代新代码中的 `Token.Current`。
- repository read/write/owner、team administrator、current user 和 system administrator 使用命名 policy 与 resource-based authorization handler。
- repository handler 复用 `IRepositoryService` / `IGitCandyRepositoryPermissionQuery`；team handler 复用 `IMembershipService`。
- `IsPrivate=true` 的仓库始终拒绝匿名访问，即使数据中误设了 anonymous read/write 标志。

### Git Basic Auth

- Git HTTP 使用独立 scheme `GitCandy.GitBasic`，不把 Identity application cookie 当作 Git 凭据。
- handler 解析 Basic header 后使用 Identity 用户名或邮箱查找用户，并通过 `SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true)` 校验。
- 成功主体由 Identity claims factory 创建，保留 user id 和 role claims。
- challenge 固定返回 `401` 和 `WWW-Authenticate: Basic realm="GitCandy", charset="UTF-8"`。
- handler 不读取或写入 Session，也不记录 authorization header、用户名对应密码或明文凭据。

### Session 与 cookie

- 当前 ASP.NET Core 切片没有必要的 Session 用途，因此移除 `AddSession` 和 `UseSession`，不产生 `.GitCandy.Session` cookie。
- Identity cookie 使用 `HttpOnly=true`、`SecurePolicy=Always`、`SameSite=Lax`、8 小时期限和 sliding expiration。
- `SecurePolicy=Always` 要求部署入口使用 HTTPS；反向代理部署必须在认证前正确处理 forwarded headers。
- 本地交互验证使用 `dotnet run --project src/GitCandy --launch-profile https`；仅使用 `http` profile 时浏览器不会回传 secure Identity cookie。
- 单实例默认数据保护 key ring 可用于当前开发和 smoke test。多实例或无状态部署必须在 M8 发布配置中持久化并共享 data-protection keys。

## 行为验证

- Kestrel + SQLite 账户集成测试覆盖注册、cookie 登录、POST 登出、改密、旧密码拒绝和 antiforgery。
- 改密后刷新当前会话；另一浏览器会话持有的旧 cookie 在安全戳校验时失效。
- 连续失败登录覆盖 Identity 失败计数、锁定和锁定期间正确密码仍被拒绝。
- Git Basic 测试覆盖用户名/邮箱认证、失败计数重置、锁定和独立 Basic challenge。
- 授权测试覆盖公有仓库匿名读/写配置、私有仓库匿名拒绝、owner、team member、team administrator、system administrator 和无权限用户。
- `ICurrentUser` 测试覆盖已认证管理员 claims 和无 `HttpContext` 的匿名默认值。

## 兼容性、迁移和回滚

- 认证兼容性是有意破坏：旧 cookie、旧密码 hash 和旧授权 token 不能继续使用。
- `/Account/Logout` 的 HTTP method 从旧版 GET 收紧为 POST；调用方应提交带 antiforgery token 的表单。
- 数据库 schema 没有变化，不需要新增 migration 或数据回填。
- 回滚方式是停止新应用并恢复上一应用版本；本切片没有删除或修改旧认证表。若已在新 Identity schema 创建账户，旧应用不会使用这些账户。

## 测试命令

```powershell
dotnet restore GitCandy.slnx
dotnet build GitCandy.slnx --no-restore
dotnet test GitCandy.slnx --no-build --no-restore
```

- Git HTTP clone/fetch/push：未运行，Smart HTTP endpoint 属于 M6。
- SSH clone/fetch/push：未运行，本切片不涉及 SSH transport。
