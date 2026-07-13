# GitCandy 升级到 ASP.NET Core 10 MVC + EF Core 路线图

评估日期：2026-07-12
当前本机 SDK：.NET SDK 10.0.301，ASP.NET Core Runtime 10.0.9
目标方向：以 ASP.NET Core 10 MVC 为主线，数据库层迁移到 EF Core，用户认证采用 ASP.NET Core Identity 标准体系，保留 GitCandy 作为 Git 代码托管服务的核心行为，并默认采用单 GitCandy 进程承载 Web、Git HTTP、内置 SSH 和后台任务。

状态标记：

- ✅ 已完成：编号范围内的验收项已经通过，并有测试或文档记录。
- 🚧 进行中：已经开始实现，但验收尚未闭环。
- ⬜ 未完成：尚未开始，或没有可验证的完成记录。

编号规则：路线图编号按优先级和实施顺序排列；编号越小，越应优先推进。同一 Milestone 内按 `#` 任务编号顺序推进。M12.5 使用 `#139A-#139I`，M12.6 使用 `#139J-#139L`，M12.7 使用 `#139M-#139W` 表示插入 M12 与 M13 之间的收口任务；M15.5 使用 `#169A-#169H`，M15.6 使用 `#169I-#169R` 插入 M15 与 M16 之间，避免重编号已经被文档引用的任务。当前没有验收记录的编号默认标为 ⬜。

校准说明：

- 第一阶段迁移主线（M0-M8）和 M9 迁移后改进池已经完成：ASP.NET Core 10 host、EF Core + Identity、MVC、Git Smart HTTP/SSH、仓库代码工作区、Git LFS、部署与运维均有可验证闭环；后续路线转为“代码托管产品协作能力”。
- M10-M12 已完成稳定命名空间、Issue、Pull Request、代码评审和 merge/squash 主链；当前不再扩张迁移范围，先完成 SonnetDB 生产验收和个人工作台独立切片，再进入 M13 合并治理与外部集成。
- 活动主线已具备 `GitCandyDbContext`、SQLite/SQL Server/SonnetDB migration、ASP.NET Core Identity、MVC、Git HTTP/SSH、部署和迁移保护网；SQLite 仍为默认 provider，SonnetDB 仅由 `gitcandy.com` 专用配置显式启用，PostgreSQL 仍是后续可选 provider 工作。
- `GitCandy.slnx` 是唯一活动 solution；旧 `GitCandy.sln` 和 MVC5 源码已在迁移完成后退役，历史实现通过 Git 历史和 `docs/migration` 行为基线查阅。
- 当前数据库策略仍为 SQLite-first：通用业务实现和默认部署继续以 SQLite 为基线；M3 已补齐 SQL Server 独立 migration 与 SQL 生成审阅，M12.6 已补齐 SonnetDB 独立 migration、兼容性回归和 `gitcandy.com` 专用部署路径。SQL Server 真实部署验证与 PostgreSQL provider 仍需后续独立回补。

## 当前产品状态与下一步

截至 2026-07-12，GitCandy 已完成从 MVC5 到 ASP.NET Core 10 的迁移，并形成可用的轻量代码托管与基础协作产品。详细实现和验收证据保留在各 Milestone；本节只维护当前决策摘要。

### 已完成基线

| 能力 | 当前状态 |
| --- | --- |
| 平台与数据 | ASP.NET Core 10、EF Core、Identity、SQLite 默认运行路径、SQL Server migration SQL、SonnetDB 专用生产路径、Nullable/Analyzer 门禁 |
| Git 服务 | Smart HTTP、内置 SSH、clone/fetch/push、Git protocol v2、大 pack 流式传输、统一 transport backend |
| 仓库能力 | 稳定 namespace/alias、创建/导入/fork/删除、tree/blob/raw/history/diff/blame/compare/archive、Git LFS basic transfer |
| 账号与权限 | Identity cookie、独立 Git Basic、2FA、恢复码、可选 OIDC、用户 SSH key、仓库/团队/管理员权限 |
| 研发协作 | Issues、评论、Markdown、labels、milestones、assignees、通知、PR、跨 fork、行内 review、approval、merge/squash |
| 运维交付 | Docker Compose、Linux systemd、Windows Service、健康检查、OpenTelemetry、自动 migration 与发布产物 |

### 当前缺口分层

| 优先级 | 必须继续完成 |
| --- | --- |
| P0 稳定性 | Git HTTP 规范 `.git` 地址真实客户端稳定性、旧地址 404、Issue 并发限流原子性、Linux/Windows 跨平台测试、覆盖率与浏览器回归门禁 |
| P0 基本功能 | Branches/Tags 独立页面与受控管理、Contributors 统计、自助密码恢复/邮件确认、TLS reverse proxy 快速部署、个人工作台与公开个人页、公开仓库发现与推荐、PAT/API auth、deploy key、保护分支、协作审计 |
| P1 研发闭环 | webhook、commit status/check、required checks、CODEOWNERS、PR/review/check 通知、Releases、assets、协作搜索 |
| P2 扩展能力 | 企业身份、remote mirror、帮助中心与文档发布、SonnetDB-backed OCI Container Registry、Wiki、其他 Packages、内置 runner、LFS locking、SonnetDB 文档知识库、全业务 API MCP、代码智能与 Agent Memory |

实施原则：先让现有 Git/Issue/PR 能力在并发、跨平台和生产部署下可靠，再增加治理与自动化；内置 CI runner、Packages 和 AI 能力不得早于 M13 的凭据、保护分支、check 和审计边界。

## 迁移起点画像（历史基线）

以下内容是 M0 冻结的迁移起点，用于解释后续设计约束；它不再代表活动主线现状。当前唯一活动 solution 是 `GitCandy.slnx`，目标 `net10.0`，M0-M8 已完成，MVC5 项目已退役并可通过 Git 历史查阅。

迁移起点是一个单项目 ASP.NET MVC 5 / .NET Framework 4.5 Web 应用：

- 解决方案：`GitCandy.sln`
- Web 项目：`GitCandy/GitCandy.csproj`，老式 non-SDK-style csproj，`TargetFrameworkVersion` 为 `v4.5`
- 包管理：`packages.config`
- Web 栈：`System.Web.Mvc`、`System.Web.Razor`、`System.Web.Optimization`、`Web.config`、`Global.asax`
- 数据库：EF6.1 + SQLite EF6 Provider，另有 SQL Server 创建脚本
- Git 能力：活动 net10.0 主线使用 `LibGit2Sharp 0.31.0` 提供托管仓库能力，并受控调用 Git 官方 Smart HTTP/SSH transport helper；冻结的 MVC5 历史基线使用 0.22.0
- 后台能力：自写 Scheduler、自写 SSH Server，随 `Application_Start` 启动，随 `Application_End` 停止
- UI：Bootstrap 3、jQuery 2、Razor MVC views，约 46 个 `.cshtml`
- 代码规模：约 192 个 C# 文件，3 个 resx 资源文件，284 个 git-tracked 文件

这不是“原地改 TargetFramework”的升级。由于 ASP.NET Core 不包含 `System.Web`，迁移更接近一次有保护网的现代化重建：先建立新 ASP.NET Core MVC 外壳，再逐步迁移数据、认证、路由、视图、Git HTTP、SSH 和后台任务。

## 首轮迁移差距（已由 M1-M9 处理）

### Web 启动和请求管线

当前入口在 `Global.asax.cs`：

- 注册 MVC routes、filters、bundles
- 注册 MEF dependency resolver
- 启动 scheduler、Git cache、SSH server
- 处理全局错误、自定义错误页
- 在 `Application_AcquireRequestState` 设置 culture
- 在 `Application_BeginRequest` 启动 profiler

ASP.NET Core 10 的标准形态应迁移到 `Program.cs`：

- `builder.Services.AddControllersWithViews()`
- `builder.Services.AddRazorPages()`，仅在使用 Identity 默认 UI / Razor Pages 区域时需要
- `builder.Services.AddDbContext<GitCandyDbContext>(...)`
- `builder.Services.AddIdentity<GitCandyUser, IdentityRole>()` 或 `AddDefaultIdentity<GitCandyUser>()`
- `builder.Services.AddAuthorization(...)`
- `builder.Services.AddSession()` 或明确移除 Session 依赖
- `builder.Services.AddLocalization()` / `UseRequestLocalization(...)`
- `app.UseExceptionHandler(...)`
- `app.UseStaticFiles()`
- `app.UseRouting()`
- `app.UseAuthentication()`
- `app.UseAuthorization()`
- `app.UseSession()`，若仍保留 Session
- `app.MapControllerRoute(...)`
- `app.MapRazorPages()`，仅在使用 Identity 默认 UI / Razor Pages 区域时需要

### `System.Web` 依赖

仓库中大量代码依赖 `System.Web`：

- `HttpContext.Current`
- `HttpRuntime.Cache`
- `HttpRuntime.UnloadAppDomain`
- `Server.MapPath`
- `VirtualPathUtility`
- `HttpException`
- `Request.UserHostAddress`
- `Request.GetBufferlessInputStream`
- `Response.OutputStream`
- `Response.AddHeader`
- `Session[...]`
- `System.Web.Mvc` filters/helpers/result 类型
- `System.Web.Optimization` bundling

这些都必须迁移到 ASP.NET Core 对应抽象：

- `HttpContext`
- `IHttpContextAccessor`
- `IMemoryCache` 或 `IDistributedCache`
- `IWebHostEnvironment`
- `PathString` / `LinkGenerator` / `IUrlHelper`
- `StatusCodeResult` / `ProblemDetails` / exception middleware
- `HttpContext.Connection.RemoteIpAddress`
- `Request.Body`
- `Response.Body`
- `Response.Headers`
- `ISession.GetString/SetString`
- `Microsoft.AspNetCore.Mvc` filters/helpers/result 类型
- WebOptimizer、Vite/esbuild/npm，或先保持静态文件无打包

### 数据访问

当前数据层是 EF6：

- `GitCandyContext : System.Data.Entity.DbContext`
- 映射使用 `EntityTypeConfiguration<T>`
- 连接串在 `Web.config`
- SQLite 默认连接串：`Data Source=|DataDirectory|GitCandy.db;BinaryGUID=True;`
- schema 由 `Sql/Create.Sqlite.sql` 和 `Sql/Create.MsSql.sql` 创建
- `Database.SetInitializer<GitCandyContext>(null)` 表示当前没有 EF 自动建库策略

迁到 EF Core 时按“新标准 schema + 旧行为参考”的方式处理：

- 用户认证不兼容旧 `Users` 表，不迁旧 `_gc_auth` cookie、`AuthorizationLog` 或 `PasswordVersion` 体系
- 认证表使用 ASP.NET Core Identity 标准表结构，必要时通过 `GitCandyUser : IdentityUser` 扩展显示名等字段
- 旧 `Users`、`AuthorizationLog`、`SshKeys` 和密码代码只作为业务语义参考，不作为 schema 兼容目标
- 仓库、团队、仓库权限等 GitCandy 领域模型可借鉴旧表设计，但应重新建模为引用 Identity user id
- `Repositories.Name`、`Teams.Name` 等领域唯一性、大小写不敏感语义仍要明确配置
- SQLite 与 SQL Server Provider 差异要明确配置
- 初始 migration 面向新系统 schema，不要求接管旧用户表；若迁移旧仓库元数据，需要单独写导入工具

注意：SQL 脚本中 `Users_IX_User_Email` 实际建在 `Name`，`Users_IX_User_Name` 实际建在 `Email`。这只作为历史参考，不再作为新用户 schema 的兼容要求。

### 认证与授权

当前认证是自定义 cookie + `AuthorizationLog`：

- Cookie key：`_gc_auth`
- Token 存在数据库和 `HttpRuntime.Cache`
- `Token.Current` 是静态线程/请求关联入口
- Git Smart HTTP 另用 Basic Auth，并在 Session 中缓存用户名
- 多个自定义 `AuthorizeAttribute` 派生过滤器控制管理员、仓库读写、团队管理员等权限

建议目标：

- Web 登录使用 ASP.NET Core Identity + EF Core + Identity cookie
- 使用 `UserManager<GitCandyUser>`、`SignInManager<GitCandyUser>`、`RoleManager<IdentityRole>` 或 policy/claims 完成用户与权限管理
- 使用 Identity 标准密码哈希、安全戳、锁定、密码重置等能力；不兼容旧密码 hash
- Git Smart HTTP 使用单独 Basic Authentication scheme 或 endpoint-level 自定义认证处理器，并复用 Identity 的用户校验能力
- 权限判断沉到 authorization handlers/services，过滤器只做薄包装
- 移除 `Token.Current`，改为 `User` claims + scoped `ICurrentUser`
- 页面主体保持 MVC + Razor Views；Identity 默认 UI 可作为 Razor Pages 混入，若要保持 GitCandy UI 一致则自建 MVC Account views 调用 Identity 服务

### Git HTTP 和 SSH

GitCandy 的核心价值不只是 MVC 页面，Git 协议能力必须单独保护：

- 路由：`git/{project}.git/{*verb}`、`git/{project}/{*verb}`
- Smart HTTP：`info/refs`、`git-upload-pack`、`git-receive-pack`
- 请求/响应必须流式处理，不能把 pack 文件完整读进内存
- 旧配置中允许特殊 path、double escaping、较大请求体和长超时；新 Kestrel/IIS 配置要对应设置
- 默认目标是一个 GitCandy 进程承载 Web UI、HTTP API、Git Smart HTTP、内置 SSH、scheduler 和后台索引入口
- SSH Server 当前在 Web 进程中作为 TCP listener 启动，应改为 `IHostedService` / `BackgroundService`，继续保持内置 SSH 为默认路线
- 外部 OpenSSH forced command 只作为可选部署适配，不作为第一阶段默认架构

Git HTTP/SSH 必须有真实 `git clone/fetch/push` 验证，不能只靠 MVC 页面测试。

#### Git/SSH 后端设计目标

GitCandy 的运维体验应尽量保持“一个应用进程搞定一切”：启动 GitCandy 后即可获得 Web、Git HTTP、SSH、后台任务和后续 Code Intelligence 能力。迁移期不应把系统拆成 OpenSSH、shell 脚本、独立 Git daemon、独立 worker 等一堆必须手工编排的组件。

设计约束：

- 内置 SSH 是默认能力，运行在 ASP.NET Core host 内的 `BackgroundService` 中，直接接入 DI、Identity、权限服务、审计、配置和日志。
- SSH 只暴露 Git 必需命令：`git-upload-pack`、`git-receive-pack`、`git-upload-archive`。默认不提供交互 shell、SFTP、端口转发和密码 SSH 登录。
- Git HTTP 与 Git SSH 共用同一套仓库路径解析、权限判断、审计、hook、限流和后台索引触发逻辑。
- 禁止业务代码散落 `Process.Start` 或拼 shell command。所有 Git 协议后端执行都必须收敛到 `IGitTransportBackend`，并且只能通过结构化参数调用。
- 第一阶段可以把 `git.exe` 作为受控协议 helper 使用，仅用于 `upload-pack`、`receive-pack`、`upload-archive` 这类 Git 官方后端命令；这不是脚本拼装路线，而是为了先保护 Git wire protocol 正确性、性能和兼容性。
- `LibGit2Sharp` 是仓库能力库，不是完整 Git server transport。它优先用于仓库浏览、commit/tree/blob/ref/branch/tag 操作、diff、blame、历史遍历、后台索引和代码图谱。
- 不得假设 `LibGit2Sharp` 可以完整替代 Git Smart HTTP/SSH 服务端的 `upload-pack`、`receive-pack`、pack negotiation、sideband、shallow/partial clone、Git protocol v2、atomic push、hook 和大 pack 流式行为。
- Git LFS 是独立 HTTP API，不属于 `LibGit2Sharp` 的替代范围；实现时必须单独设计 batch transfer、对象存储、权限过滤、审计和可选 locking。
- 中长期评估减少外部进程依赖：仓库元数据、refs、diff、blame、archive、搜索索引等优先通过 LibGit2Sharp 或托管实现；是否自研 pack protocol server 必须等 clone/fetch/push、LFS、大 pack、协议 v2、权限和压力测试保护网完整后再独立决策。
- 对部署者暴露的是单进程能力边界：即使内部短暂启动 Git helper，也不能要求部署者安装和管理额外守护进程、shell 脚本或 OpenSSH 配置。

## 推荐目标结构

第一阶段建议保持迁移边界清晰，不急着做过度拆分：

```text
GitCandy/
├── GitCandy.slnx
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── src/
│   ├── GitCandy/              # 唯一 ASP.NET Core 10 MVC 主程序和 host
│   ├── GitCandy.Core/         # 领域模型、权限、配置抽象、通用服务
│   ├── GitCandy.Data/         # EF Core DbContext、migrations、repositories
│   ├── GitCandy.Git/          # LibGit2Sharp、git.exe transport、cache
│   ├── GitCandy.Ssh/          # 内置 SSH server，默认随 GitCandy host 运行
│   └── GitCandy.CodeIntelligence/ # 迁移稳定后的代码索引、符号图谱和 AI memory
├── tests/
│   ├── GitCandy.Tests/
│   └── GitCandy.IntegrationTests/
└── Sql/
```

如果希望降低第一轮改造成本，也可以先只创建 `src/GitCandy` 一个 SDK-style 主程序项目，把 Web UI、Git HTTP、内置 SSH、scheduler 和后台入口都收敛到这个 host；待测试跑通后再拆 `Core/Data/Git/Ssh`。不要同时重写架构、UI 和协议层。

解决方案规则：

- `GitCandy.slnx` 是 ASP.NET Core 迁移主线的活动 solution，所有新的 `dotnet build`、`dotnet test`、CI 和迁移验证命令都应显式指定它。
- 迁移完成后已删除旧 `GitCandy.sln` 和 MVC5 项目，避免工具自动选择错误 solution；历史实现从 Git 历史读取，行为基线继续保留在 `docs/migration`。

## 产品能力路线图补充

GitCandy 的产品目标不是完整复制 GitLab，而是在轻量自托管前提下满足程序员日常 Git 协作，并在迁移稳定后形成自己的 AI 代码库知识图谱能力。

### P0：必须可靠的 Git Server 内核

