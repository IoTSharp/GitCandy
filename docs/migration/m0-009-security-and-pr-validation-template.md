# M0 #009 安全与 PR 验证模板

记录日期：2026-07-09

## 验收结论

- 已记录 ASP.NET Core Identity 迁移期的密码策略、登录 cookie 生命周期、安全戳失效边界和私有仓库匿名访问规则。
- 已新增迁移 PR 验证模板：`.github/PULL_REQUEST_TEMPLATE.md`。
- 本文件作为后续 M1 到 M8 迁移 PR 的安全检查基线。任何改变认证、授权、Git HTTP/SSH、数据库 schema、公开 URL、配置键或文件系统布局的 PR，都必须能对照本文件说明影响、验证和回滚方式。
- 本任务只新增文档和 PR 模板，不改变运行时代码、数据库 schema、认证行为或 Git 协议行为。

## 安全基线范围

本基线覆盖第一轮 ASP.NET Core 10 MVC + EF Core + Identity 迁移必须提前固定的安全口径：

- Web UI 登录使用 ASP.NET Core Identity application cookie。
- Git HTTP 使用独立 Basic/PAT authentication scheme，不复用浏览器登录 cookie 的挑战、跳转或 session 逻辑。
- SSH 使用 public key authentication，并在迁移后接入 Identity 用户和 GitCandy 权限服务。
- 新系统不兼容旧 `_gc_auth` cookie、旧 `AuthorizationLog`、旧密码 hash 或旧密码版本表。
- 私有仓库匿名访问默认拒绝；公开仓库匿名读只在仓库和站点配置都允许时成立。
- 权限判断必须在服务端执行，不能只依赖 Razor view 隐藏按钮。

非目标：

- 不在 M0 #009 实现 Identity、Basic Auth、PAT、SSH key schema 或权限服务。
- 不调整旧 MVC5 运行行为。
- 不提高或降低现有仓库的公开 URL、Git URL、数据库 schema 或部署配置。

## Identity 密码策略

旧 GitCandy 密码规则只要求 6 到 100 字符，不迁移旧密码 hash。ASP.NET Core 迁移启用注册或改密码前，必须显式配置 `IdentityOptions.Password`，避免因框架默认值或模板差异导致行为漂移。

第一阶段迁移基线采用 ASP.NET Core Identity 默认密码强度，并保留旧系统的最小长度下限：

| 选项 | M0 基线值 | 说明 |
| --- | --- | --- |
| `Password.RequiredLength` | `6` | 与旧 GitCandy 最小长度一致。 |
| `Password.RequiredUniqueChars` | `1` | 保持 Identity 默认口径。 |
| `Password.RequireDigit` | `true` | 新账户使用 Identity 标准密码复杂度。 |
| `Password.RequireLowercase` | `true` | 新账户使用 Identity 标准密码复杂度。 |
| `Password.RequireUppercase` | `true` | 新账户使用 Identity 标准密码复杂度。 |
| `Password.RequireNonAlphanumeric` | `true` | 新账户使用 Identity 标准密码复杂度。 |

迁移说明要求：

- 如果 M4 登录/注册垂直切片为了兼容旧体验而放宽复杂度，必须在 PR 中说明安全取舍、测试覆盖和回滚方式。
- 如果 M9 #091 再强化密码策略、引入 MFA、外部登录或 passkey，必须作为独立任务处理。
- 不把明文密码写入 fixture、日志、配置文件、PR 描述或测试快照。
- M0 #001 样例用户密码必须来自本机环境变量、user-secrets 或测试专用 secret provider。

## 锁定和登录确认

第一阶段迁移基线：

| 选项 | M0 基线值 | 说明 |
| --- | --- | --- |
| `Lockout.AllowedForNewUsers` | `true` | 新用户允许失败计数和锁定。 |
| `Lockout.MaxFailedAccessAttempts` | `5` | 连续失败达到阈值后锁定。 |
| `Lockout.DefaultLockoutTimeSpan` | `5 minutes` | 保持 Identity 默认短锁定窗口。 |
| `SignIn.RequireConfirmedAccount` | `false` | 第一阶段没有邮件确认闭环时不启用。 |
| `SignIn.RequireConfirmedEmail` | `false` | 启用前必须先有邮件发送、确认页和恢复流程。 |