- Git HTTP/SSH clone、fetch、push，优先验证 Git protocol v2、认证失败、权限不足、仓库不存在和 service 不支持行为。
- 稳定仓库命名空间：Web 规范 URL 为 `/{namespace}/{repository}`，Git HTTP/LFS 规范 URL 为 `/{namespace}/{repository}.git`，namespace 可属于用户或团队；Web、Git HTTP、LFS 和 SSH 复用同一 resolver，不提供 `/git/{project}` 或无 `.git` remote 兼容入口。
- 改名占用：用户/团队/仓库使用稳定内部 ID，历史名称默认保留 365 天且可配置；保留期内旧名称不得被再次占用，但旧 Web/Git/SSH 地址不提供访问或重定向。
- Git LFS：至少实现 HTTP batch transfer、对象上传/下载、对象存在性检查和权限过滤；locking 可作为 P1。
- 仓库管理：创建、导入、重命名、删除、归档、默认分支、fork、mirror、public/private、owner/team/admin 权限。
- 代码浏览：tree、blob、raw、commit、diff、blame、compare、archive、独立 Branches/Tags 页面与受控管理、Contributors 统计均已完成。
- 账号与访问凭据：Identity 用户、团队/组织、PAT、SSH key、deploy key、审计日志。
- 仓库路径安全：所有 repository/cache/archive/delete 路径都必须归一化并验证位于预期根目录。
- Hook pipeline：pre-receive、update、post-receive，至少支持保护分支、审计、webhook 和 Code Intelligence 索引入队。

### P1：日常协作能力

- 代码工作区：tree、blob、raw、commit、diff、blame、compare、固定 commit permalink、行号与代码片段链接；这是 Issue 引用代码和 PR 行内评审的前置能力。
- Issues：评论与 timeline、Markdown fenced code block、labels、milestones、assignees、mentions、references、templates、subscriptions 和 notifications。
- Pull Request / Merge Request：draft、commits、files changed、review threads、inline comments、approval/request changes、merge commit、squash、冲突提示；同仓库分支先行，跨 fork 在稳定 namespace/fork 生命周期完成后接入。
- Branch protection：禁止强推、禁止删除、required checks、required approvals、CODEOWNERS。
- Wiki、releases、release assets。
- Webhook、commit status、checks API，先接外部 CI，再考虑内置 runner。
- 远程仓库连接：绑定 GitHub、GitLab、Gitee 用户或组织账号，支持 remote discovery、一次性导入、单向 pull/push mirror、手动同步、计划任务、webhook 加速、ref filter、失败诊断和审计；双向 mirror 默认禁用。
- 搜索：仓库、用户、团队、issue、PR、commit、代码文本搜索。

### P2：企业与安全能力

- 审计日志：登录、凭据使用、clone/fetch/push、权限变更、分支保护变更、强推、删除分支。
- Secret scanning 和 push protection，至少先从 hook pipeline 做可扩展入口。
- Signed commits/tags 展示与策略校验。
- 备份、恢复、迁移、健康检查和配置诊断。
- 组织级策略：默认可见性、成员权限、仓库创建权限、PAT 过期策略。
- 团队治理：`TeamOwner`（最高管理员）、`Leader`、`DeputyLeader`、`Member` 四级角色，至少保留一个本地 break-glass owner，团队角色与具体仓库权限分离。
- 企业身份：Microsoft Entra ID 优先采用 OIDC/SAML + SCIM；企业微信、飞书、钉钉通过独立 OAuth/通讯录 adapter 接入，覆盖 tenant 绑定、用户/部门同步、角色映射、停用、恢复、同步作业和管理员诊断页面。
- OCI Container Registry 由 M15.6 作为独立产品阶段承接，使用 GitCandy `/v2/` 协议层和 SonnetDB 私有 bucket；NuGet/npm/Maven 等其他 Packages/Artifacts 后续再分别规划，不阻塞 Git 核心能力。

### P3：AI 代码库知识图谱

- 以 M16 为主线，先做只读 ingest、搜索、符号、调用关系、影响分析和 MCP tools。
- M15.5 重写后的规范文档在部署后自动摄入 SonnetDB，以 BM25 + 向量 Hybrid Search 提供带版本引用的帮助知识库。
- 所有业务 API 建立 MCP coverage matrix；只读能力先行，写入与危险操作必须经过 scope、幂等、确认、审计和可配置禁用。
- 第一版默认不让 Agent 修改仓库；写入能力从审计良好的 agent memory 和 PR 辅助开始。
- Code Intelligence 不进入 Git HTTP/SSH 热路径；push 后只触发后台索引入队。
- 私有仓库、未发布分支、代码片段、Agent memory 查询必须复用 GitCandy 权限语义。

## 前端和中间件路线

迁移期前端策略：

- 第一轮迁移保持 MVC + Razor Views + Bootstrap 3 行为，不同时做 UI redesign。
- 旧 bundling 先替换为直接静态引用；Vite/esbuild/Tailwind 等新链路放到迁移稳定后单独做。
- 安全关键判断不依赖视图隐藏按钮，服务端权限必须再次校验。

迁移稳定后前端策略：

- 普通 Git 管理页面优先 Razor + 少量 TypeScript/htmx/Alpine，保持轻量、快和易部署。
- 主导航为登录用户增加第一项“我的”，使用清晰的二级导航承载个人仓库、Packages、Stars、设置和团队；其后增加“发现”，面向所有访问者展示公开仓库推荐；个人工作台和发现页先作为独立垂直切片落地，不与全站 UI redesign 或新前端构建链绑定。
- 主导航增加“帮助”入口，指向随应用发布的 `/help` 静态文档站点；帮助站点由版本化 Markdown 源生成，不在 Razor view 中维护第二份说明文案。
- Code Intelligence Explorer 可以使用 React + TypeScript + Vite，避免把复杂交互强塞进传统 Razor。
- 代码查看和 diff 体验可评估 Monaco Editor、Shiki/highlight.js、diff2html 或等价组件。
- 图谱视图可评估 React Flow、Cytoscape.js 或等价组件。
- 表格和数据请求可评估 TanStack Table / Query；样式体系可评估 Tailwind + Radix/shadcn，但必须作为独立 UI 现代化任务。

ASP.NET Core 中间件和后台能力：

- Web UI 使用 Identity Cookie。
- Git HTTP 使用独立 Basic/PAT authentication scheme，不能依赖浏览器 cookie 或 Session。
- API/MCP 使用 Bearer/PAT，并按 repository/team/branch 做权限过滤。
- SSH 使用 public key auth，并直接接入 Identity 用户、SSH key、权限服务和审计。
- Git endpoints 禁止响应 buffering，不能全局压缩 pack 响应，不能把 pack 完整读入内存。
- 对登录、API、Git HTTP、SSH 分别设置限流、请求体限制、超时、日志脱敏和审计。
- Scheduler 采用 Quartz.NET 并作为 ASP.NET Core hosted service 随 GitCandy host 启停；第一阶段使用内存调度，避免把 Quartz 持久化 schema 混入 EF/Identity 迁移，后续若需要持久化、集群或管理 UI 再作为独立任务评估。
- 后台任务使用 Quartz.NET job、`CancellationToken`、`IDbContextFactory` 和清晰的 graceful shutdown。

## 竞品参考学习对象

- Gitea / Forgejo：轻量自托管、内置 SSH、简单部署、仓库/issue/PR/wiki/actions/packages 的边界。
- GitLab：Gitaly、GitLab Shell、Workhorse 的协议和存储分层，Merge Request、CI/CD、approval、CODEOWNERS、审计与企业策略。
- GitHub：Pull Request UX、checks/status、CODEOWNERS、branch rules、code scanning、secret scanning、Copilot agent 场景。
- Gitee：国内企业 DevOps、组织/项目协同、代码扫描、流水线、测试、制品和效能度量。
- Sourcegraph / OpenGrok：大规模代码搜索、符号导航、跨仓库代码理解、代码图谱和 Agent context。

参考竞品时只吸收产品能力和架构边界，不照搬其部署复杂度。GitCandy 的默认体验仍应是一个 ASP.NET Core 10 应用进程即可启动核心服务。

代码浏览、Issue、PR/Review、合并治理的竞品矩阵和领域边界见 `docs/product/collaboration-roadmap.md`。仓库路径重定向、名称保留、团队角色、Microsoft Entra ID/企业微信/飞书/钉钉身份连接，以及 GitHub/GitLab/Gitee pull/push mirror 的详细决策见 `docs/product/enterprise-repository-roadmap.md`。

## Milestone 路线图

### ✅ Milestone 0：基线冻结与迁移保护网

目标：先知道“什么算没迁坏”。

定位：

- 迁移型任务的第一优先级，不写业务迁移代码也要先完成。
- 产出必须能支撑后续 PR 说明是否影响 Git 协议、数据库 schema、公开路由和权限语义。

#### ✅ M0 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #000 | 迁移分支与旧项目冻结 | 已建立 `migration/aspnet-core-10`，在迁移期间冻结 MVC5 项目，并在 `docs/migration/m0-000-baseline.md` 记录工作区基线；迁移完成后旧项目已退役 |
| ✅ #001 | 测试数据与样例仓库 | 已新增 `tools/migration/m0-001/New-M0SampleData.ps1` 和 `docs/migration/m0-001-test-data-and-sample-repositories.md`，可重复生成新库样例数据规格、管理员、普通用户、团队、公有仓库、私有仓库和 bare git repository |
| ✅ #002 | Web 行为清单 | 已新增 `docs/migration/m0-002-web-behavior-checklist.md`，记录登录、登出、注册、改密码、用户/团队/仓库 CRUD、仓库浏览页面行为 |
| ✅ #003 | Git HTTP 行为清单 | 已新增 `docs/migration/m0-003-git-http-behavior-checklist.md`，记录 `clone`、`fetch`、`push`、认证失败、权限不足、仓库不存在和 service 不支持行为 |
| ✅ #004 | SSH 行为清单 | 已新增 `docs/migration/m0-004-ssh-behavior-checklist.md`，记录 SSH clone/fetch/push、host key、端口、public key 认证、Git 命令分派和权限行为 |
| ✅ #005 | Identity 与领域 schema smoke test 骨架 | 已新增 `docs/migration/m0-005-identity-domain-schema-smoke-tests.md`，建立可运行的新数据库创建、Identity/领域数据读取写入和基础权限查询 smoke test 入口 |
| ✅ #006 | 权限服务测试基线 | 已新增 `docs/migration/m0-006-permission-service-test-baseline.md` 和 `GitCandyRepositoryPermissionQueryTests`，覆盖匿名、公有仓库、私有仓库、owner、team、administrator 权限语义 |
| ✅ #007 | MVC smoke test 基线 | 已新增 `tools/migration/m0-007/Invoke-M0MvcSmokeTests.ps1` 和 `docs/migration/m0-007-mvc-smoke-test-baseline.md`，覆盖首页、仓库列表、登录页、主要表单和错误页 |
| ✅ #008 | Git HTTP integration script | 已新增 `tools/migration/m0-008/Invoke-GitHttpIntegration.ps1` 和 `docs/migration/m0-008-git-http-integration-script.md`，可重复运行本地 Git HTTP `clone`、`fetch`、`push` 集成脚本 |
| ✅ #009 | 安全与 PR 验证模板 | 已新增 `docs/migration/m0-009-security-and-pr-validation-template.md` 和 `.github/PULL_REQUEST_TEMPLATE.md`，记录 Identity 密码策略、cookie 生命周期、安全戳、私有仓库匿名访问规则和迁移 PR 验证模板 |

验收：

- 有可重复的本地测试数据和测试命令。
- 当前项目关键行为被文档化。
- 后续任何迁移 PR 都能说明是否影响 Git 协议、数据库 schema、公开路由。

### ✅ Milestone 1：新 ASP.NET Core 10 MVC 外壳

目标：建立可运行的新项目壳，不迁业务。

定位：

- 只搭建 host、solution、基础项目和占位路由。
- 不搬迁旧业务代码，不提前做 UI redesign。

#### ✅ M1 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #010 | SDK-style 主程序项目 | 已创建 `src/GitCandy` ASP.NET Core MVC 主程序项目，目标 `net10.0`，作为唯一 GitCandy host |
| ✅ #011 | SDK 固定策略 | 已新增 `global.json` 固定 .NET 10 SDK feature band，并 roll-forward 到 `latestFeature` |
| ✅ #012 | Solution 迁移 | 已新增 `GitCandy.slnx` 作为迁移主线，本地验证脚本和 CI workflow 均固定到 `.slnx`；迁移完成后旧 `GitCandy.sln` 已删除 |
| ✅ #013 | 构建公共属性 | 已新增 `Directory.Build.props`，新项目启用 `ImplicitUsings`、`Nullable` 和 `TreatWarningsAsErrors` |
| ✅ #014 | Central Package Management | 已新增 `Directory.Packages.props` 管理包版本 |
| ✅ #015 | 标准 MVC pipeline | 已在 `Program.cs` 建立 `AddControllersWithViews`、routing、static assets、错误处理、HSTS 和 HTTPS 重定向基础管线 |
| ✅ #016 | 认证/授权占位 | 已加入 Identity、认证 cookie、授权策略、session/localization 的占位配置，不迁旧认证 |
| ✅ #017 | 兼容占位路由 | 已新增 `/`、`/Repository`、`/Account/Login`、Git Smart HTTP 等兼容占位路由，并用 HTTP smoke test 保护 |
| ✅ #018 | `System.Web` 入口检查 | 已新增 `SystemWebEntryCheckTests`，在 `GitCandy.slnx` 测试中确认新项目不引用 `System.Web`、`System.Web.Mvc`、`System.Web.Optimization`、`System.Data.Entity` |
| ✅ #019 | 空壳构建验证 | `dotnet build .\GitCandy.slnx` 已通过，Debug 构建 0 警告/0 错误 |

验收：

- `dotnet build GitCandy.slnx` 能构建新空壳。
- `/`、`/Repository`、`/Account/Login` 等占位路由存在。
- 没有旧 `System.Web` 引用进入新项目。

### ✅ Milestone 2：配置、日志、缓存、DI 和后台生命周期

目标：先把横切基础设施从 `Global.asax` 转成 ASP.NET Core 形态。

定位：

- 迁移启动、停止、配置、缓存、DI、后台任务生命周期。
- 仍以行为保持和可诊断性为主，不重写业务模型。

#### ✅ M2 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #020 | 配置迁移 | 已新增 `GitCandy:Application` appsettings 配置、`GitCandyApplicationOptions`、启动期校验和旧 `UserConfiguration` 别名；旧 `LogPathFormat` 不迁移 |
| ✅ #021 | 路径配置抽象 | 已新增 `IGitCandyApplicationPaths`，将应用路径解析收敛到 `IWebHostEnvironment.ContentRootPath/WebRootPath` |
| ✅ #022 | 标准日志 | 运行时代码统一通过 DI 使用 `ILogger<T>` 和 ASP.NET Core logging providers，不保留旧静态日志入口 |
| ✅ #023 | 缓存替换 | 已注册 `IMemoryCache`，新增 `IApplicationCache`/`MemoryApplicationCache` 作为旧 `HttpRuntime.Cache` 迁移入口，并补充门禁和缓存行为测试 |
| ✅ #024 | DI 替换 MEF | 已新增 `IMembershipService`、`IRepositoryService`、`IGitServiceFactory`、`IGitRepositoryPathResolver` 和 `ISchedulerJob` DI 注册，补充 MEF 门禁与迁移记录 |
| ✅ #025 | Quartz.NET Scheduler hosted service | 已引入 Quartz.NET in-memory scheduler，使用 `AddQuartz` / `AddQuartzHostedService` 接入 ASP.NET Core 生命周期，并通过 bridge job 执行 DI 注册的 `ISchedulerJob` |
| ✅ #026 | SSH 生命周期占位 | 已新增 `SshServerHostedService`、`ISshServerRuntime` 和占位 runtime，将内置 SSH 启停接入 ASP.NET Core hosted service，并保留 graceful shutdown 取消令牌入口 |
| ✅ #027 | Profiler 迁移 | `Profiler` 改为 middleware 或 action filter |
| ✅ #028 | 启停诊断 | 已新增 host lifecycle 启停日志，并为 SSH runtime 与 Quartz scheduler job 注册失败补充结构化诊断日志 |
| ✅ #029 | 跨宿主路径验证 | 已新增启动期路径验证和 repository/cache 根目录边界解析，配置路径在 Windows、IIS、Kestrel 下可预测 |

验收：

- 应用启动/停止日志清晰。
- Scheduler/SSH 不再依赖 `Application_Start/Application_End`。
- 配置路径在 Windows/IIS/Kestrel 下可预测。

### ✅ Milestone 3：EF Core 数据层迁移

目标：建立新的 EF Core + ASP.NET Core Identity 数据模型，并让 GitCandy 领域模型能正确读取和写入。

定位：

- 以新系统 schema 为目标，不兼容旧 `Users`、`AuthorizationLog`、`PasswordVersion`。
- SQLite 是当前主程序的实现和运行验收 provider；M3/M4/M5/M6/M7 的垂直切片先围绕 SQLite 打通。SQL Server 仅补齐独立 provider、初始 migration 和 migration SQL 生成审阅，不切换主程序默认 provider。
- PostgreSQL 和 SonnetDB 作为后续 provider 工作保留记录；迁移主线当前不扩大它们的 migration、schema 差异和部署验证范围。
- Provider 配置参考 IoTSharp 的做法：基础 `DbContext` 保持 provider-neutral，按配置选择 provider，并为不同 provider 保留独立 migrations assembly。

#### ✅ M3 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #030 | `GitCandyDbContext` 基线 | 已新建 provider-neutral `GitCandyDbContext : IdentityDbContext<GitCandyUser>`，领域表由 #033 扩展 |
| ✅ #031 | Provider 配置 | SQLite 锁定为短期业务实现与运行验收 provider；provider-neutral context 与独立 migrations assembly 边界保留，SQL Server migration 由 #038 补齐，PgSQL/SonnetDB 不扩大范围 |
| ✅ #032 | Identity 标准 schema | 已新增 SQLite `InitialIdentitySchema` migration/snapshot，并用 `MigrateAsync` smoke test 验证 `AspNetUsers`、`AspNetRoles`、claims、logins、tokens、Identity 索引和 `__EFMigrationsHistory` |
| ✅ #033 | GitCandy 领域表 | 已重新建模 `Teams`、`Repositories`、用户/团队角色和 `SshKeys`，并用 SQLite migration-backed smoke test 覆盖表/索引创建、读写、Identity user id 外键和 SSH fingerprint 唯一约束 |
| ✅ #034 | Identity user id 外键 | `UserRepositoryRoles.UserId`、`UserTeamRoles.UserId`、`SshKeys.UserId` 均使用 Identity `AspNetUsers.Id`，并用 SQLite FK 元数据、孤儿 user id 写入失败和删除用户级联清理验证 |
| ✅ #035 | 约束与索引 | 已显式配置 Identity/domain key 长度、字段长度、required、PK/FK、cascade delete、唯一索引和大小写不敏感名称规则，并用 EF metadata 与 SQLite duplicate-case smoke tests 验证 |
| ✅ #036 | Lazy loading 决策 | 已明确不引入 lazy loading proxies，`GitCandyDbContext` 禁用 lazy loading，实体不支持代理导航，查询必须显式 `Include`、join、`Any` 或投影 |
| ✅ #037 | DbContext 注入边界 | 各 provider 统一注册 pooled `IDbContextFactory<GitCandyDbContext>` 和 scoped `GitCandyDbContext`；应用服务按 scope 使用 context，后台工作按次创建独立 context，并有生命周期/并发测试保护 |
| ✅ #038 | Migration 策略 | SQLite 与 SQL Server 均有独立 `InitialIdentitySchema` migration/snapshot；SQL Server idempotent SQL 可离线生成并审阅，baseline 明确排除旧认证表，旧元数据导入另做工具 |
| ✅ #039 | 数据层 smoke tests | 已覆盖 SQLite migration/Identity/领域 CRUD/权限/约束/lazy loading、Identity 密码存储校验、DbContext scope/factory 隔离，以及 SQL Server Identity/领域 migration SQL、类型、索引和旧表排除；PgSQL/SonnetDB 保留为后续可选 provider 验证 |

验收：

- 新 SQLite 数据库可通过 EF Core migration 创建；早期 `EnsureCreated` 只作为 smoke test，不作为发布迁移依据。
- SQL Server schema 或 migration SQL 可生成并审阅，至少覆盖 Identity schema 和 GitCandy 领域表。
- Identity 存储层的用户创建/密码校验和团队/仓库 CRUD 测试通过；浏览器 cookie 登录已由 M4 验收。
- SQLite 与 SQL Server schema 差异被记录；PostgreSQL/SonnetDB 若作为可选 provider 发布，也必须各自补齐 migration SQL 和差异说明。
- 没有隐式 lazy loading 导致的 N+1 或空集合行为变化。

### ✅ Milestone 4：认证、授权和会话

目标：让 Web UI 和 Git HTTP 都有清晰认证方案。

定位：

- Web UI 使用 ASP.NET Core Identity cookie。
- Git HTTP Basic Auth 独立认证 scheme，不能依赖浏览器 cookie 或 Session 缓存。

#### ✅ M4 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #040 | Identity cookie 登录 | Web UI 使用 `.GitCandy.Identity` application cookie，用户名/邮箱登录和 cookie 往返测试通过 |
| ✅ #041 | 旧认证不兼容决策 | 已明确不兼容旧 `_gc_auth` cookie、旧密码 hash、`PasswordVersion` 和旧 `AuthorizationLog` |
| ✅ #042 | 账户页面实现 | 已采用 MVC `AccountController` + Razor Views 实现登录、注册、改密、POST 登出和拒绝访问页面 |
| ✅ #043 | `ICurrentUser` | 已增加 scoped `ICurrentUser`，从 `HttpContext.User` claims 读取当前 Identity 用户，不使用 `Token.Current` |
| ✅ #044 | 授权 handler | 已实现 repository read/write/owner、team administrator、current user 和 system administrator policy/handler |
| ✅ #045 | Git Basic Auth scheme | 已实现独立 `GitCandy.GitBasic` scheme，使用 Identity 密码、锁定和 claims；M6 endpoint 只需显式绑定该 scheme |
| ✅ #046 | Session 收敛 | 当前新 host 没有必要 Session 用途，已移除 `AddSession`/`UseSession`；Git Basic 在无 Session 服务时通过 |
| ✅ #047 | Cookie 安全设置 | 已配置 `HttpOnly`、`SecurePolicy=Always`、`SameSite=Lax`、8 小时期限和 sliding expiration |
| ✅ #048 | Identity 行为测试 | 注册、登录、POST 登出、密码修改、失败计数、锁定、安全戳和另一会话旧 cookie 失效测试通过 |
| ✅ #049 | 权限行为测试 | 私有仓库匿名拒绝、公有仓库按配置匿名读写、管理员、owner、team member/team administrator 测试通过 |

验收：

- 注册、登录、登出、密码修改、锁定/失败计数、安全戳失效行为可测。
- 私有仓库匿名不可读。
- 公有仓库匿名按配置可读/可写。
- 管理员、仓库 owner、团队权限测试通过。

### ✅ Milestone 5：MVC Controllers 和 Razor Views 迁移

目标：迁移页面功能，但不顺手重做 UI。

定位：

- 第一轮保持现有 Razor 页面和 Bootstrap 3 视觉行为。
- 迁移页面可用性、表单验证和 URL 兼容，不引入新前端构建链。

#### ✅ M5 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #050 | Controller API 迁移 | 控制器从 `System.Web.Mvc.Controller` 迁移到 `Microsoft.AspNetCore.Mvc.Controller` |
| ✅ #051 | Web API 替换 | 替换 `HttpException`、`Request.Url`、`Request.UserHostAddress`、`Response.AddHeader`、`Response.Cookies.Set` 等 |
| ✅ #052 | Razor imports | Views 增加 `_ViewImports.cshtml`，移除 `Views/Web.config` |
| ✅ #053 | Helper 迁移 | `System.Web.Mvc.Html` helper、`MvcHtmlString` 改为 ASP.NET Core helper/tag helper、`IHtmlContent` 或 `HtmlString` |
| ✅ #054 | URL helper 迁移 | `ViewContext.HttpContext.Request.Url.PathAndQuery` 改为 ASP.NET Core API |
| ✅ #055 | 资源与本地化 | `App_GlobalResources` 迁移到标准 resx/localization 方案，必要时保留强类型资源访问 |
| ✅ #056 | 静态资源迁移 | `Content`、`Scripts`、`fonts` 迁移到 `wwwroot` |
| ✅ #057 | Bundling 过渡 | `System.Web.Optimization` 第一阶段替换为直接静态引用 |
| ✅ #058 | 页面 smoke tests | 主要页面可打开、表单可提交、验证信息正确 |
| ✅ #059 | URL 与视觉兼容 | 静态资源、语言切换、字体、highlight、marked、bootstrap-switch 工作，页面 URL 与旧版兼容 |

验收：

- 主要页面可打开、表单可提交、验证信息正确。
- 资源语言切换可用。
- 静态资源路径、字体、highlight、marked、bootstrap-switch 工作。
- 页面 URL 与旧版兼容。

### ✅ Milestone 6：Git Smart HTTP 迁移

目标：保住 Git 客户端协议行为。

定位：

- 这是 GitCandy 的核心垂直切片，优先于大规模 UI 清理。
- 必须保持 streaming、headers、URL escaping、认证挑战和 Git 客户端错误行为。
- Git 协议执行必须收敛在 `IGitTransportBackend`，业务层不得散落命令行调用。

#### ✅ M6 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #060 | Smart HTTP endpoint | 已将两套兼容 Git URL 从占位响应切换到 ASP.NET Core `GitController.Smart` |
| ✅ #061 | 请求/响应 streaming | 已使用 `Request.Body`、helper stdin/stdout 和 `Response.Body` 异步流式复制，不完整读入 pack |
| ✅ #062 | gzip request body | 已使用流式 `GZipStream` 解压并通过 endpoint 测试 |
| ✅ #063 | Git headers | 已保留 Smart HTTP content type、no-cache headers、protocol v0/v1/v2 framing 和 Basic challenge |
| ✅ #064 | 请求限制配置 | 已新增 4 GiB request body、30 分钟 timeout、stream buffer 配置及 IIS/Nginx 说明 |
| ✅ #065 | URL 与路径安全 | 已覆盖 URL escaping、跨平台分隔符、dot traversal、repository root 与 symlink/junction 最终路径边界 |
| ✅ #066 | Git transport backend 边界 | 已建立 `IGitTransportBackend`，使用 `ProcessStartInfo.ArgumentList` 收敛 upload-pack、receive-pack、upload-archive |
| ✅ #067 | Git clone/fetch/push 验证 | 真实 Kestrel + SQLite + Git 客户端 `.git`/无后缀 clone、fetch、Basic Auth push 通过 |
| ✅ #068 | 大 pack 流式验证 | 24 MiB 随机文件 pack 经同一流式 endpoint push 通过 |
| ✅ #069 | 权限失败行为 | 已覆盖匿名/错误凭据 401、已认证无权限 403、仓库不存在 404、service 不支持 403 |

验收：

- `git clone http://.../git/{repo}.git` 通过。
- `git fetch` 通过。
- `git push` 通过。
- 大文件/较大 pack 流式传输。
- 权限不足时 Git 客户端收到正确 401/403/404 行为。

### ✅ Milestone 7：SSH 和后台任务现代化

目标：让 SSH Server 和任务调度适应 ASP.NET Core 生命周期。

定位：

- 默认路线是内置 SSH server 随 GitCandy host 同进程运行。
- 只迁移生命周期、DI、数据库访问边界、Git 后端复用和可诊断性。
- 外部 OpenSSH forced command 仅作为可选部署适配，放到迁移稳定后按独立任务评估。
- SSH 协议栈替换或大升级放到迁移稳定后单独做。

#### ✅ M7 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #070 | 内置 SSH hosted service | `SshServerConfig` 改为 hosted service，随 GitCandy host 同进程启动和停止 |
| ✅ #071 | SSH DI 权限接入 | `GitSshService` 通过 DI 获取权限服务和配置 |
| ✅ #072 | SSH 与 Git backend 复用 | SSH clone/fetch/push 复用 `IGitTransportBackend`、仓库路径解析、权限判断、审计和 hook pipeline |
| ✅ #073 | 后台 DbContext 边界 | 后台线程访问数据库使用 `IDbContextFactory` |
| ✅ #074 | SSH 配置迁移 | 保留端口、host keys、开关配置迁移路径 |
| ✅ #075 | SSH 安全评估 | 评估自写 SSH 协议实现的安全性、算法兼容性、host key 管理和禁用交互 shell/SFTP/端口转发的策略 |
| ✅ #076 | Scheduler 取消支持 | Quartz.NET job 支持 `CancellationToken`，应用停止时等待或取消后台任务并记录诊断日志 |
| ✅ #077 | 启停日志与失败诊断 | 端口占用、host key 缺失、后台任务异常日志可诊断 |
| ✅ #078 | SSH clone/fetch/push 验证 | SSH clone、fetch、push 通过 |
| ✅ #079 | 关闭与端口冲突验证 | 应用停止时 SSH listener 和 scheduler 正常退出；端口被占用时应用行为明确 |

验收：

- 内置 SSH server 默认随 GitCandy host 同进程运行，不要求部署者配置外部 SSHD。
- 应用停止时 SSH listener 和 scheduler 正常退出。
- SSH clone/fetch/push 通过。
- 端口被占用时日志可诊断，应用行为明确。

### ✅ Milestone 8：部署、运维和文档

目标：让新版本能被部署和回滚。

定位：

- 完成迁移发布前的部署、备份、回滚、配置迁移和健康检查闭环。
- 面向部署者的变更必须同步 README/CHANGES 或迁移说明。

#### ✅ M8 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #080 | 部署方式说明 | 只支持 Docker Compose、Linux systemd、Windows Service；明确不支持 IIS |
| ✅ #081 | 配置迁移指南 | `Web.config` connectionStrings/appSettings 到 `appsettings.json`/环境变量的对照表 |
| ✅ #082 | 旧数据策略 | 明确旧用户数据不兼容；旧仓库/团队元数据若要迁移，使用独立导入工具 |
| ✅ #083 | 文件系统路径指南 | 说明 repository、cache、git-core、SSH host keys 和 Data Protection keys 路径；日志输出由宿主 provider 管理 |
| ✅ #084 | Health checks | 增加数据库连接、repository path、cache path、Git backend、SSH listener 检查 |
| ✅ #085 | 备份策略 | 明确数据库、repositories、cache、host keys 和 key ring 的备份和恢复策略 |
| ✅ #086 | Migration SQL | Release CI 生成 SQLite/SQL Server migration SQL；生产启动先检测并自动应用 pending migrations |
| ✅ #087 | 回滚方案 | 有可执行的版本固定、schema 快照恢复和前置备份要求 |
| ✅ #088 | 文档/CHANGES 更新 | 面向用户、部署者、数据库、认证、公开 URL 的变更同步文档 |

验收：

- 新旧配置对照表完成。
- 可以创建并启动新的 EF Core/Identity 数据库。
- 有回滚方案和备份说明。

### ✅ Milestone 9：迁移稳定后的独立改进

目标：收拢第一轮迁移之外的改进项，避免塞进主迁移大改。

定位：

- 这些都值得做，但必须在行为迁移稳定后按独立变更推进。
- 每个编号都应独立评估收益、维护成本、兼容性和回滚方案。
- M9 是并行改进池，不要求完成 Identity、SSH、Nullable 等工作后才开始 UI；各工作流只遵守自身依赖顺序。
- UI 工作流固定按 `#090 -> #096 -> #100 -> #101/#102/#103 -> #104 -> #105` 推进，原型未评审前不修改生产 Razor 页面。

#### ✅ M9 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #090 | UI 信息架构与双主题原型基线 | 已完成页面/角色/状态矩阵、浅色/深色 token、整体布局和桌面/移动原型；未修改生产 Razor 页面 |
| ✅ #091 | Identity 增强 | 已增加可配置密码策略、TOTP/恢复码/受信任浏览器和可选 OpenID Connect 登录、注册、绑定与解绑流程 |
| ✅ #092 | SSH 协议栈替换或升级 | 已替换为 Microsoft Dev Tunnels SSH，现代 OpenSSH 无兼容参数完成 SSH clone/fetch/push |
| ✅ #093 | Nullable 全面启用 | 活动项目已全面启用 nullable，移除迁移源码中的局部关闭和无说明的 null-forgiving，用显式空值契约清理告警，并新增源码门禁测试；全解决方案构建 0 警告/0 错误，完整测试通过 |
| ✅ #094 | 架构拆分深化 | 已建立 Core/Data/Git/SSH/Web 编译边界、模块内 DI 注册和项目依赖方向门禁；公开行为保持不变 |
| ✅ #095 | Observability | 已引入 OpenTelemetry tracing/metrics/logging，覆盖 ASP.NET Core 请求、runtime、Git transport 和 Quartz job，并支持可配置 OTLP/Console exporter |
| ✅ #096 | 前端资产管线 | 已使用 npm lockfile + esbuild 建立离线资产构建，Lucide 自托管打包，Docker 独立 Node stage 不进入运行镜像 |
| ✅ #097 | LibGit2Sharp 升级 | 活动 net10.0 主线已采用 0.31.0 / NativeBinaries 2.0.323，并覆盖 bare 初始化、仓库验证、HEAD/commit/branch/tag 和 native runtime 回归 |
| ✅ #098 | 减少 Git helper 依赖 | 仓库初始化、验证和元数据读取已进入托管服务；外部进程门禁将 helper 锁定为 upload-pack、receive-pack、upload-archive |
| ✅ #099 | 外部 OpenSSH 可选适配 | 已提供默认关闭的 AuthorizedKeysCommand + key-specific forced-command 入口，复用 Identity key、权限、路径和 transport；内置 SSH 仍为默认 |
| ✅ #100 | 双主题运行机制与应用框架 | 已实现首屏 System/Light/Dark、`.GitCandy.Theme` 持久化和全局响应式 header/navigation/content/footer |
| ✅ #101 | 仓库工作区 UI | 已迁移列表、详情、clone URL、元数据/权限操作，并由 #106-#108 补齐真实代码树、提交、diff、blame、compare 和归档工作区 |
| ✅ #102 | 账户与凭据 UI | 已迁移登录、注册、资料、密码、Identity security 和 SSH key 页面，不改变认证语义 |
| ✅ #103 | 团队与管理 UI | 已迁移用户、团队、成员、协作者和只读设置页面，服务端授权保持不变 |
| ✅ #104 | 响应式、无障碍与完整状态 | 已覆盖桌面/移动、键盘焦点、reduced motion 与 empty/error/denied/destructive 等状态 |
| ✅ #105 | 视觉回归与 Bootstrap 3 收尾 | 已建立 Light/Dark、桌面/移动 Playwright 基线并移除 Bootstrap 3/jQuery/Glyphicons 运行时资产 |
| ✅ #106 | 仓库生命周期与读取服务契约 | bare 创建、remote import、fork network、默认分支、安全删除和读取 DTO 已收敛到应用/Git 服务，controller 不直接操作 LibGit2Sharp |
| ✅ #107 | Tree、Blob、Raw 与代码片段链接 | 已实现语法高亮、binary/large/未知编码降级、固定 SHA permalink、`#Lx-Ly` 与复制，并覆盖私有权限、symlink/submodule 和路径边界 |
| ✅ #108 | Commit、Diff、Blame 与 Compare | 已实现 history/detail、parent diff、branch/tag ref 解析与选择、blame、compare 和异步流式 ZIP，具备 diff/archive 大小、取消和权限边界；独立 Branches/Tags/Contributors 页面转入 M12.5 `#139G-#139I` |
| ✅ #109 | Git LFS v2 垂直切片 | 已实现 basic batch/upload/download/HEAD/verify、权限/配额、临时区 SHA-256 校验与原子提交，并通过真实 `git lfs` push/fetch/clone；locking 后补 |

验收：

- 不混入主迁移路线的大 PR。
- 每个改进项都有独立兼容性说明、回滚方案和验证结果。
- `#090` 必须先交付页面/角色/状态矩阵、主题 token、布局规则和可交互框架原型，由评审结论冻结生产实现边界。
- `#096` 只建立资产交付能力；`#100` 到 `#103` 按独立垂直切片逐批修改生产 UI，不合并成一次大改。
- UI 验收至少覆盖匿名用户、普通用户、repository owner、administrator，以及 Light/Dark、桌面/移动和权限失败状态。
- UI 变更不得改变公开路由、表单字段、antiforgery、Identity cookie、Git HTTP/SSH 或服务端权限判断。
- `#101` 只有在 `#106-#108` 提供真实 tree/commit/diff 服务、页面与权限/大文件验证后才能关闭；不能以静态空页面或重定向代替代码工作区。`#109` 是独立 Git LFS 协议切片，不混入 UI 收尾。