迁移要求：

- 登录失败错误不要区分“用户不存在”和“密码错误”，避免用户枚举。
- 锁定状态、失败计数和安全戳失效必须通过 Identity 标准字段和服务处理，不重新发明旧 `AuthorizationLog` 逻辑。
- 若后续启用账号或邮箱确认，必须同步更新注册、登录、密码重置、seed 用户和 smoke tests。

## Cookie 生命周期

Web UI 使用 Identity application cookie。Git HTTP、API/MCP 和 SSH 不得依赖该 cookie 完成协议认证。

第一阶段迁移基线：

| 项目 | M0 基线 |
| --- | --- |
| Cookie 用途 | 仅用于浏览器 Web UI 登录。 |
| Cookie 名称 | 使用明确的新名称，例如 `.GitCandy.Identity`；不得复用 `_gc_auth`。 |
| `HttpOnly` | `true`。 |
| `SecurePolicy` | 生产环境必须要求 HTTPS；开发环境可随请求策略。 |
| `SameSite` | `Lax`，除非外部登录或跨站回调有明确需求。 |
| `ExpireTimeSpan` | `14 days`。 |
| `SlidingExpiration` | `true`。 |
| `LoginPath` | `/Account/Login`，保留本地 `ReturnUrl` 语义。 |
| `LogoutPath` | `/Account/Logout`。 |
| `AccessDeniedPath` | 使用明确的 access denied 页面或保持旧 404/Unauthorized 差异并记录。 |

兼容性要求：

- 旧 `_gc_auth` cookie 在新系统中无效，不做兼容读取。
- 登录、登出、改密码、安全戳失效后必须能清理或刷新 cookie。
- `ReturnUrl` 必须只接受本地 URL；外部 URL 必须回退到安全默认页。
- Git HTTP endpoints 认证失败必须返回 Git 客户端能理解的 401/403/404 行为，不得重定向到登录页。
- Basic/PAT 认证失败时不得写入 Web UI login cookie。

## 安全戳失效边界

ASP.NET Core Identity 的 security stamp 是替代旧 token/version 失效机制的核心。迁移实现必须用 Identity 标准能力处理 Web 会话失效，不保留旧 `AuthorizationLog`。

第一阶段迁移基线：

- `SecurityStampValidatorOptions.ValidationInterval` 不应大于 `30 minutes`。
- 当前用户修改密码后应刷新当前登录或重新登录，并使其他旧 cookie 在下一次 security stamp 校验时失效。
- 管理员重置用户密码、锁定用户、禁用用户、修改安全敏感登录字段时，必须更新 security stamp 或采取等价失效措施。
- 如果 cookie claims 中缓存系统管理员、团队、仓库权限等授权信息，角色或权限变更必须更新 security stamp 或避免把这些权限长期缓存进 cookie。
- SSH key、PAT、deploy key 变更不应依赖浏览器 cookie 的安全戳；对应凭据必须有独立撤销和审计路径。

后续测试要求：

- M4/M5 必须覆盖改密码后旧 cookie 失效。
- M4/M6 必须覆盖 Git Basic/PAT 不受浏览器 cookie 状态影响。
- M7 必须覆盖 SSH key 删除后不能继续通过该 key 访问。

## 私有仓库匿名访问规则

M0 样例权限矩阵以 `public-demo` 和 `private-demo` 为基线：

| Actor | Repository | Web 可见 | Git HTTP read | Git HTTP write | SSH read/write |
| --- | --- | --- | --- | --- | --- |
| anonymous | `public-demo` | 可见 | 站点和仓库允许时可读 | 默认不可写 | SSH 仍需要可认证 key，除非后续明确改动 |
| anonymous | `private-demo` | 不可见 | 不可读 | 不可写 | 不可读写 |
| owner | `private-demo` | 可见 | 可读 | 可写 | 可读写 |
| team member | `private-demo` | 可见 | 可读 | 可写，若团队角色允许 | 可读写，若 key 和角色允许 |
| administrator | `private-demo` | 可见 | 可读 | 可写 | 是否隐式拥有 SSH 权限必须在 M7 明确 |
| authenticated no-role user | `private-demo` | 不可见 | 不可读 | 不可写 | 不可读写 |