### ✅ Milestone 10：稳定命名空间、改名限频与历史地址兼容

目标：把用户、团队和仓库从可变名称迁移到稳定 ID，Web 提供 `/{namespace}/{repository}`，Git HTTP/LFS 提供 `/{namespace}/{repository}.git`；旧地址和 retained alias 只保留名称占用，不提供 Web、Git HTTP/LFS 或 SSH 访问。

定位：

- 这是跨 fork PR、企业身份同步、团队改名、仓库镜像和 Code Intelligence 的共同前置层，应先于 M12 `#138` 和 M14-M16 实施。
- 用户和团队进入同一个大小写不敏感的全局 namespace claim；系统保留路由、当前 slug 和有效 alias 不能冲突。
- 默认 alias 保留 365 天，可通过配置修改；用户/团队 slug 在任意滚动 7 天内最多成功改名 3 次。
- `DisplayName` 与 URL `Slug` 分离；修改显示名称不创建 alias，也不消耗改名次数。
- 历史 alias 直接指向稳定 namespace/repository ID，不建立字符串跳转链。
- Web、Git HTTP、SSH 必须复用同一 resolver、权限、路径边界、审计、hook 和 transport backend。

#### ✅ M10 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #110 | Namespace 与 URL 契约冻结 | 冻结 Web `/{namespace}/{repository}`、Git HTTP/LFS `/{namespace}/{repository}.git` 与 SSH namespace 形态、保留系统 slug、大小写和 `.git` 规则；不映射 legacy `/git/{project}` |
| ✅ #111 | 稳定 namespace/alias schema | 新增稳定 namespace、namespace alias、repository alias、rename event 模型；SQLite migration 可运行，SQL Server migration SQL 可审阅，唯一索引覆盖大小写和有效占用 |
| ✅ #112 | 统一 namespace/repository resolver | 所有 Web、Git HTTP、SSH、archive/cache/path 操作先解析稳定 ID，再做权限和根目录边界检查；禁止 controller 按字符串各自查询 |
| ✅ #113 | 原子改名与限频服务 | 用户/团队 slug 在滚动 7 天最多成功改 3 次；并发改名、大小写变体、系统保留名和 alias 抢占在事务/唯一约束下失败并审计 |
| ✅ #114 | Alias 生命周期与配置 | 默认 `AliasRetentionDays=365`，后台任务幂等处理到期、延长、释放和删除主体保留策略，管理页显示有效期和占用原因 |
| ✅ #115 | Web 规范地址直切 | UI 只生成当前 `/{namespace}/{repository}`，旧 `/Repository/...` 和 alias URL 返回 404；私有资源不泄漏存在性 |
| ✅ #116 | Git HTTP/LFS 规范地址直切 | 仅 `/{namespace}/{repository}.git` 完成 clone/fetch/push/LFS；legacy、alias 和无 `.git` remote 返回 404，streaming、headers、401/403/404 和大 pack 行为不回归 |
| ✅ #117 | SSH 规范地址直切 | 当前 namespace/repository 路径复用 resolver、权限与 `IGitTransportBackend` 并通过真实 clone/fetch/push；legacy 与 alias 命令在 transport 启动前拒绝 |
| ✅ #118 | 改名管理与审计 UI | 用户/团队/仓库改名预览冲突、剩余次数、alias 到期时间和受影响 URL；灾难恢复 override 独立授权、要求理由和二次确认 |
| ✅ #119 | 路由与并发验证报告 | SQLite/SQL Server、Web/Git/LFS/SSH、连续/并发改名、alias 到期占用、旧地址 404 与真实 Git 客户端矩阵均覆盖 |

验收：

- `https://host/team-or-user/repository` 访问页面，`https://host/team-or-user/repository.git` 完成 Git HTTP/LFS clone/fetch/push，SSH 使用相同 namespace/repository `.git` 语义。
- 用户/团队连续 3 次改名成功，第 4 次在滚动 7 天内失败；失败尝试不消耗次数，并发请求不能突破限制。
- 默认 365 天内旧名称不可被任何用户或团队占用；到期释放、管理员延长和删除主体保留均有测试及审计。
- 历史名称在保留期内继续占用但不可访问；旧 Web、Git HTTP/LFS、SSH、alias 和无 `.git` remote 均返回 not found。
- 公开路由变化同步 README、部署配置、CHANGES 和迁移/回滚说明。

详细设计和竞品依据见 `docs/product/enterprise-repository-roadmap.md`。

### ✅ Milestone 11：Issues 与仓库讨论闭环

目标：为每个仓库提供轻量但完整的问题跟踪能力，让缺陷、需求、任务、技术讨论和代码片段可以被创建、分类、指派、引用、订阅和关闭，并为 M12 PR 复用 timeline、Markdown、mention 和通知基础。

定位：

- GitCandy 学习 GitHub/Gitea 的轻量 Issue 闭环和 GitLab/Gitee 的负责人/里程碑工作流，不在本阶段实现 boards、epics、iterations、工时或任意自定义字段。
- Issue 与 PR 使用仓库级、事务安全、单调递增的共享 `WorkItemNumber`，但保持独立实体和应用服务；`#123` 在仓库内唯一且可引用。
- Issue body/comment 支持受限 CommonMark、fenced code block、task list、mention、work-item/commit 引用；原始 Markdown 与安全渲染结果分离，代码块不得执行。
- 私有仓库 Issue、标题、评论、引用和通知必须复用仓库读取权限，不能通过全局列表、mention、搜索或错误消息泄漏存在性。
- 第一版通知以持久化站内 inbox 为准，邮件只是可选投递器；通知发送前和读取时都要复核权限。

#### ✅ M11 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #120 | Work item 编号与 Issue schema | 建立 repository-scoped sequence、Issue、timeline event、subscription 和乐观并发字段；SQLite migration 可运行，SQL Server migration SQL 可审阅，并发创建不重号 |
| ✅ #121 | Issue CRUD、状态与列表 | 实现 create/read/edit/open/close/reopen、author/assignee 权限、分页、open/closed、author/assignee/label/milestone 过滤和稳定排序 |
| ✅ #122 | 安全 Markdown 与评论 | 实现 body/comment、fenced code block、inline code、task list 和编辑历史；HTML sanitization、危险 URL、超长内容和 XSS 测试闭环 |
| ✅ #123 | Labels、Milestones 与 Assignees | 仓库级 label 颜色/描述、milestone 截止时间/进度、负责人和协作者管理；删除/归档元数据不破坏历史 Issue |
| ✅ #124 | Mention、引用、订阅与通知 | 支持 `@user`、`#123`、`owner/repo#123`、commit SHA、assignment/reply/status 通知和订阅/退订；目标不可读时不建可见反向引用 |
| ✅ #125 | Issue templates | 从受控仓库路径读取 Markdown template，支持默认模板、query 预填和缺失/无效模板降级；template 不能读取仓库根之外文件 |
| ✅ #126 | 关系与自动关闭语义 | 支持 related、duplicate、blocks/blocked-by；PR merge 或默认分支 commit 中的 `fixes/closes/resolves #123` 幂等关闭并记录 timeline |
| ✅ #127 | 讨论治理与速率限制 | repository owner 可锁定/解锁讨论；编辑、隐藏和删除保留审计语义，对匿名/低权限创建与 mention fan-out 设置独立限流 |
| ✅ #128 | Issue MVC 垂直切片 | 完成仓库 Issue 导航、列表、详情、创建/编辑、timeline、metadata 和通知 inbox 的 Razor UI，覆盖 empty/error/denied/mobile 状态 |
| ✅ #129 | Issue 权限与集成验证 | 覆盖 public/private、author、assignee、owner、team、administrator、并发编号、Markdown XSS、跨仓库引用、通知泄漏和 SQLite/Kestrel smoke tests |

验收：

- 用户能在有权访问的仓库创建、编辑、评论、分类、指派、订阅、关闭和重新打开 Issue。
- fenced code block、task list、mention、work-item/commit 引用正确渲染且不能注入脚本或危险链接。
- labels、milestones、assignees、relations 和 timeline 在并发编辑、元数据删除/归档后保持一致。
- 私有仓库 Issue 不通过引用、通知、搜索、全局计数或 403/404 差异泄漏。

### ✅ Milestone 12：Pull Request、代码评审与合并

目标：在 GitCandy 内完成从分支变更提议、diff 评审、修改迭代、批准到安全合并的日常协作闭环。

定位：

- 第一垂直切片先支持同一仓库内 source/target branch；M10 稳定 namespace 和 `#106` fork 生命周期完成后再接跨 fork PR，不能用字符串仓库名维持跨仓库关系。
- PR 必须有 Conversation、Commits、Files changed 三个核心视图，并汇总 draft、conflict、review、check 和 mergeability 状态；Checks 的外部写入接口由 M13 提供。
- 普通评论与 review thread 分开建模。行内评论锚定 original base/head SHA、path、old/new side、line 和 hunk context；新 push 后可靠重映射，否则标记 `Outdated`。
- 合并前必须重新读取 source/target tip、冲突、approval/check、draft 和权限，并用 repository 级锁或等价乐观并发避免合并过期 head。
- 第一版提供 merge commit 和 squash；rebase merge、merge queue/train、批量 suggestion apply 和在线解决冲突延后。

#### ✅ M12 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #130 | PR schema、编号与引用快照 | 建立 PullRequest、source/target ref、original/current base/head SHA、merge result 和并发字段，复用 M11 WorkItemNumber/timeline；维护服务端只读 `refs/pull/{number}/head` 等内部 refs，拒绝客户端写入，分支删除后历史仍可读 |
| ✅ #131 | 创建、Draft 与状态流转 | 同仓库 branch compare 后创建 PR，支持 draft/ready、edit、close/reopen；禁止 source=target、无差异、无读写权限和重复 open PR |
| ✅ #132 | Conversation、Commits、Files changed | 展示 description/timeline、提交列表、merge-base diff、renames/binary/large diff 降级、分页/折叠和固定 commit 链接 |
| ✅ #133 | 行内 Review threads | 支持单行/范围评论、reply、resolve/unresolve 和 outdated；新 push 后基于 hunk context 重映射，不能把评论静默挂到错误代码 |
| ✅ #134 | Reviewer 与 Review 状态 | author/assignee/reviewer 分离，支持 request review、comment、approve、request changes、dismiss/re-request；本人批准和过期批准策略显式配置 |
| ✅ #135 | Mergeability 与冲突检测 | 汇总 draft、source/target 变化、conflict、required approval/check 和 branch policy；状态异步刷新但合并时必须同步复核 |
| ✅ #136 | Merge commit 与 Squash 服务 | 所有 ref 写入收敛到受控 merge service，生成可审阅 message，校验目标未变化，写入失败不留下半完成状态，并触发 hook/audit/index queue |
| ✅ #137 | Issue 关联与自动关闭 | PR 显示 related Issue，merge 成功后按 closing keywords 幂等关闭目标 Issue；close/reopen 未合并 PR 不关闭 Issue |
| ✅ #138 | Fork 与跨仓库 PR | 在 M10 namespace 和 #106 fork 生命周期上支持同 fork network 的 source repository，删除 fork/source branch 后保留审计和可诊断状态 |
| ✅ #139 | PR/Review/Merge 集成验证 | 覆盖并发 push/merge、outdated thread、冲突、权限撤销、branch 删除、merge/squash、hook 失败、大 diff 和真实 Git fetch/push 后 ref 结果 |

验收：

- 开发者能 push branch、创建 draft PR、转 ready、请求 reviewer，并查看 Conversation/Commits/Files changed。
- reviewer 能留下稳定的行内 thread、request changes 和 approve；新 commit 后 thread 正确重映射或明确 outdated。
- merge/squash 只在最新 head、权限、approval/check 和 branch policy 满足时执行，并能用真实 Git 客户端 fetch 到正确结果。
- 私有仓库、fork、source branch 和 review 内容不通过 PR 列表、引用、通知或 diff API 越权暴露。

### ✅ Milestone 12.5：稳定性、代码浏览基本面与开发门禁收口

目标：在继续增加治理功能前，修复本次核对发现的缺陷和基本功能缺口，补齐 Branches、Tags、Contributors 仓库浏览入口，让现有 Git/Issue/PR 主链在生产部署、并发和跨平台 CI 中可持续验证。

定位：

- 本阶段不新增协作领域模型，不与 M13 PAT、webhook、branch protection schema 混在一起。
- Git HTTP alias、Issue 限流属于已完成能力的缺陷修复，必须先恢复稳定测试证据。
- 密码恢复、TLS 快速部署和 CI 门禁属于自托管产品的基本可用性，不应推迟到企业身份或 Code Intelligence 阶段。
- 活动主线已经能读取 branch/tag ref 并用于代码页和 PR，但尚未提供独立的 Branches/Tags/Contributors 页面；M9 完成口径只覆盖 ref 读取与 revision 选择，当前 ASP.NET Core 主线的页面和管理闭环由本阶段补齐，不恢复 MVC5 页面或实现。
- Branch/Tag 写操作必须收敛到受控 Git 服务并复用仓库写权限；M13 branch protection 上线后继续复用同一策略入口，不在 controller 或 Razor 中直接修改 ref。

#### ✅ M12.5 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #139A | Git HTTP 规范地址稳定性 | 覆盖规范 `.git` 地址连续/并发 clone、fetch、push、协议 v2、响应结束和 reverse proxy，并断言 legacy、alias 与无 `.git` remote 返回 404；不得用重试掩盖协议缺陷 |
| ✅ #139B | Issue 限流原子化 | 替换“先 Count 再写入”的并发可绕过实现；多实例边界明确，并发请求不能突破额度，mention fan-out 与权限检查不回归 |
| ✅ #139C | Identity 账号恢复 | 实现一次性、限时密码重置 token、邮件确认/重置投递抽象、管理员安全恢复、成功后安全戳失效、枚举防护、限流和审计 |
| ✅ #139D | TLS 与 proxy 快速部署 | 提供 Caddy 或 Nginx Compose 示例、Forwarded Headers 信任边界、Secure cookie、HTTPS redirect、clone URL 和 OIDC callback smoke test |
| ✅ #139E | 跨平台 CI 与覆盖率 | PR 同时运行 Windows/Linux build+test，采集 Core/Data/Auth/Git 覆盖率并逐步达到 80%，把 Playwright 关键流程和视觉回归接入 CI |
| ✅ #139F | 发布与恢复演练 | 在发布前验证容器/服务包启动、SQLite migration、备份/恢复、Data Protection keys、仓库/LFS 数据和版本回滚的一致恢复集 |
| ✅ #139G | Branches 页面与基础管理 | 为规范 namespace 地址提供分支列表，显示默认分支、tip commit、更新时间及相对默认分支的 ahead/behind；空仓库和 unborn HEAD 明确降级。删除操作要求仓库写权限、antiforgery、默认分支保护、结构化 ref 参数和受控服务；真实 fetch/prune 后结果一致，并为 M13 branch protection 保留统一策略入口 |
| ✅ #139H | Tags 页面与基础管理 | 为规范 namespace 地址提供 tag 列表，区分 lightweight/annotated tag，显示目标 commit、tagger/date/message 和 archive 入口；删除要求仓库写权限、antiforgery、合法 `refs/tags/*` 校验和受控服务，真实 fetch/prune 后结果一致。signed tag 展示与策略校验仍按 P2 独立推进 |
| ✅ #139I | Contributors 与仓库统计 | 按指定 branch/tag/commit 聚合 Git author commit 数并展示 contributor 排名，同时提供 commit、contributor、file、source size 和 repository size 摘要；作者归并规则显式且不自动绑定 Identity、不公开邮箱。遍历数量、输出数量、缓存、超时、取消和大仓库降级可配置，私有仓库权限与非法 revision/path 不泄漏数据 |

验收：

- `dotnet build` 保持 0 warning/0 error，Windows 与 Linux 完整测试稳定通过。
- Git alias 真实客户端矩阵重复运行无间歇失败，Issue 限流具备并发测试。
- 用户能在不泄漏账号存在性的前提下完成密码恢复；默认部署文档能形成可登录的 HTTPS 环境。
- 规范 namespace 仓库导航可进入 Branches、Tags、Contributors；公开/私有、匿名/可写/管理员、空仓库和非法 ref 均有 MVC 集成测试。
- Branch/Tag 删除不能删除默认分支或逃逸允许的 ref namespace，真实 Git 客户端能观察到正确结果；Contributors 在大仓库边界下可取消、限时且不暴露作者邮箱。
- 覆盖率、浏览器 smoke test、发布启动和备份恢复结果进入 CI 或发布门禁，不只保留人工说明。

### 🚧 Milestone 12.6：SonnetDB 生产部署垂直切片

目标：在不改变 SQLite 默认部署的前提下，让配置明确选择 SonnetDB 的 GitCandy 实例具备独立 migration、Identity/领域读写保护网，并部署到 `gitcandy.com` 与现有 sonnet.vip Caddy/SonnetDB 栈协同运行。

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ✅ #139J | Host 按配置选择 SonnetDB | Composition Root 读取 `GitCandy:Database:Provider` 后只注册 SQLite 或 SonnetDB 其中一个；Web 与 OpenSSH 命令路径一致，默认仍为 SQLite |
| ✅ #139K | SonnetDB migration 与兼容修复 | 完整 Identity/领域 migration 可在远程空库执行；修复唯一索引 NULL 语义、Serializable 事务读取、savepoint 能力声明、EF `COALESCE` 投影和 Server 镜像构建；Identity 注册/登录、repository CRUD 与真实 Git HTTP smoke 通过 |
| 🚧 #139L | `gitcandy.com` 生产部署 | 复用现有 Caddy 与内部 SonnetDB，固定代理信任边界，持久化 repository/LFS/host key/Data Protection 数据；DNS、TLS、Web 登录、HTTP/SSH clone/fetch/push 和备份恢复全部验证后完成 |

验收：

- SonnetDB 只在配置选择时加载，SQLite 现有测试和部署行为不回归。
- SonnetDB migration、Identity、权限查询和领域 CRUD 在嵌入式测试与远程 Server profile 上均通过。
- `gitcandy.com` 的 HTTP 自动跳转 HTTPS，Caddy 不缓冲 Git pack 响应，SSH 使用独立端口且 SonnetDB 不暴露公网。
- 数据库、repositories、LFS、SSH host key 和 Data Protection keys 纳入同一备份/恢复与回滚演练。

### ⬜ Milestone 12.7：个人工作台、公开个人页与仓库发现

目标：在不重做全站视觉体系的前提下，为登录用户提供以“我的”为入口的个人工作台，把现有账户详情升级为完整个人页，并为所有访问者提供基于真实活跃度与热度指标的公开仓库发现页。

定位：

- “我的”是登录后左侧主菜单第一项，位于全局 `Repositories` 之前；未登录用户不显示该入口，不能用隐藏菜单代替服务端授权。
- “我的”展开后的二级菜单顺序固定为 `Repositories`、`Packages`、`Stars`、`设置`、`团队`。桌面侧栏直接展示二级菜单，窄屏在现有 navigation drawer 中保持同一顺序、active state 和键盘操作。
- “发现”是独立一级主菜单，紧跟“我的”；未登录时它是左侧第一项。它不属于“我的”二级菜单，也不替代全局仓库管理入口。
- 页面参考成熟代码托管个人页的信息密度和浏览习惯，但沿用 GitCandy 的 Razor、样式 token、Lucide 图标、权限服务和稳定 namespace，不复制外部品牌、文案、标识或专有组件。
- 本切片只现代化个人工作区，不顺带重做仓库、Issue、PR 或管理员页面，也不引入 React、Tailwind 或新的前端构建链。
- `Stars` 纳入最小可用的仓库收藏领域能力；`Packages` 先定义可复用的目录页与数据契约，OCI 镜像上传下载、存储、保留与删除由 M15.6 独立实现，NuGet/npm/Maven 等其他 registry 继续后置，不能伪装成已可发布制品。

#### 导航与公开路径契约

| 入口 | 规范路径 | 可见性与行为 |
| --- | --- | --- |
| 我的 | `/me` | 仅登录用户；解析当前 Identity 用户后跳转到其规范个人页，默认打开 `Repositories`；保留安全的本地 `returnUrl`，未登录时进入登录流程 |
| 个人页 | `/{username}` | 单段规范用户名路径；默认等价于 `?tab=repositories`，复用 M10 的大小写、保留字、稳定 ID 和改名占用规则；未知用户、历史 alias 和非法名称返回 404 |
| Repositories | `/{username}?tab=repositories` | 公开访问只返回查看者有权读取的仓库；本人查看时仍由仓库权限服务过滤，不建立绕过权限的“我的仓库”查询 |
| Packages | `/{username}?tab=packages` | 只展示查看者有权读取且已有目录记录的 package；registry 尚未启用时呈现真实空状态，不提供无后端的上传按钮 |
| Stars | `/{username}?tab=stars` | 公开访问只展示仍可公开读取的收藏；本人可看到自己有权读取的私有仓库收藏，权限丢失后立即不可见 |
| 设置 | `/{username}?tab=settings` | 仅本人可见；非本人返回 404，避免暴露账号管理入口；敏感操作继续使用独立 POST、antiforgery、重新认证和安全戳规则 |
| 团队 | `/{username}?tab=teams` | 本人查看完整成员关系与可执行操作；其他查看者只看到允许公开展示的团队，不泄漏私有团队、成员关系或仓库名称 |
| 发现 | `/explore` | 匿名和登录用户均可访问；只展示公开且允许匿名读取的仓库，不因当前用户额外权限混入私有仓库；`explore` 进入系统保留 slug |

路径设计说明：个人子页使用受 allowlist 约束的 `tab` 查询参数，而不是 `/{username}/repositories` 等两段路径，避免与现有 `/{namespace}/{repository}` 仓库规范 URL 冲突。未知 `tab` 统一规范化到 `repositories`；页面生成的链接只使用小写规范值。固定系统路由优先于单段个人路由，相关保留用户名必须进入 M10 的统一保留字校验。

#### 公开仓库发现与推荐规格

发现页使用紧凑、可扫描的公开仓库列表，不使用营销 hero 或装饰性卡片：

- 页面顶部提供关键字搜索、主要语言、更新时间窗口和归档状态筛选；推荐、近期活跃、Star、下载、访问量采用 segmented control 或排序菜单切换，筛选条件可通过 URL 复现。
- 默认“推荐”列表展示排名、namespace/name、可见性、描述、主要语言色板、最近提交时间、近期提交频率、Star 数、下载量和访问量；指标展示使用明确时间窗，不能把累计值伪装成近期趋势。
- 列表支持稳定分页、空状态和移动端单栏布局；仓库名称、描述和指标不得因超长文本造成水平溢出或布局跳动。
- 推荐理由使用短标签解释主要贡献因素，例如“近期活跃”“Star 增长”“下载较多”，不展示内部权重、用户身份或可用于反推访问者的细粒度事件。

推荐信号与统计口径：

| 信号 | 统计口径 |
| --- | --- |
| 提交频率 | 按公开仓库默认分支的有效 commit 增量计算 7/30/90 天窗口，结合活跃天数和时间衰减；导入旧历史不能在导入当天伪造活跃峰值，后台统计不得每次扫描完整 Git 历史 |
| Star | 使用 `RepositoryStars` 的唯一用户计数、近期新增速度和撤销后的净值；禁用/删除账号、批量异常账号和仓库 owner 自己的 star 不参与趋势加权 |
| 下载量 | 分别累计成功的 archive、release asset、package 和 LFS 下载；Git clone/fetch 作为独立 Git 获取信号记录，尚未实现的 release/package 类型不能产生虚假计数 |
| 访问量 | 只统计成功的规范仓库页面访问，过滤健康检查、静态资源、已知 bot、重复刷新和管理员探测；按日聚合去重，不保存可反查个人的原始 IP、Authorization header、token 或完整 User-Agent |

推荐计算边界：

- 使用 `RepositoryMetricDaily` 保存按仓库、日期、指标类型聚合的计数，使用 `RepositoryRecommendationSnapshot` 保存计算时间、算法版本、各信号归一化值、总分和排名；原始事件只保留完成聚合所需的最短周期。
- 默认分数对提交频率、Star、下载量和访问量分别做 `log1p`/分位数归一化与时间衰减，再按配置权重合成；必须记录算法版本，权重变化通过新快照生效，不改写历史结果。
- Quartz 后台 job 增量汇总指标并生成不可变推荐快照；Web 请求只读取最近成功快照，计算失败时回退到上一快照或确定性的最近更新排序，绝不在请求或 Git HTTP/SSH 热路径实时计算。
- 新仓库使用明确的冷启动策略和最低样本门槛；排序使用稳定次级键，防止同分抖动。仓库改为私有、归档、删除或关闭匿名读取后必须立即从查询结果过滤，无需等待下一次快照。
- 建立防刷与隐私门槛：速率限制、异常突增检测、同主体重复行为降权、管理员诊断和算法审计；排行榜不能公开访问者、下载者、clone 用户或 commit 作者邮箱。

#### 页面布局与内容规格

所有 tab 复用同一个个人页 shell，不为每个栏目复制用户摘要或权限判断：

- 桌面采用无外层卡片的双栏布局：左侧固定宽度个人摘要，右侧为自适应内容区；内容区顶部是个人二级 tab 导航，当前 tab 有明确 active state 和 `aria-current="page"`。
- 个人摘要包含头像或稳定首字母 fallback、显示名、`@username`、简介；本人显示“编辑资料”命令。邮箱默认不公开，后续新增公司、地点、个人链接等字段时必须逐字段定义公开开关、长度、URL scheme 和 XSS 校验。
- 窄屏改为单栏：头像与姓名先出现，简介和资料操作随后，tab 可横向滚动但不能遮挡或挤压文本；搜索、筛选和主操作换行排列，列表保持可扫描而不是退化成横向溢出表格。
- 每个 tab 都有稳定标题、结果计数、加载/空数据/无筛选结果/权限变化状态、分页和返回焦点行为；不使用说明性营销区、装饰性大卡片或嵌套卡片。
- 页面只显示业务内容，不出现设计来源、模仿说明或外部产品名称；页脚、metadata、Open Graph 和错误页同样遵守该要求。

各 tab 内容：

| Tab | 页面内容与操作 |
| --- | --- |
| `Repositories` | 顶部提供仓库名/描述搜索、类型与可见性筛选、最近更新/名称排序，以及本人有权限时的“新建仓库”操作；列表行展示 namespace/name、可见性、归档或 mirror 状态、描述、主要语言色板、star 数、fork 信息和最近更新时间；支持分页，以及“尚无仓库”和“筛选无结果”两种空状态 |
| `Packages` | 提供名称搜索、生态/类型与可见性筛选、最近更新排序；目录项展示 package 名、最新版本、类型、来源仓库、可见性、更新时间和进入详情的链接；OCI 镜像操作只在 M15.6 registry 与权限闭环完成后出现，其他 package 类型继续显示真实未启用状态 |
| `Stars` | 复用仓库摘要的搜索、语言/可见性筛选和最近收藏/最近更新/名称排序；列表展示仓库归属、描述、主要语言、star 数、fork 信息、更新时间，并允许本人 star/unstar；删除仓库或失去读取权限后不得留下可枚举的私有 metadata |
| `设置` | 作为个人设置目录与现有 Account 页面整合，包含公开资料、账号与邮箱、密码/2FA/恢复码、SSH keys、通知偏好，以及 M13 完成后接入的 PAT；每一项显示当前状态和明确命令，敏感值、token、recovery code 不在目录页回显 |
| `团队` | 展示团队标识、名称、简介、本人角色、成员数、可见仓库数和最近活动；按名称搜索并按最近活动/名称排序；本人按权限看到创建团队、进入团队、成员管理或退出命令，最后 owner 和私有团队规则继续由服务端保护 |

#### 数据、权限与性能边界

- 个人页查询使用专用 application service/ViewModel 投影，controller 保持薄；禁止在 Razor 中逐项触发 EF 查询，仓库、star、package、团队计数必须避免 N+1 和重复枚举。
- `RepositoryStars` 使用 Identity user ID 与 repository ID 组成唯一约束，记录 `CreatedAtUtc`；删除用户或仓库时清理关系。SQLite、SQL Server 与 SonnetDB migration、并发重复 star/unstar 幂等和权限变化都要有测试。
- 公开个人页不能显示邮箱、登录状态、SSH/PAT、私有团队、私有 package 或不可读仓库；管理员权限也不能让普通页面意外公开敏感字段。
- 搜索、筛选、排序和分页参数采用 allowlist、长度限制和确定性次级排序；页面大小设上限，取消令牌贯穿数据库查询，不为统计扫描完整 Git 历史。
- 头像第一阶段使用本地 fallback 或受控的应用资源；若后续允许上传或远程头像，必须单独处理内容类型、尺寸、存储路径、缓存、代理和隐私边界。

#### ⬜ M12.7 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ⬜ #139M | “我的”一级菜单与二级导航 | 登录用户左侧第一项为“我的”，二级顺序固定，桌面/移动 active state、键盘、焦点和未登录行为一致；不影响管理员菜单和全局仓库入口 |
| ⬜ #139N | 个人规范路由与页面 shell | `/me`、`/{username}` 和 allowlist `tab` 契约落地，避开 repository 路由冲突；双栏/单栏个人摘要、tab、404、canonical URL 与改名规则有集成测试 |
| ⬜ #139O | Repositories 个人列表 | 权限过滤后的搜索、筛选、排序、分页、语言/状态 metadata、新建入口和空状态完成；本人私有仓库与访客公开视图有差异测试 |
| ⬜ #139P | Repository Stars 垂直切片 | EF Core schema、star/unstar、计数和 `Stars` tab 完成；唯一约束、幂等并发、删除级联、私有仓库权限和三 provider migration 闭环 |
| ⬜ #139Q | Packages 目录页边界 | 完成目录 ViewModel、筛选/排序/分页、权限与真实空状态；预留 M15.6 OCI image 数据契约，形成其他 registry 后端非目标文档，不新增假上传接口或第二套对象存储 |
| ⬜ #139R | 设置与团队整合 | 现有 profile/security/SSH/notification/team 页面进入统一二级导航，敏感设置仅本人可见，团队列表复用团队权限并覆盖最后 owner 边界 |
| ⬜ #139S | 响应式、可访问性与浏览器回归 | 覆盖桌面/窄屏截图、长用户名/长仓库名、键盘和 screen reader 语义、空/错误/分页状态；MVC smoke 与真实浏览器门禁加入 CI |
| ⬜ #139T | “发现”主菜单与公开路由 | “发现”紧跟“我的”，匿名时成为第一项；`/explore`、系统保留 slug、active state、固定路由优先级和公开仓库硬过滤有集成测试 |
| ⬜ #139U | 仓库指标采集与日聚合 | 建立 commit/Star/archive/release/package/LFS/Git 获取/页面访问的可用信号和 `RepositoryMetricDaily`；过滤失败请求、bot、重复事件和敏感字段，采集不延长协议响应 |
| ⬜ #139V | 推荐算法与快照作业 | 建立归一化、衰减、权重、冷启动、防刷、算法版本与 `RepositoryRecommendationSnapshot`；Quartz 增量计算、失败回退、取消、限时和并发资源上限闭环 |
| ⬜ #139W | 发现页列表与推荐验收 | 完成搜索、筛选、排序、推荐理由、分页、空状态和响应式列表；验证四类信号排序、私有仓库即时移除、统计隐私、抗刷、确定性与大规模查询性能 |

验收：

- 登录后左侧第一项稳定显示“我的”，其下按要求显示五个二级入口；所有入口的选中状态、刷新、返回和窄屏 drawer 行为一致。
- “发现”紧跟“我的”；匿名与登录访问得到相同的公开仓库候选边界，登录用户的私有仓库权限不会污染公开推荐结果。
- `/me` 与规范个人页可访问，个人 tab 不抢占任何 `/{namespace}/{repository}` Web 或 Git URL，改名、保留字、大小写和 404 行为与 M10 一致。
- 个人页在桌面和移动端均清晰呈现个人摘要、tab 工具栏、列表内容、操作和空状态；长文本不溢出、不遮挡且不造成布局跳动。
- 访客、本人、团队成员和管理员矩阵证明私有仓库、私有团队、私有 package、邮箱与安全设置不会越权显示。
- `Repositories` 和 `Stars` 可端到端使用；`Packages` 明确显示真实可用数据或空状态，不声称 registry 能力已经完成。
- 发现页能按提交频率、Star、下载量和访问量生成可解释、可版本化的推荐快照；统计采集不保存敏感请求数据，刷量、失败请求和机器人流量不会直接抬高排名。
- `dotnet build GitCandy.slnx`、`dotnet test GitCandy.slnx`、SQLite/SonnetDB 数据 smoke、MVC 集成测试和桌面/移动浏览器 smoke 全部通过；涉及 star schema 的 SQL Server migration SQL 可生成审阅。

### ⬜ Milestone 13：合并治理、外部集成与发布基础

目标：让 Issue/PR 不只是页面功能，而能接入外部 CI、自动化和仓库治理，并形成可审计、可诊断的团队开发入口。

定位：

- 先做外部 CI 所需的 PAT、webhook 和 commit status/check API，不在本阶段自研 runner 或兼容 GitHub Actions workflow。
- 执行顺序为 `#140 -> #140A -> #143 -> #141/#142 -> #144-#149`：先建立机器凭据和基础 push gate，再接外部 CI 和 required checks。
- branch protection 必须同时作用于 Git HTTP/SSH push 和 Web merge；视图隐藏按钮不能替代 pre-receive/update 侧服务端策略。
- webhook 使用版本化 event envelope、签名、delivery ID、重试和脱敏记录；接收方失败不能回滚已成功的 Git push/PR merge。
- 通知、审计、search 和 release 复用 repository 权限；私有资源不能进入无权用户的索引、payload 或失败诊断。

#### ⬜ M13 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ⬜ #140 | Personal Access Token 与 API auth | scoped PAT 只存 hash，支持创建时一次显示、到期、撤销、last-used、审计和 API/Git Basic 独立 scope；token 不进入 URL 或日志 |
| ⬜ #140A | Deploy key 与机器凭据 | 仓库级只读/可写 deploy key、唯一 fingerprint、到期/撤销/last-used、最小权限和审计；不得与用户 SSH key 或 Web cookie 混用 |
| ⬜ #141 | Versioned Webhook delivery | 支持 push、issue、comment、pull_request、review、check、release 事件，HMAC 签名、delivery ID、超时、指数退避、重放和 SSRF/内网目标策略 |
| ⬜ #142 | Commit Status 与 Check API | 外部系统按 commit SHA 写入 pending/success/failure/error、context、target URL 和 summary；幂等更新、权限、限流和过期 head 展示明确 |
| ⬜ #143 | Branch protection 与 push gate | branch pattern、禁止 force/delete、allowed push/merge、required checks/approvals；Git HTTP/SSH 和 Web merge 复用同一策略评估与审计 |
| ⬜ #144 | CODEOWNERS 与 Required Review | 从受控路径解析 CODEOWNERS，按 changed paths 请求 reviewer，支持最少批准数、code owner 批准、dismiss stale approval 和可解释阻塞原因 |
| ⬜ #145 | 通知中心与投递器 | 扩展 M11 inbox 到 PR/review/check/release，支持 read/unread、参与/mention/assignment/review-request 过滤；邮件/webhook 投递失败可诊断不丢 inbox |
| ⬜ #146 | 协作审计日志 | 记录 Issue/PR/review/protection/token/webhook/check/merge/release 关键变更，字段脱敏、按仓库/操作者/时间查询并保持不可由普通用户篡改 |
| ⬜ #147 | Releases 与 assets | 从 tag 创建 release、Markdown notes 和受限附件，校验 tag/repository 权限、文件名/大小/路径边界、下载授权和孤儿清理 |
| ⬜ #148 | 协作搜索 | 搜索 repository、issue、PR、commit 和代码文本，所有结果先按 repository 权限过滤；SQLite-first 方案与后续 provider 扩展边界明确 |
| ⬜ #149 | 治理与外部 CI 端到端验证 | 外部 fixture 收 webhook、写 check，required review/check 阻止或允许 push/merge；覆盖重试、token 撤销、私有数据、并发和审计完整性 |

验收：

- 外部 CI 能使用最小 scope PAT 接收 push/PR webhook、回写 commit check，并参与 required check 合并门禁。
- 保护分支对 Git HTTP、SSH 和 Web merge 一致生效，force/delete/绕过行为有明确授权和审计。
- CODEOWNERS、required approvals/checks 的阻塞原因对有权限用户可解释，规则变化和 bypass 可审计。
- release assets、搜索、通知和 webhook 不泄漏私有仓库数据，失败投递不会破坏 push/merge 主事务。