迁移要求：

- 私有仓库匿名 Web 页面、Git HTTP clone/fetch/push、SSH clone/fetch/push 和后续 API/MCP 查询都必须拒绝。
- 公开仓库匿名写入默认拒绝；若未来支持匿名写，必须作为独立安全评审任务。
- Git HTTP 的 Basic/PAT scheme 与 Web Identity cookie 分离，避免浏览器 cookie 改变 Git 客户端认证挑战。
- 权限服务应统一服务 Web、Git HTTP、SSH、后台 hook 和后续 Code Intelligence，不得在 controller 或 session handler 中各自散落判断。
- 任何返回 404 隐藏私有仓库存在性的策略，都必须在 Web/Git/API 三处写清楚并有测试。

## 迁移 PR 安全检查

每个迁移 PR 至少检查以下项目。不适用时写明原因，不要删除检查项：

- 是否改变 Identity password、lockout、sign-in、cookie 或 security stamp 行为。
- 是否改变 Git HTTP Basic/PAT、SSH public key、API bearer/PAT 任一认证 scheme。
- 是否改变私有仓库匿名访问、owner/team/admin 权限语义。
- 是否改变公开 URL、Git URL、route pattern、response header 或 Git 客户端错误行为。
- 是否改变数据库 schema、索引、默认值、seed 数据、migration SQL 或配置键。
- 是否新增或修改 repository/cache/archive/delete 路径操作，并完成路径归一化和根目录边界检查。
- 是否调用 `git.exe` 或其他外部程序，并通过结构化参数传参，不经过 shell。
- 是否可能记录密码、token、authorization header、SSH private key、host key 私钥、PAT 或 repository 绝对敏感路径。
- 是否影响 clone/fetch/push streaming，避免把 pack 文件完整读入内存。
- 是否更新 `CHANGES.md`、`ROADMAP.md`、README 或迁移文档。

## PR 模板

仓库已新增 `.github/PULL_REQUEST_TEMPLATE.md`。迁移 PR 内容应至少包含：

```markdown
## 变更点
- 待填写

## 对应 ROADMAP
- Milestone / 垂直切片：

## 安全与兼容性
- Identity / Cookie / Security stamp：
- Git HTTP / SSH / API authentication：
- 私有仓库匿名访问：
- 数据库 schema / migration：
- 公开 URL / Git URL：
- 文件系统路径：
- 日志与敏感信息：

## 测试说明
- 已运行：
- 未运行：

## 是否破坏兼容
- [ ] 是，说明原因、迁移方案、回滚方式
- [ ] 否

## 文档/CHANGES
- [ ] 已更新 ROADMAP/README/CHANGES.md，或说明无需更新
```

## 本任务验证

已运行：

- 静态阅读 `ROADMAP.md`、M0 #000 到 #005 迁移文档、现有 `src/` 和 `tests/` Identity/Data 层文件。
- `rg -n "[ \t]+$" .github\PULL_REQUEST_TEMPLATE.md docs\migration\m0-009-security-and-pr-validation-template.md`：未发现尾随空格。
- `git diff --check`：通过；仅输出当前工作区已有文件的 CRLF 转换提示。

未运行：

- `dotnet build`。原因：本任务只新增文档和 PR 模板，不修改 C# 项目或构建输入。
- `dotnet test`。原因：本任务只新增文档和 PR 模板，不修改测试或运行时代码。
- SQLite 数据读写 smoke test。原因：M0 #009 不改变数据层。
- MVC 登录和主要页面 smoke test。原因：M0 #009 不改变 Web 运行时代码。
- Git HTTP clone/fetch/push。原因：M0 #009 不改变 Git HTTP 运行时代码。
- SSH clone/fetch/push。原因：M0 #009 不改变 SSH 运行时代码。

## 兼容性影响

本任务只新增迁移文档、PR 模板并更新路线图状态，不改变：

- 公开路由和 Git URL。
- 数据库 schema、索引、默认值或 seed 行为。
- Identity cookie、Basic Auth、SSH public key 或权限语义。
- 配置键、环境变量或部署方式。
- Git HTTP/SSH 协议行为和响应 header。
- repository、cache、App_Data、host keys、logs 等文件系统布局。