详细竞品依据、领域模型边界、代码片段、diff anchor 和端到端验收主链见 `docs/product/collaboration-roadmap.md`。

### ⬜ Milestone 14：团队治理与企业身份联邦

目标：将 GitCandy 建设为企业内部 Git 管理入口，提供四级团队角色、管理员可配置的企业连接，以及 Microsoft Entra ID、企业微信、飞书、钉钉的登录与目录同步能力。

定位：

- M14 依赖 M10 的稳定 namespace ID，并复用 M11-M13 的通知、审计、PAT 和权限边界；外部目录改名不能直接改写字符串外键或绕过 rename/alias 规则。
- 登录联邦与目录供应分层：OIDC/SAML/OAuth 负责认证，SCIM/通讯录 API 负责用户、部门、团队和停用生命周期。
- `TeamOwner` 是最高管理员，其下为 `Leader`、`DeputyLeader`、`Member`；至少保留一个不依赖外部目录的本地 `TeamOwner`。
- provider 同步默认只授予 `Member`，外部组到更高角色的映射必须显式配置，不能自动授予系统管理员或删除最后一个 `TeamOwner`。
- 管理员 UI 必须能绑定、设置、测试、启用/停用、预览同步、立即运行、查看健康状态和脱敏错误。

#### ⬜ M14 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ⬜ #150 | 四级团队角色 schema 与权限矩阵 | 将单一 `IsAdministrator` 迁移为 `TeamOwner/Leader/DeputyLeader/Member`，明确成员管理、仓库管理、团队改名/删除、连接管理权限和最后 owner 保护 |
| ⬜ #151 | 团队授权服务与 UI | policy/handler、service、controller 和 Razor UI 复用统一角色比较；角色提升/降级、批量成员操作和仓库授权均服务端复核并审计 |
| ⬜ #152 | 企业连接与 secret 边界 | 建立 authentication/directory/provisioning provider 接口、tenant/external object 映射、secret reference、同步游标和管理员连接页面基线 |
| ⬜ #153 | Microsoft Entra ID 登录连接 | 在 #091 通用 OIDC 基础上支持管理员绑定 tenant、域/issuer 限制、claims 映射、账号冲突处理、连接测试和按组织启用 |
| ⬜ #154 | SCIM 2.0 Users/Groups 垂直切片 | 实现 bearer 保护的 Users/Groups create/query/PATCH、`active=false/true`、分页、幂等 externalId 和 Entra provisioning smoke test |
| ⬜ #155 | 企业微信 adapter | 企业 OAuth 登录、CorpId/UserId 稳定绑定、部门/成员全量与增量同步、最小 scope、停用和同步诊断 |
| ⬜ #156 | 飞书 adapter | tenant 绑定、OAuth 登录、稳定 user ID 映射、部门/成员增量同步、token 轮换和事件去重 |
| ⬜ #157 | 钉钉 adapter | 企业绑定、OAuth 登录、CorpId/unionId/userId 作用域映射、部门/成员同步、token 轮换和事件去重 |
| ⬜ #158 | Deprovision 与对账作业 | 显式停用阻止登录并按策略撤销 session/PAT/SSH/Basic；临时 API 故障不批量删人，全量对账、隔离、恢复和 break-glass 流程可验证 |
| ⬜ #159 | 企业连接安全与集成验证 | 覆盖 secret/log 脱敏、callback state/PKCE、签名/webhook、限流重试、用户冲突、最后 owner、SQLite/SQL Server 和 provider sandbox/fixture 测试 |

验收：

- 四级团队角色有书面权限矩阵和服务端测试，最后一个 `TeamOwner` 无法被删除、降级或被目录同步停用。
- 管理员能在 UI 中完成 provider 绑定、设置、测试、启用、暂停、同步预览、立即同步和错误诊断，普通用户不能读取连接 secret。
- Microsoft Entra ID 先形成 OIDC + SCIM 可验证垂直切片；企业微信、飞书、钉钉分别以 adapter 实现，不在 controller 中堆 provider 分支。
- 同步使用稳定外部 ID 且幂等；邮箱、手机号、显示名变化不会创建重复用户，外部停用和恢复不会误删仓库或审计历史。
- 真实 secret 不进入仓库、普通数据库字段、日志、URL、错误页或配置导出。

### ⬜ Milestone 15：远程账号连接、Pull/Push Mirror 与同步作业

目标：允许用户或管理员绑定 GitHub、GitLab、Gitee 账号，发现并连接远程仓库，通过可观测、可取消、可审计的后台 job 完成导入、单向拉取和单向推送。

定位：

- 远程账号连接与 GitCandy Web 登录相互独立；绑定 token 只用于 provider API 和 Git remote，不进入 Identity cookie。
- 第一阶段只同步 commits、branches、tags。Issues、PR/MR、Wiki、Releases、CI、Packages 和 LFS 不隐式同步。
- `Pull` mirror 以 remote 为权威并默认禁止本地 push；`Push` mirror 以 GitCandy 为权威并在本地 `post-receive` 后异步入队。
- 默认遇到 divergent/non-fast-forward ref 就停止并告警，不做静默 force overwrite；强制策略必须显式授权、二次确认和审计。
- 双向 mirror 默认禁用，待单向同步、保护分支、webhook、冲突恢复和压力测试齐备后再独立评估。

#### ⬜ M15 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ⬜ #160 | Remote account/provider 抽象 | 区分用户连接和组织/服务账号，支持 OAuth/App/PAT/SSH 能力描述、稳定 external repository ID、最小 scope、secret reference、轮换和撤销 |
| ⬜ #161 | GitHub/GitLab/Gitee 绑定与设置 UI | 管理员配置 provider client、callback、启用状态和组织账号；用户绑定/解绑个人账号、发现可访问仓库；测试连接且不回显 token |
| ⬜ #162 | Remote/mirror EF Core schema | 建模 direction、authority、remote identity/URL、ref filter、schedule、divergence/prune policy、游标、状态、last success/failure 和审计；SQLite/SQL Server migration 闭环 |
| ⬜ #163 | 受控 remote sync backend | 所有 fetch/push 收敛到 `IRemoteRepositorySyncBackend`，复用仓库根目录边界和 Git executable resolver，使用 `ArgumentList`/credential helper、流式 I/O、取消、超时和日志脱敏 |
| ⬜ #164 | Pull mirror 垂直切片 | 从远程初始导入和周期 fetch，按 ref policy 更新，支持手动同步、remote rename、只读保护、divergence 检测和失败恢复 |
| ⬜ #165 | Push mirror 垂直切片 | 本地 push 成功后只入队，后台合并 ref 事件并推送；protected/ref filter、keep-divergent、删除 ref 和显式 force policy 可配置且审计 |
| ⬜ #166 | 持久化同步 job pipeline | job 状态与下一次运行持久化，Quartz 负责唤醒；按 remote 串行、实例并发限制、lease、指数退避+jitter、最大重试和 graceful shutdown 后恢复 |
| ⬜ #167 | Webhook、立即同步与运维视图 | provider webhook 验签/去重并加速任务，周期对账兜底；UI 显示排队/运行/成功/失败、分类错误、下一次运行、暂停/重试/取消 |
| ⬜ #168 | Provider connectors | GitHub 优先 GitHub App 短期 token，GitLab 使用 OAuth/application，Gitee 使用 OAuth/PAT 适配；处理 rate limit、token 过期、repo rename/delete 和权限变化 |
| ⬜ #169 | Mirror 兼容、故障与规模验证 | 用 GitHub/GitLab/Gitee fixture 或 sandbox 验证导入、pull、push、ref filter、远程不可用、凭据撤销、并发、大仓库、重启恢复；输出双向 mirror go/no-go 报告 |

验收：

- 管理员和用户可以在各自权限内绑定、测试、启用、禁用和撤销 GitHub/GitLab/Gitee 连接，管理员不能查看明文 token。
- Pull mirror 远程更新可按计划、webhook 或手动触发进入 GitCandy；Push mirror 不延长或回滚用户本地 push 请求。
- job 在应用重启后可恢复，重复 webhook/手动点击不会并发运行同一 remote；取消、超时、重试和限流状态可诊断。
- 默认策略不会覆盖 divergent refs；force/prune 需要清晰风险提示、repository owner/administrator 授权和审计。
- 仓库 mirror 页面明确说明只同步 Git refs，不声称同步 LFS、Issue、PR/MR、Wiki 或 CI 数据。
- 大仓库同步使用流式 I/O，并发限额不会破坏 Git HTTP/SSH clone/fetch/push 延迟和内存边界。

详细账号模型、作业语义、竞品对照和非目标见 `docs/product/enterprise-repository-roadmap.md`。

### ⬜ Milestone 15.5：帮助中心、全量文档重构与部署发布

目标：把分散的 README、迁移记录、产品决策和运维说明整理为一套可持续维护的 GitCandy 文档体系，在主菜单提供“帮助”入口，并在每次部署产物生成时使用 JekyllNet 构建、校验和打包 `/help` 静态站点。

定位：

- “帮助”是匿名与登录用户都可见的一级主菜单，位于业务导航之后、管理菜单之前，打开 `/help/`；帮助站点保持 GitCandy 自身名称、导航和视觉 token，不显示生成器或参考站点品牌文案。
- JekyllNet 作为仓库级 .NET local tool 固定版本，通过 `.config/dotnet-tools.json` 恢复；文档构建使用 `dotnet tool run jekyllnet build --source docs --destination <staging>/wwwroot/help`，不依赖开发机全局工具。
- “部署时生成”指 Docker/build/package/publish 的受控构建阶段，不是在生产请求或应用启动时临时生成。文档构建或链接校验失败时发布产物失败，不能悄悄部署旧帮助页。
- Markdown 源文件是静态帮助站点和 M16 知识库摄入的唯一事实来源；生成 HTML、搜索索引和临时目录都是构建产物，不提交仓库、不再次向量化。
- 本 Milestone 只建立帮助站点和文档质量闭环，不同时引入新的前端 SPA，也不把文档知识库或 MCP 实现提前塞进发布任务。

#### 文档信息架构与重写范围

先建立文档 inventory，给每份资料标注 audience、owner、canonical path、公开级别、最后验证版本和替代/归档关系，再按以下结构重写：

| 文档域 | 必须覆盖的内容 |
| --- | --- |
| 开始使用 | 产品边界、系统要求、首次启动、管理员创建、创建仓库、HTTP/SSH clone/fetch/push、最短故障检查 |
| 账号与个人工作台 | 注册/登录/恢复/2FA、安全戳、个人页、Repositories、Packages 边界、Stars、设置、SSH key、通知和团队入口 |
| 仓库与 Git | namespace/rename/alias、创建/导入/fork/mirror、默认分支、Branches/Tags、代码浏览、archive、LFS、Git HTTP/SSH URL 和认证错误 |
| 协作 | Issues、Markdown、labels、milestones、通知、PR、review、merge/squash、保护分支、check、CODEOWNERS、release 和审计 |
| 管理与安全 | 用户/团队/权限矩阵、PAT/deploy key、企业身份、webhook、限流、路径边界、secret 管理、威胁与故障响应 |
| 部署与运维 | 配置键、SQLite/SQL Server/SonnetDB、Docker、Linux systemd、Windows Service、reverse proxy/TLS、备份恢复、升级回滚、日志、指标、健康检查和容量规划 |
| API 与自动化 | HTTP API 版本、认证、分页、错误模型、幂等/限流、webhook event、OpenAPI、MCP 连接与工具目录，以及 Git/LFS/SSH 协议端点为何不是普通业务 API |
| 开发与架构 | solution/project 边界、数据模型、migration、transport backend、后台任务、测试、发布流程、贡献规范和 ADR/ROADMAP 索引 |
| 排障与发布 | 常见登录、权限、Git、SSH、LFS、数据库、代理和部署故障；版本支持矩阵、CHANGES、迁移说明、已知限制和回滚检查表 |

文档治理规则：

- 保留 `docs/migration` 作为历史证据，但公共帮助中心只链接仍适用于当前版本的规范指南；过期说明必须加 archived/version metadata，不能与当前操作指南并列造成冲突。
- 配置键、路由、权限矩阵、CLI/API schema 尽量从结构化源或测试生成校验清单，避免复制后漂移；代码示例必须可编译或进入 smoke test。
- 每页有稳定 permalink、标题、摘要、版本、语言、公开级别和最后验证信息；站内链接使用 `/help` base path，兼容反向代理 `PathBase`。
- 禁止把 token、密码、连接串 secret、真实邮箱、内网地址和私有仓库资料写入文档、示例、构建日志或搜索索引。

#### 构建、发布与运行时边界

- Docker multi-stage build、`dotnet publish` 包装脚本、Debian 包和 Windows 发布流程复用同一个文档构建入口；生成站点随应用产物打包到 `wwwroot/help`，离线部署也能阅读。
- `/help` 与 `/help/{**path}` 只提供生成目录内的静态文件，规范化路径并阻止目录逃逸；目录 URL、404、缓存头、压缩、content type、CSP 和 `PathBase` 有集成测试。
- CI 执行 JekyllNet build、front matter/schema、站内链接、锚点、重复 permalink、图片、代码片段和敏感信息检查；发布校验确认帮助首页及关键页面存在且版本与应用一致。
- 发布 manifest 记录应用版本、Git commit、文档内容 hash、JekyllNet 版本和生成时间；M16 摄入器读取同一 manifest，保证模型引用与用户看到的帮助版本一致。
- 回滚应用版本时同时回滚帮助站点和文档 manifest；不得让新文档描述旧二进制不具备的功能。

#### ⬜ M15.5 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ⬜ #169A | 文档 inventory 与信息架构 | 盘点 README/CHANGES/ROADMAP/docs/API/部署资料，标注 owner、audience、公开级别、canonical/archived 和缺口；确定 `/help` 导航、permalink 与版本策略 |
| ⬜ #169B | 用户与协作文档重写 | 完成开始使用、账号、个人工作台、仓库、Git HTTP/SSH/LFS、Issue、PR/review 和发现页说明，示例路径与当前路由/权限一致 |
| ⬜ #169C | 管理、部署与排障文档重写 | 完成权限、安全、配置、三数据库 provider、Docker/Linux/Windows/TLS、备份恢复、可观测性、升级回滚和故障排查 |
| ⬜ #169D | API、MCP 与开发者文档基线 | 建立 HTTP API/OpenAPI、webhook、分页/错误/限流、MCP coverage matrix 和架构贡献文档；未实现能力明确标注 roadmap 状态 |
| ⬜ #169E | JekyllNet local tool 与帮助主题 | 固定 local tool 版本，建立 `_config.yml`、layouts、导航、代码高亮、搜索元数据和响应式主题；无外部运行时 CDN 依赖 |
| ⬜ #169F | “帮助”主菜单与安全静态路由 | 主菜单、`/help` 路由、PathBase、404、缓存/CSP/content type、路径边界和匿名访问完成；应用页与帮助站点可互相返回 |
| ⬜ #169G | 部署阶段构建与多产物打包 | Docker、publish、Debian、Windows 流程统一生成并打包帮助站点和 manifest；失败阻止发布，回滚保持二进制与文档版本一致 |
| ⬜ #169H | 文档质量与发布验收 | 链接/锚点/permalink、代码示例、截图、可访问性、敏感信息、版本一致性、离线访问和桌面/移动浏览器 smoke 进入 CI |

验收：

- 主菜单“帮助”在匿名、登录和移动 drawer 中均可访问，`/help` 能覆盖用户、管理员、部署者和开发者的主要任务。
- 所有现行说明都有明确事实来源、owner 和最后验证版本；历史迁移文档不会被误当成当前操作步骤。
- 每种发布产物在部署构建阶段使用固定版本 JekyllNet 生成同一站点，构建失败不发布，运行时不需要 JekyllNet 或网络连接。
- 帮助站点版本、应用版本与文档 manifest 一致，回滚后三者同步；生成目录不进入 Git。
- `dotnet build/test`、JekyllNet build、链接/示例/敏感信息检查及 `/help` 桌面/移动 smoke 全部通过。

### ⬜ Milestone 15.6：SonnetDB-backed OCI Container Registry

目标：在 GitCandy 主进程中提供兼容 OCI Distribution 的容器镜像注册服务，让 Docker/Podman 等客户端能够登录、push、pull 和管理 `host/{namespace}/{image}:{tag}`，并使用 SonnetDB 私有对象桶保存 layer、config 和 manifest 原始内容。

定位：

- GitCandy 负责公开 `/v2/` 协议、token challenge、namespace/repository 权限、metadata、审计、配额、GC 和 Web UI；SonnetDB 只作为受信任的 blob storage backend，不直接面向 Docker 客户端或公网。
- 默认仍保持单 GitCandy host：不要求部署独立 Registry daemon。SonnetDB 可以复用现有生产服务，但 registry 使用独立 database/bucket、最小权限服务凭据、容量配额和备份边界。
- GitCandy 业务数据库仍可选择 SQLite、SQL Server 或 SonnetDB；启用 Container Registry 必须显式配置可用的 SonnetDB 对象存储连接。未配置时 Git/Issue/PR 和 M12.7 Packages 目录继续工作，registry endpoint 明确禁用而不是回退到临时磁盘。
- `IContainerRegistryBlobStore` 隔离 OCI 领域与存储实现，SonnetDB adapter 使用受支持的 `SndbObjectStorageClient`/结构化 API。不得假设当前 S3 风格端点已经完整兼容 AWS SigV4 或现成 Registry S3 storage driver。
- 本阶段依赖 M12.7 的 Packages 页面与 namespace UX，以及 M13 的 PAT、审计、限流和保护边界；不阻塞当前 Git 服务主线，也不把 NuGet/npm/Maven registry 混入同一切片。

#### 公开地址与 OCI Distribution 协议

规范镜像引用为 `registry-host/{namespace}/{image}:{tag}` 或 `@sha256:{digest}`。同域部署使用 GitCandy HTTPS host；独立 registry host 只是反向代理和证书配置差异，仍进入同一 GitCandy 应用。

| 协议能力 | 必须实现的行为 |
| --- | --- |
| API 探测 | `GET /v2/` 返回规范成功响应或 `401` challenge，不能跳转 Web 登录页 |
| Blob 查询与拉取 | `HEAD/GET /v2/{name}/blobs/{digest}`，校验 digest，支持 `Range`/`206`、准确长度与内容类型，响应全程流式 |
| Blob 上传 | `POST /v2/{name}/blobs/uploads/` 创建 UUID；`GET` 查询 offset；`PATCH` 顺序追加 chunk；`PUT ...?digest=sha256:...` 完成并校验；`DELETE` 取消，`Location`/`Range`/`Docker-Upload-UUID` header 正确 |
| Monolithic upload 与 mount | 支持客户端一次性完成上传；cross-repository mount 只有在调用者对源有 pull、目标有 push 且 digest 已存在时才建立引用，否则回退普通 upload |
| Manifest | `HEAD/GET/PUT/DELETE /v2/{name}/manifests/{reference}`，保存并按原始 bytes 计算 digest，正确协商 OCI/Docker media type，支持 tag、digest、image index 和多架构 manifest |
| Tags 与 catalog | `GET /v2/{name}/tags/list` 支持稳定分页；全局 catalog 默认仅管理员可见，匿名用户不能借 catalog 枚举私有名称 |
| 错误协议 | 使用 OCI Distribution 规定的 error envelope、code 和 status，不把浏览器 HTML 或通用 ProblemDetails 混入 registry wire contract |

首期非目标：Notary/cosign 签名验证、SBOM/attestation/referrers、漏洞扫描、远端 registry proxy cache、跨实例复制、地理镜像、OCI artifacts 通用 UI 和其他 package ecosystem。这些能力必须在 push/pull、GC 和恢复保护网稳定后分别规划。

#### SonnetDB blob 存储映射与必须补强项

建议使用 bucket `gitcandy-registry`，对象键采用内容寻址：

```text
blobs/sha256/{digest-prefix}/{digest-hex}
uploads/{upload-uuid}/...
```

- layer、config 和 manifest 都按 SHA-256 digest 保存为不可变 blob；tag 只是 GitCandy metadata 中指向 manifest digest 的可变引用。相同 digest 跨 image/namespace 物理复用，但授权始终按逻辑 image 引用复核。
- SonnetDB 已有流式 Put/Get、SHA-256、HEAD、Range、multipart、quota、version、lifecycle、retention 和 audit，可作为基础；GitCandy adapter 不能把 layer 全量读入内存，也不能经 Base64/JSON 转发二进制内容。
- OCI upload chunk 映射到 SonnetDB multipart part，并持久化 UUID、upload ID、下一 offset、过期时间和 owner。完成时先组合并校验客户端声明 digest，校验成功后再原子发布；失败内容不可通过最终 digest 路径读取。
- 在生产启用前，SonnetDB 必须具备或由 adapter 补齐 conditional create、同 digest 并发去重、临时对象到 digest key 的原子 promote/等价提交语义。若只能 copy+delete，必须记录双倍 I/O、崩溃窗口和恢复规则，不能宣称原子。
- SonnetDB bucket policy 当前不能代替 GitCandy authorization；bucket 只授予 GitCandy 服务凭据，不按最终用户建立 policy。所有 namespace/image pull/push/delete 在 GitCandy endpoint 与 service 再次校验。
- 对象版本和 delete marker 不能无限累积。Registry GC 必须显式删除无引用版本、过期 multipart part 和临时对象，并与 retention/legal hold 协调；不能仅配置按时间过期的 bucket lifecycle 删除仍被 manifest 引用的 layer。
- 当前对象写入和 metadata 发布顺序需要补充 kill、掉电、disk-full 和 fsync 语义验证；未达到“成功响应后的 blob 可恢复”门槛前不得用于生产镜像仓库。

#### Registry metadata 与一致性

EF Core 领域表建议为：

| 实体 | 用途与约束 |
| --- | --- |
| `ContainerRepositories` | 稳定 ID、namespace、image name、可见性、owner、创建/更新时间和删除状态；namespace/name 大小写与保留字规则明确 |
| `ContainerBlobs` | digest、size、media type、SonnetDB object key、状态和创建时间；digest 全局唯一，只有 `Available` blob 可被读取 |
| `ContainerManifests` | manifest digest、schema/media type、原始 blob 引用、subject 与 platform 摘要；manifest graph 可遍历 |
| `ContainerManifestBlobs` | manifest 到 config/layer/child manifest 的引用边，用于授权、配额、删除和 GC mark |
| `ContainerTags` | repository + tag 唯一，指向 manifest；更新使用并发版本并记录旧/new digest 审计 |
| `ContainerUploadSessions` | UUID、repository、actor、SonnetDB upload ID、offset、状态、过期时间和并发版本；重启后可恢复或清理 |

- metadata migration 覆盖 SQLite、SQL Server 和 SonnetDB；blob bytes 只进入 SonnetDB bucket，不塞入 EF `byte[]` 列。
- manifest/tag metadata 与对象存储无法天然共享数据库事务，必须采用可恢复状态机：`Uploading -> Verifying -> Available`，先保证 blob 可读再发布引用；失败/超时记录可由后台 reconciliation 修复或回收。
- 删除 tag/manifest 默认只删除逻辑引用，不立即删除共享 blob。后台 mark-and-sweep 从所有可见 tag、保留 manifest、subject/referrer 和活动 upload 标记，经过 grace period 后才能 sweep。
- namespace 配额同时记录逻辑引用大小、唯一 blob 物理大小、tag/manifest 数和上传并发；显示口径必须明确，不能因跨 namespace dedup 错算或泄漏其他 namespace 是否拥有某 digest。

#### 认证、权限与审计

- Registry 使用独立 `RegistryBearer` authentication scheme。`docker login` 可以用用户名 + scoped PAT 交换短期 bearer token；Web Identity cookie、Git Basic 和 SonnetDB token 不作为 Docker 客户端凭据。
- challenge 正确返回 `WWW-Authenticate: Bearer realm=...,service=...,scope=repository:{name}:pull,push`；token 的 audience、expiry、repository scope 和操作集合经过签名校验，撤销 PAT 或停用用户后不能继续换取新 token。
- scope 至少拆分 `registry:pull`、`registry:push`、`registry:delete`、`registry:admin`。公开 image 可匿名 pull；push/delete 需要 owner/team/admin 权限并在服务端复核，不能只信任客户端请求 scope。
- 审计记录 login/token exchange、push start/complete/fail、pull、tag mutation、manifest delete、mount、GC、quota reject 和管理员 bypass；不记录 bearer/PAT、Authorization header、layer 内容或可识别的下载者隐私数据。
- Docker API 设独立限流、上传并发、请求体、idle timeout 和 bandwidth 配额；reverse proxy 禁止缓冲大 layer，响应压缩不能应用于 layer/manifest 原始 bytes。

#### 备份、GC 与运维边界

- Registry 备份必须绑定 EF metadata checkpoint、SonnetDB bucket 对象/metadata 和 registry manifest，记录一致性点、对象数量、总大小与 SHA-256。只备份业务表或只复制 bucket 都不算可恢复。
- 恢复后运行只读 reconciliation：检查所有 tag/manifest/blob 引用、object existence/size/digest、活动 upload 和孤儿对象；验证通过后才开放 push/delete，pull 可按风险策略只读开放。
- Quartz job 承载 upload expiry、reconciliation、GC mark/sweep 和容量统计，使用持久化状态、lease、取消、批次上限与低优先级 I/O，不能影响 Git clone/fetch/push 或 Web 登录。
- 提供 health/diagnostics：SonnetDB 连接、bucket、可用容量、上传积压、GC epoch、孤儿/缺失 blob、最近备份恢复、digest mismatch 和端到端 probe；日志和 metrics 不包含 image 私有 metadata。
- 成功的公开 image pull 只有在 package 显式关联 GitCandy repository 时，才按日聚合进入 M12.7 的下载推荐信号；失败、鉴权拒绝、私有 image 和重复 chunk/Range 请求不能抬高公开仓库排名。

#### ⬜ M15.6 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ⬜ #169I | OCI Distribution 兼容矩阵与架构决策 | 冻结支持的 distribution API/media type/client 矩阵、image name/URL、同域/独立域部署、非目标和回滚；确认 SonnetDB SDK 而非假定通用 S3 driver |
| ⬜ #169J | Registry blob abstraction 与 SonnetDB hardening | 建立 `IContainerRegistryBlobStore` adapter，验证流式、Range、multipart、SHA-256、conditional create、并发去重、原子 promote、fsync/disk-full/crash 和对象版本清理 |
| ⬜ #169K | OCI metadata schema 与 migration | 建立 repository/blob/manifest/link/tag/upload session 状态机、唯一索引、并发版本和三 provider migration；bytes 不进入 EF 表 |
| ⬜ #169L | Registry token service 与权限 | 独立 bearer challenge、用户名+PAT 换短 token、pull/push/delete/admin scope、公开 pull、团队/owner/admin policy、撤销和审计闭环 |
| ⬜ #169M | Blob push/pull 垂直切片 | 完成 probe、HEAD/GET/Range、POST/PATCH/PUT/DELETE upload、digest 校验、恢复上传、monolithic upload 和 cross-repository mount；全程流式 |
| ⬜ #169N | Manifest、tag 与多架构镜像 | 完成 manifest media type 协商、tag/digest 查询、OCI index、多架构 child graph、tags/list、受控 delete 和并发 tag 更新 |
| ⬜ #169O | Packages/Container Web UI 与文档 | M12.7 Packages 接入 image/tag/platform/size/pull 命令、可见性、更新时间和删除/保留操作；同步 M15.5 `/help`、配置/部署/备份说明和 CHANGES，不泄漏私有 digest 或 SonnetDB 地址 |
| ⬜ #169P | 配额、GC、reconciliation 与运维 | 实现逻辑/物理配额、upload expiry、mark-and-sweep grace、缺失/孤儿修复、health/metrics/审计和低优先级 Quartz 作业 |
| ⬜ #169Q | OCI conformance 与客户端矩阵 | 运行 OCI Distribution conformance，并用 Docker、Podman/兼容客户端覆盖 login、push/pull、resume、Range、mount、multi-arch、错误 envelope、反向代理和 TLS |
| ⬜ #169R | 安全、故障、规模与恢复验收 | 覆盖越权、digest spoof、路径逃逸、token 撤销、并发重复 layer、大镜像、slow client、kill/disk-full、备份恢复、GC 竞态和 Git 服务资源隔离 |

验收：

- 真实 Docker 客户端能对公有/私有 image 完成 login、push、pull、重新 tag 和多架构 manifest；匿名、无 scope、权限不足和 token 撤销行为符合协议。
- 上传中断后可按 offset 恢复；错误 digest 永不发布，并发上传相同 layer 最终只有一个可用内容寻址 blob，不产生不可控版本膨胀。
- 大 layer 请求/响应全程流式，Range、header、media type、error envelope 和 reverse proxy 行为通过 conformance 与真实客户端测试。
- tag/manifest 删除不会误删共享 layer；GC 在 active upload、并发 pull/push 和跨 image 引用下保持正确，过期上传与无引用对象最终可回收。
- SonnetDB bucket 不暴露公网，最终用户权限全部由 GitCandy enforcement；bucket policy 占位能力不会被误当成安全边界。
- 业务 metadata、bucket 和 registry manifest 可一致备份与恢复；kill、掉电模拟、disk-full 和 digest reconciliation 证明成功响应的数据不会静默损坏。
- Registry 限流、并发和后台 GC 不破坏 Git HTTP/SSH clone/fetch/push 延迟、应用内存边界和 graceful shutdown。
- `/help`、部署配置、备份恢复、客户端命令和 CHANGES 与实际 `/v2/` 行为同步，未实现的签名、扫描、referrers 和其他 package ecosystem 不被文档宣称可用。

### ⬜ Milestone 16：Agent Memory / Codebase Intelligence

目标：在 GitCandy 作为 Git 代码托管服务的基础上，落地面向 AI Agent 和开发者的代码库记忆、文档知识库与智能检索能力。系统应能摄入 Git 仓库、M15.5 规范文档、ADR、CI 变更、代码评审记录和 Agent 会话，在 SonnetDB 中建立全文与向量索引，并通过 Web UI、HTTP API 和 MCP 查询“如何使用、代码是什么、谁调用谁、为什么这么设计、改这里会影响哪里”；同时建立全部业务 API 到 MCP 的可审计覆盖矩阵。

定位：

- ⬜ M16 纳入 GitCandy 应用层产品路线，作为迁移稳定后的重点能力建设，并复用 M11-M13 产生的 Issue、PR、review、check 和审计数据。
- 实施顺序排在 ASP.NET Core/EF Core/Identity/Git HTTP/SSH 主迁移跑通之后，不阻塞前置迁移验收。
- 第一版优先做只读文档/代码索引、检索、符号查询、调用关系和影响分析，再扩展业务写入工具、Agent 记忆写入、审计和 Explorer 管理界面。
- 结构化 GitCandy 业务数据继续遵循 SQLite-first 和 EF Core provider 边界；文档块、embedding、全文、向量和 Hybrid Search 明确使用 SonnetDB 知识库 schema。未配置 SonnetDB 时 `/help` 仍可用，但知识检索/MCP docs search 必须明确显示未启用，不能用不可复现的假向量静默降级。
- SonnetDB 可以复用当前 GitCandy 生产数据库服务，但知识库使用独立 database/schema、迁移版本、配额、备份和访问凭据；知识数据的重建、删除或模型升级不得改写 Identity/领域表。

设计原则：

- GitCandy 产品能力优先。Code Memory schema、ingest、MCP tools、API 和 UI 都按 GitCandy 的仓库、用户、团队、权限和部署模型设计。
- 数据库能力抽象优先。若出现全文、向量、混合检索、审计、权限过滤等共性需求，应沉淀为通用服务接口和可替换 provider，而不是把某个搜索引擎或向量库写死在 controller 中。
- Core 轻依赖边界不破坏。`GitCandy.Core` 不直接引入 Roslyn、tree-sitter、libgit2 等大型运行时依赖；代码解析与 Git 扫描放在 `GitCandy.CodeIntelligence`、独立 ingest 工具、后台 worker 或扩展包中。
- 结构化优先，向量补充。文件、符号、调用边、引用边、commit、ADR、会话、工具调用都以结构化表、文档或边表落库；embedding 用于语义召回，不替代确定性的 symbol/edge 查询。
- 安全只读起步。MCP memory tools 第一版默认只读，按 repository/team/owner/branch 隔离；代码片段读取要有大小限制、路径白名单、权限复核和审计事件。
- 增量索引优先。索引任务以 repository、branch、commit、file hash 为边界增量执行，支持 `CancellationToken`，不能阻塞 clone/fetch/push 和 Web 登录。
- 权限一致。搜索、片段读取、调用关系、影响分析和 Agent memory 查询必须复用 GitCandy 的公开仓库、私有仓库、owner、team、administrator 权限语义。
- 文档单一来源。知识库只摄入 M15.5 manifest 中允许的 Markdown、README、CHANGES、ROADMAP、API/ADR 等规范源，排除生成 HTML、`external`、`bin/obj`、secret、临时日志和重复副本；每条命中返回文档版本、canonical `/help` URL、标题、段落和引用位置。
- MCP 是应用服务适配层，不是 controller 的自动反射代理。每个 tool 必须有稳定名称、版本化 JSON schema、scope、分页/大小限制、取消、限流、审计和脱敏结果；写操作还要有幂等键、并发版本和明确确认语义。
- “全部 API 封装”以 coverage matrix 验收：所有业务 API 必须映射为 MCP tool/resource，或被明确分类为非 MCP 端点并记录原因。Git Smart HTTP/LFS 大流传输、SSH wire protocol、登录/OIDC callback、health/static files 等协议或生命周期端点不机械暴露为模型工具。

数据模型草案：

| 类型 | 建议实体 | 用途 |
| --- | --- | --- |
| 仓库与文件 | `code_repositories`、`code_files`、`code_file_versions` | repo/project/branch/commit、路径、语言、hash、mtime、大小、license 元数据 |
| 符号与结构 | `code_symbols`、`code_symbol_locations` | namespace/type/method/property/endpoint/test 等符号定义与位置 |
| 关系边 | `code_edges` | calls/references/implements/tests/imports/routes_to/owns 等边 |
| 文本与向量 | `code_chunks` | 代码块、注释、README、docs、embedding、BM25/Hybrid Search 元数据 |
| 文档知识库 | `knowledge_documents`、`knowledge_chunks`、`knowledge_ingest_state` | canonical source、版本/hash、公开级别、标题/段落、embedding、全文字段、摄入状态和引用 URL |
| Git 演化 | `code_commits`、`code_changes` | commit 时间线、作者、文件变更、热点模块、变更趋势 |
| 决策与记忆 | `code_decisions`、`agent_memories`、`agent_tool_events` | ADR、设计决策、review 结论、Agent 会话摘要和工具调用审计 |
| MCP 目录 | `mcp_tool_catalog`、`mcp_tool_audit` | API/tool/resource coverage、schema 版本、required scopes、调用结果、耗时和脱敏审计 |

#### SonnetDB 文档知识库与自动摄入

- 部署产物携带 M15.5 的规范 Markdown 与 manifest。应用启动完成后由可取消的后台 service/Quartz job 计算 SHA-256，按 H2/H3 和 token 上限切块、保留合理 overlap，仅重建新增/变化/删除文件；摄入失败只影响知识库健康状态，不阻止 Web/Git 服务启动。
- `knowledge_ingest_state` 记录 source hash、文档版本、chunk 数、embedding provider/model/dimension、最近成功与错误；相同 manifest 重复部署幂等，删除或改名文档会清理陈旧 chunk。
- embedding provider 必须显式配置并记录模型/维度。模型或维度变更时构建影子索引、验证召回后原子切换，旧索引按保留期清理；不得把不同模型向量混在同一索引。
- 查询使用 SonnetDB BM25 + HNSW/KNN + metadata filter 的 Hybrid Search，支持文档版本、语言、audience、公开级别和 repository filter；排序融合可解释，低置信度允许返回“未找到”，不能编造文档答案。
- 管理诊断页显示 provider、模型、维度、文档数、chunk 数、manifest 版本、最近摄入、失败原因、重建/取消状态和存储规模；立即重建仅管理员可执行并写入审计。
- 备份包含知识 schema 与 manifest；因为知识库可从源文档重建，恢复策略允许先恢复业务数据再限流重建向量，但不能让重建抢占 Git HTTP/SSH 资源。

#### 全业务 API 的 MCP 覆盖边界

MCP server 随 GitCandy 主 host 暴露受认证的 Streamable HTTP 入口，复用 M13 PAT/Bearer scope、authorization handlers、审计、限流和 OpenTelemetry；可选 stdio adapter 只能作为本机开发入口，不成为生产必需组件。

| API 域 | MCP 规划 |
| --- | --- |
| 帮助与知识 | `docs_search`、`docs_get`、`docs_topics`，返回可验证引用；知识状态和重建只向管理员开放 |
| 仓库与代码 | repository list/detail、tree/blob/history/diff/blame/compare/branches/tags/contributors/search 采用只读 tools/resources；snippet、文件大小、revision 和路径必须限界 |
| Issues 与 PR | list/detail/timeline/review/check 为只读第一批；create/comment/edit/state/review/merge 等写工具在 scope、幂等、并发和审计完成后逐项启用 |
| 用户、团队与个人工作台 | 只暴露调用者有权读取的公开资料、团队、Stars、通知和设置状态；密码、2FA secret/recovery code、cookie、SSH private key、PAT 明文永不返回 |
| 仓库治理与自动化 | branch protection、CODEOWNERS、webhook、status/check、release、mirror 和审计查询复用业务服务；secret 创建只允许一次性受控响应，危险写操作默认关闭 |
| 管理与运维 | health、配置诊断、job/knowledge status 采用最小只读工具；用户停用、权限提升、删除、恢复、force/prune 等高风险动作要求独立 admin scope、确认和完整审计，首版可标记未开放 |
| 协议端点 | Git Smart HTTP、LFS 对象流、SSH、OIDC callback、静态文件和浏览器登录不包装为 MCP tool；提供高层仓库/凭据管理工具替代，并在 coverage matrix 记录原因 |

MCP coverage matrix 从 ASP.NET endpoint metadata/OpenAPI 与显式 MCP catalog 双向对账，CI 对新增/删除/改名 API 强制要求更新映射、schema、权限、文档和测试；禁止“新增 API 但 MCP 清单无记录”。

#### ⬜ M16 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ⬜ #170 | Code Memory 应用方案与 schema 草案 | 定义 repo/file/symbol/edge/chunk/commit/decision/memory schema、索引建议、权限模型、规模边界和 EF Core 映射 |
| ⬜ #171 | ingest 工具第一版：Git + 文件 + 文档块 | 扫描 Git 工作区、README/docs/source 文件，写入 repo/file/chunk/commit 基础数据，支持增量和取消 |
| ⬜ #172 | C# 符号索引器 | 在代码智能模块或工具层引入 Roslyn 分析路径，输出 namespace/type/member/test/endpoint 符号和位置 |
| ⬜ #173 | 调用边与引用边第一版 | 提取 calls/references/implements/tests/imports/routes_to 边，提供 callers/callees/impact 查询服务 |
| ⬜ #174 | Code Memory MCP tools | 暴露 `code_search`、`symbol_search`、`code_callers`、`code_callees`、`code_impact`、`code_snippet`、`decision_search` |
| ⬜ #175 | Hybrid Search 与排序融合 | 将 `code_chunks` 接入全文 BM25、embedding KNN 和 metadata filter 融合排序，并记录可解释排序信息 |
| ⬜ #176 | Agent Memory 应用 API | 提供 Agent memory 写入/读取契约、会话摘要、工具调用审计和 repository/branch 级权限过滤 |
| ⬜ #177 | Code Memory Explorer UI | 展示 repo/project、索引状态、文件/符号搜索、调用关系、影响分析、决策记录和 Agent memory |
| ⬜ #178 | VS Code / Copilot 接入样例 | 通过 MCP/API 展示“解释当前符号”“查找调用者”“改动影响分析”“检索历史决策”等场景 |
| ⬜ #179 | 应用规模与验证报告 | 使用 GitCandy 自身仓库和至少一个中大型 C# 仓库做 ingest/query/profile，输出增量成本和部署建议 |
| ⬜ #180 | 文档 corpus manifest 与分类 | 接入 M15.5 manifest，明确 README/CHANGES/ROADMAP/docs/API/ADR 的 canonical、版本、audience、公开级别和排除规则；生成 HTML/third-party/secret 不入库 |
| ⬜ #181 | SonnetDB knowledge schema 与向量索引 | 建立 documents/chunks/ingest state、BM25、HNSW/KNN、metadata filter、配额、备份和独立权限；记录 embedding model/dimension 与影子索引切换 |
| ⬜ #182 | 自动增量摄入与知识诊断 | 部署后后台按 hash 切块、embedding、upsert/delete，支持幂等、取消、限流、失败恢复和重建；管理页显示 provider、版本、规模与健康状态 |
| ⬜ #183 | 文档 Hybrid Search 与 MCP | 完成 `docs_search/docs_get/docs_topics`、引用、低置信度、版本/语言/权限过滤及检索质量集；回答可回到同版本 `/help` 原文 |
| ⬜ #184 | 全部 API inventory 与 MCP coverage matrix | 从 endpoint metadata/OpenAPI 盘点每个业务/协议端点，记录 tool/resource 名、scope、读写、风险、schema 版本或不可封装原因；CI 检测漂移 |
| ⬜ #185 | MCP host、认证与只读业务 tools | 主 host 提供 Streamable HTTP MCP，接入 PAT/Bearer、授权、限流、取消、分页、审计和 tracing；仓库/代码/Issue/PR/团队等只读域达到 matrix 覆盖 |
| ⬜ #186 | MCP 写工具与危险操作治理 | 按域接入 create/update/comment/review/merge 等写工具，要求最小 scope、antireplay/idempotency、并发版本、确认、审计和可配置禁用；凭据与协议 secret 不暴露 |
| ⬜ #187 | MCP parity、安全与规模验收 | API 与 MCP 结果/权限 parity、schema compatibility、prompt injection、越权、批量枚举、撤销 token、并发、超时和大结果测试闭环；输出未开放高风险动作清单 |

验收：

- 能对 GitCandy 自身仓库完成首次索引和增量索引。
- M15.5 文档能在部署后自动、幂等地摄入 SonnetDB，Hybrid Search 返回同版本 `/help` 引用；文档删除、改名和 embedding 模型升级不会留下混合或陈旧索引。
- 私有仓库内容不会被无权限用户或 Agent tool 查询到。
- `code_search`、`symbol_search`、`code_callers`、`code_callees`、`code_impact`、`code_snippet` 至少有 API 或 MCP smoke tests。
- 代码片段读取有路径归一化、仓库根目录边界检查、长度限制和审计记录。
- 大仓库索引不会影响 Git HTTP clone/fetch/push 的流式行为。
- 每个业务 API 在 MCP coverage matrix 中都有 tool/resource 或明确的非 MCP 原因；只读 tools 达到权限 parity，写工具默认最小 scope 且全部可审计。
- SQLite/SQL Server 默认业务路径仍可运行；未配置 SonnetDB 时帮助站点和核心 Git 服务正常，知识库明确不可用；配置 SonnetDB 后向量 schema、备份恢复和规模验证通过。

## 里程碑总览

| 里程碑 | 优先级含义 | 完成口径 |
| --- | --- | --- |
| ✅ M0 | 行为基线 | 基线测试数据、行为清单、迁移分支和 PR 验证模板已完成 |
| ✅ M1 | 新 host 起步 | 新 ASP.NET Core 10 MVC 空壳可运行 |
| ✅ M2 | 横切基础设施 | 配置、日志、缓存、DI、hosted services 接入已完成 |
| ✅ M3 | 新数据层 | EF Core + Identity 新 schema 可通过 SQLite migration 创建；Identity 存储和权限 smoke test 通过；SQL Server migration SQL 可生成审阅，PgSQL/SonnetDB 后续独立回补 |
| ✅ M4 | 认证与权限 | Web Identity cookie、Git Basic Auth、权限语义测试通过 |
| ✅ M5 | Web 垂直切片 | 账户、团队、仓库 CRUD 页面迁移完成 |
| ✅ M6 | Git HTTP 垂直切片 | Git Smart HTTP clone/fetch/push 完成 |
| ✅ M7 | SSH 与后台任务 | SSH 和 scheduler 完成 |
| ✅ M8 | 发布闭环 | 部署文档、迁移脚本、回滚方案完成 |
| ✅ M9 | 迁移后改进池 | 仓库工作区、生命周期、代码浏览与 Git LFS 均已完成可验证闭环 |
| ✅ M10 | 稳定命名空间 | `/{namespace}/{repository}[.git]`、改名限频、历史 alias、Web/Git HTTP/SSH 兼容与提示闭环 |
| ✅ M11 | Issues | Issue、评论、代码块、labels、milestones、assignees、references、notifications 和权限闭环 |
| ✅ M12 | Pull Request 与 Review | draft、commits、files changed、行内 review、approval、merge/squash 和并发/权限闭环 |
| ✅ M12.5 | 稳定性与基本面收口 | alias 协议稳定、原子限流、Branches/Tags/Contributors、密码恢复、TLS 快速部署、跨平台 CI、覆盖率和恢复演练闭环 |
| 🚧 M12.6 | SonnetDB 生产部署 | provider 按配置选择、SonnetDB migration/兼容保护网已完成，`gitcandy.com` 已解析到目标主机；等待远程部署与 Web/Git HTTP/SSH/恢复验收 |
| ⬜ M12.7 | 个人工作台、公开个人页与仓库发现 | 左侧首项“我的”、五项二级导航、紧随其后的“发现”、个人规范路径、Repositories、Packages、Stars、设置、团队、公开仓库指标推荐和响应式/权限回归闭环 |
| ⬜ M13 | 合并治理与集成 | PAT、webhook、status/check、branch protection、CODEOWNERS、审计、release 和外部 CI 闭环 |
| ⬜ M14 | 企业组织与身份 | 四级团队角色、Microsoft Entra ID、企业微信、飞书、钉钉登录/目录同步和管理员连接界面闭环 |
| ⬜ M15 | 远程仓库连接 | GitHub/GitLab/Gitee 账号绑定、导入、Pull/Push mirror、持久化 job、webhook 和故障诊断闭环 |
| ⬜ M15.5 | 帮助中心与文档发布 | “帮助”主菜单、全量文档重构、JekyllNet 固定工具、部署阶段生成、多产物打包、链接/示例/版本质量门禁闭环 |
| ⬜ M15.6 | OCI Container Registry | GitCandy `/v2/`、SonnetDB blob bucket、registry token/scope、manifest/tag/multi-arch、Packages UI、配额/GC、conformance 和备份恢复闭环 |
| ⬜ M16 | 代码智能、文档知识库与 MCP | Agent Memory / Codebase Intelligence、SonnetDB 文档向量知识库、自动摄入、Hybrid Search、全部业务 API MCP coverage、Explorer、IDE 样例和规模验证 |

## 当前实施顺序

1. 🚧 M12.6：完成 `#139J/#139K` 与 DNS 解析，当前推进 `#139L` 的生产部署与协议/恢复验收。
2. ⬜ M12.7 个人工作台与发现：按 `#139M-#139S -> #139T-#139W` 完成“我的”个人页，再完成“发现”、公开仓库指标、推荐快照和浏览器/隐私/防刷门禁。
3. ⬜ M13 凭据与 push gate：按 `#140 -> #140A -> #143` 完成 PAT、deploy key 和基础保护分支。
4. ⬜ M13 外部 CI：按 `#141/#142 -> #144-#149` 完成 webhook、status/check、required checks/CODEOWNERS、通知、审计、release 和搜索。
5. ⬜ M14-M15：在 M13 的稳定 ID、凭据、通知和审计边界上推进企业身份与远程 mirror。
6. ⬜ M15.5：盘点并重写全部现行说明，固定 JekyllNet，在所有部署产物中生成 `/help` 和版本 manifest；这是后置规划，不阻塞当前 M12.6-M13。
7. ⬜ M15.6：按 `#169I -> #169J/#169K/#169L -> #169M/#169N -> #169O/#169P -> #169Q/#169R` 完成 OCI 协议与 SonnetDB 存储硬化、metadata/auth、push/pull、多架构、UI/GC、conformance 和恢复验收；这是后置 Packages 能力。
8. ⬜ M16：先推进 `#170-#175` 只读代码索引，再以 M15.5 文档执行 `#180-#185` SonnetDB 知识库、文档 MCP 和只读业务 tools；随后完成 `#176-#179` Agent Memory/Explorer/规模报告，并以 `#186-#187` 收口写工具与全部业务 API coverage；全程不进入 Git HTTP/SSH 热路径。

明确后置：M15.6 OCI Container Registry、Wiki、NuGet/npm/Maven 等其他 Packages、内置 CI runner、双向 mirror、LFS locking 和 AI 写入能力不阻塞 M12.5-M13，也不得绕过后续建立的 PAT、branch protection、check 和审计边界。

## 高风险点清单

- `System.Web` 没有兼容层，所有相关 API 都要替换
- Git HTTP 对 streaming、headers、URL escaping、请求体限制很敏感
- 单进程承载 Web、Git HTTP、SSH、后台任务后，资源隔离、取消、限流和 graceful shutdown 必须更严格
- EF Core 默认不 lazy load，原有 navigation 行为可能变化
- SQLite 和 SQL Server 的 collation、GUID、identity/autoincrement 行为不同
- Identity cookie 服务 Web UI，Git 客户端仍需要独立 Basic Auth scheme，不能只依赖浏览器 cookie
- `HttpRuntime.UnloadAppDomain` 在 ASP.NET Core 没有等价用法
- 自写 SSH server 跑在 Web 进程内，需要生命周期、线程安全、算法兼容性和 host key 管理评估
- 完全纯托管重写 Git pack/wire protocol 风险很高；必须先用 `IGitTransportBackend` 隔离后端，再在测试保护网完整后逐步减少 `git.exe` helper 依赖
- 旧前端依赖很老，第一轮不要同时升级 UI 框架
- LibGit2Sharp 仍依赖按 RID 交付的 native binary；每次升级和新增发布 RID 都必须验证 restore、publish、加载和仓库操作回归
- 旧用户、旧密码、旧登录 token 不兼容，需要明确升级后重新创建账号或提供独立导入策略
- namespace、repository 和历史 alias 共用路由空间；系统保留 slug、大小写归一化、alias 到期释放和并发抢名必须由数据库唯一约束与事务共同保护
- Git HTTP 历史地址提示必须保持 protocol framing 和流式请求体，不能为了插入 warning 破坏 pkt-line/pack 或让 push 重放失败
- 企业目录同步可能误停用 owner、误合并同邮箱用户或在上游故障时批量删成员；必须使用稳定 external ID、隔离状态、对账和本地 break-glass owner
- 远程 mirror 涉及长期凭据、force/prune、远程限流、双向竞态和后台资源争用；默认单向、非破坏性、持久化状态且严格日志脱敏
- Issue/PR 的 Markdown、附件和 webhook target 都是新的输入面；必须防 XSS、危险 URL、SSRF、mention fan-out 和私有仓库元数据泄漏
- PR 行内评论若只保存当前行号会在新 push 后错位；必须保存 original base/head、path、side、line 和 hunk context，并显式处理 outdated thread
- mergeability 页面状态可能过期；真正 merge 前必须重新验证 branch tips、冲突、approval/check、保护规则和权限，并保护 ref 更新的并发一致性
- branch protection 必须在 Git HTTP、SSH 和 Web merge 三条入口复用同一策略，不能只靠页面按钮或 controller 判断
- 公开仓库推荐容易被刷 Star、重复访问、机器人下载和批量导入旧 commit 操纵；指标必须按成功事件聚合、时间衰减、异常降权和算法版本生成快照，并且永远先过滤非公开仓库
- 访问/下载统计可能演变为用户追踪或泄漏私有仓库热度；只保存最小日聚合，不记录原始 IP、凭据或可识别访问者，统计任务不得进入 Git protocol 热路径
- 帮助文档若在应用发布之外独立漂移，会让用户和模型得到与二进制不一致的说明；JekyllNet 构建、应用版本、文档 manifest、知识摄入和回滚必须绑定同一 release
- OCI Distribution 对路由、header、digest、media type、Range、断点续传和错误 envelope 很敏感；不能把普通 MVC/API 行为或 SonnetDB S3 风格端点直接当成兼容 registry
- Container Registry 的最终用户权限必须由 GitCandy token scope 和 namespace policy 执行；SonnetDB bucket policy 当前不能作为授权边界，bucket/服务凭据不得暴露给 Docker 客户端
- layer 内容寻址、manifest/tag metadata 和 SonnetDB 对象写入跨越存储事务；必须使用可恢复状态机、digest 校验、conditional create/原子 promote、reconciliation 和并发去重，避免发布半成品或无限对象版本
- Registry GC 若只按时间删除对象会破坏共享 layer；必须从 tag/manifest graph 做 mark-and-sweep，并保护活动 upload/pull、grace period、retention/legal hold 和一致备份恢复
- ⬜ M16 索引与检索必须严格复用仓库权限，避免私有仓库、未发布分支、代码片段或 Agent 会话被越权读取
- Roslyn、tree-sitter、libgit2 等代码智能依赖必须隔离在代码智能模块、worker 或工具中，避免拖慢 Web 启动和污染核心领域层
- embedding/vector provider 可能带来数据驻留、成本、部署和可复现性问题，必须按 provider 独立评估
- SonnetDB 文档向量索引必须记录 embedding 模型与维度并支持影子重建；禁止混用不同维度、重复摄入生成 HTML，或因摄入失败阻止核心 Git 服务启动
- MCP 不能通过自动反射无差别暴露 controller；API/tool coverage、scope、schema、分页、写入确认、幂等、撤销和审计必须进入 CI，Git/LFS/SSH/登录回调等协议端点应显式排除
- 大仓库索引、全文构建和向量生成必须后台化、可取消、可限流，不能阻塞 Git HTTP/SSH 协议路径

## 官方资料参考

- ASP.NET Core fundamentals and middleware: https://learn.microsoft.com/aspnet/core/fundamentals/
- ASP.NET Core MVC migration from ASP.NET MVC/Web API: https://learn.microsoft.com/aspnet/core/migration/fx-to-core/
- ASP.NET Core session migration notes: https://learn.microsoft.com/aspnet/core/migration/fx-to-core/areas/session
- ASP.NET Core Identity: https://learn.microsoft.com/aspnet/core/security/authentication/identity
- Scaffold Identity in ASP.NET Core projects: https://learn.microsoft.com/aspnet/core/security/authentication/scaffold-identity
- EF Core DbContext configuration: https://learn.microsoft.com/ef/core/dbcontext-configuration/
- Porting from EF6 to EF Core: https://learn.microsoft.com/ef/efcore-and-ef6/porting/
- Central Package Management: https://learn.microsoft.com/nuget/consume-packages/central-package-management
