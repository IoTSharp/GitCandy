# GitCandy 升级到 ASP.NET Core 10 MVC + EF Core 路线图

评估日期：2026-07-11
当前本机 SDK：.NET SDK 10.0.301，ASP.NET Core Runtime 10.0.9
目标方向：以 ASP.NET Core 10 MVC 为主线，数据库层迁移到 EF Core，用户认证采用 ASP.NET Core Identity 标准体系，保留 GitCandy 作为 Git 代码托管服务的核心行为，并默认采用单 GitCandy 进程承载 Web、Git HTTP、内置 SSH 和后台任务。

状态标记：

- ✅ 已完成：编号范围内的验收项已经通过，并有测试或文档记录。
- 🚧 进行中：已经开始实现，但验收尚未闭环。
- ⬜ 未完成：尚未开始，或没有可验证的完成记录。

编号规则：路线图编号按优先级和实施顺序排列；编号越小，越应优先推进。同一 Milestone 内按 `#` 任务编号顺序推进。当前没有验收记录的编号默认标为 ⬜。

校准说明：

- 第一阶段迁移主线（M0-M8）和 M9 迁移后改进池已经完成：ASP.NET Core 10 host、EF Core + Identity、MVC、Git Smart HTTP/SSH、仓库代码工作区、Git LFS、部署与运维均有可验证闭环；后续路线转为“代码托管产品协作能力”。
- 活动主线已具备 `GitCandyDbContext`、SQLite/SQL Server migration、ASP.NET Core Identity、MVC、Git HTTP/SSH、部署和迁移保护网；PostgreSQL/SonnetDB 仍是后续可选 provider 工作，不应与协作功能 schema 同批扩张。
- .NET 迁移主线以 `GitCandy.slnx` 为准；旧 `GitCandy.sln` 只作为 MVC5 行为参考，不能作为新 SDK-style 项目的构建入口或 CI 默认入口。
- 当前短期数据库策略为 SQLite-first：业务实现、Identity/领域 schema、登录/仓库列表等垂直切片先只以 SQLite 作为运行和验收 provider。M3 已补齐 SQL Server 独立 migration 与 SQL 生成审阅；SQL Server 真实部署验证、PostgreSQL/SonnetDB migration、schema 差异和部署兼容性等整体迁移跑通后再独立回补。

## 迁移起点画像（历史基线）

以下内容是 M0 冻结的迁移起点，用于解释后续设计约束；它不再代表活动主线现状。当前活动 solution 是 `GitCandy.slnx`，目标 `net10.0`，M0-M8 已完成，MVC5 项目只保留为行为参考。

迁移起点是一个单项目 ASP.NET MVC 5 / .NET Framework 4.5 Web 应用：

- 解决方案：`GitCandy.sln`
- Web 项目：`GitCandy/GitCandy.csproj`，老式 non-SDK-style csproj，`TargetFrameworkVersion` 为 `v4.5`
- 包管理：`packages.config`
- Web 栈：`System.Web.Mvc`、`System.Web.Razor`、`System.Web.Optimization`、`Web.config`、`Global.asax`
- 数据库：EF6.1 + SQLite EF6 Provider，另有 SQL Server 创建脚本
- Git 能力：活动 net10.0 主线使用 `LibGit2Sharp 0.31.0` 提供托管仓库能力，并受控调用 Git 官方 Smart HTTP/SSH transport helper；旧 MVC5 参考项目仍固定在 0.22.0
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
- `GitCandy.sln` 在迁移期只保留为旧 MVC5 项目的行为参考；不要把新 SDK-style 项目加入旧 solution。
- 正式迁移稳定后再单独决定是否删除旧 `GitCandy.sln`，避免 `.sln` 和 `.slnx` 长期并存导致工具自动选择错误 solution。

## 产品能力路线图补充

GitCandy 的产品目标不是完整复制 GitLab，而是在轻量自托管前提下满足程序员日常 Git 协作，并在迁移稳定后形成自己的 AI 代码库知识图谱能力。

### P0：必须可靠的 Git Server 内核

- Git HTTP/SSH clone、fetch、push，优先验证 Git protocol v2、认证失败、权限不足、仓库不存在和 service 不支持行为。
- 稳定仓库命名空间：规范 URL 为 `/{namespace}/{repository}[.git]`，namespace 可属于用户或团队；Web、Git HTTP 和 SSH 复用同一 resolver，并继续保护现有 `/git/{project}[.git]/{*verb}` 兼容入口。
- 改名兼容：用户/团队/仓库使用稳定内部 ID，历史名称默认保留 365 天且可配置；保留期内旧 Web/Git URL 继续工作并提示更新，旧名称不得被再次占用。
- Git LFS：至少实现 HTTP batch transfer、对象上传/下载、对象存在性检查和权限过滤；locking 可作为 P1。
- 仓库管理：创建、导入、重命名、删除、归档、默认分支、fork、mirror、public/private、owner/team/admin 权限。
- 代码浏览：tree、blob、raw、commit、diff、blame、branches、tags、archive。
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
- Packages/Artifacts 可列入迁移稳定后的独立产品阶段，不阻塞 Git 核心能力。

### P3：AI 代码库知识图谱

- 以 M16 为主线，先做只读 ingest、搜索、符号、调用关系、影响分析和 MCP tools。
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
| ✅ #000 | 迁移分支与旧项目冻结 | 已建立 `migration/aspnet-core-10`，保留当前 MVC5 项目为行为参考，并在 `docs/migration/m0-000-baseline.md` 记录工作区基线 |
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
| ✅ #012 | Solution 迁移 | 已新增 `GitCandy.slnx` 作为迁移主线；旧 `GitCandy.sln` 暂作行为参考；本地验证脚本和 CI workflow 均固定到 `.slnx` |
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

#### 🚧 M9 拆分

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
| ✅ #108 | Commit、Diff、Blame 与 Compare | 已实现 history/detail、parent diff、branch/tag、blame、compare 和异步流式 ZIP，具备 diff/archive 大小、取消和权限边界 |
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

目标：把用户、团队和仓库从可变名称迁移到稳定 ID，提供 `/{namespace}/{repository}[.git]` 规范 URL；用户或团队改名后，旧地址在可配置保留期内继续支持 Web、Git HTTP 和 SSH 访问，并给出更新地址提示。

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
| ✅ #110 | Namespace 与 URL 契约冻结 | 冻结 `/{namespace}/{repository}[.git]` Web/Git HTTP/SSH 形态、保留系统 slug、大小写规则、`.git` 规则和 legacy `/git/{project}` 映射策略 |
| ✅ #111 | 稳定 namespace/alias schema | 新增稳定 namespace、namespace alias、repository alias、rename event 模型；SQLite migration 可运行，SQL Server migration SQL 可审阅，唯一索引覆盖大小写和有效占用 |
| ✅ #112 | 统一 namespace/repository resolver | 所有 Web、Git HTTP、SSH、archive/cache/path 操作先解析稳定 ID，再做权限和根目录边界检查；禁止 controller 按字符串各自查询 |
| ✅ #113 | 原子改名与限频服务 | 用户/团队 slug 在滚动 7 天最多成功改 3 次；并发改名、大小写变体、系统保留名和 alias 抢占在事务/唯一约束下失败并审计 |
| ✅ #114 | Alias 生命周期与配置 | 默认 `AliasRetentionDays=365`，后台任务幂等处理到期、延长、释放和删除主体保留策略，管理页显示有效期和占用原因 |
| ✅ #115 | Web 重定向与提示 | 旧 Web URL 使用 `308` 到规范 URL，保留安全 query，规范页输出 canonical link 并显示一次更新书签提示；私有资源不泄漏存在性 |
| ✅ #116 | Git HTTP alias 兼容 | 带/不带 `.git` 的当前/旧 namespace/旧 repository 路径都能 clone/fetch/push；客户端看到更新 remote 提示，streaming、headers、401/403/404 和大 pack 行为不回归 |
| ✅ #117 | SSH alias 兼容 | 当前/旧路径复用 resolver、权限与 `IGitTransportBackend` 并通过真实 clone/fetch/push；OpenSSH adapter 与内置 listener 都通过 stderr 提示规范 remote，transport stdout 不受影响 |
| ✅ #118 | 改名管理与审计 UI | 用户/团队/仓库改名预览冲突、剩余次数、alias 到期时间和受影响 URL；灾难恢复 override 独立授权、要求理由和二次确认 |
| ✅ #119 | 兼容与并发验证报告 | SQLite/SQL Server、Web/Git/SSH、连续/并发改名、alias 到期、保留路由、legacy 与真实 Git 客户端矩阵均已覆盖，内置 SSH stderr 提示由真实客户端断言保护 |

验收：

- `https://host/team-or-user/repository` 与 `.git` 变体可访问页面并完成 Git HTTP clone/fetch/push，SSH 使用相同 namespace/repository 语义。
- 用户/团队连续 3 次改名成功，第 4 次在滚动 7 天内失败；失败尝试不消耗次数，并发请求不能突破限制。
- 默认 365 天内旧名称不可被任何用户或团队占用；到期释放、管理员延长和删除主体保留均有测试及审计。
- 历史地址在有效期内不只“跳页面”，还必须真实通过 Git HTTP/SSH fetch/push；客户端能看到规范 URL 提示。
- 公开路由变化同步 README、部署配置、CHANGES 和迁移/回滚说明；旧 `/git/{project}` 不能无说明删除。

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

### ⬜ Milestone 12：Pull Request、代码评审与合并

目标：在 GitCandy 内完成从分支变更提议、diff 评审、修改迭代、批准到安全合并的日常协作闭环。

定位：

- 第一垂直切片先支持同一仓库内 source/target branch；M10 稳定 namespace 和 `#106` fork 生命周期完成后再接跨 fork PR，不能用字符串仓库名维持跨仓库关系。
- PR 必须有 Conversation、Commits、Files changed 三个核心视图，并汇总 draft、conflict、review、check 和 mergeability 状态；Checks 的外部写入接口由 M13 提供。
- 普通评论与 review thread 分开建模。行内评论锚定 original base/head SHA、path、old/new side、line 和 hunk context；新 push 后可靠重映射，否则标记 `Outdated`。
- 合并前必须重新读取 source/target tip、冲突、approval/check、draft 和权限，并用 repository 级锁或等价乐观并发避免合并过期 head。
- 第一版提供 merge commit 和 squash；rebase merge、merge queue/train、批量 suggestion apply 和在线解决冲突延后。

#### ⬜ M12 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ⬜ #130 | PR schema、编号与引用快照 | 建立 PullRequest、source/target ref、original/current base/head SHA、merge result 和并发字段，复用 M11 WorkItemNumber/timeline；维护服务端只读 `refs/pull/{number}/head` 等内部 refs，拒绝客户端写入，分支删除后历史仍可读 |
| ⬜ #131 | 创建、Draft 与状态流转 | 同仓库 branch compare 后创建 PR，支持 draft/ready、edit、close/reopen；禁止 source=target、无差异、无读写权限和重复 open PR |
| ⬜ #132 | Conversation、Commits、Files changed | 展示 description/timeline、提交列表、merge-base diff、renames/binary/large diff 降级、分页/折叠和固定 commit 链接 |
| ⬜ #133 | 行内 Review threads | 支持单行/范围评论、reply、resolve/unresolve 和 outdated；新 push 后基于 hunk context 重映射，不能把评论静默挂到错误代码 |
| ⬜ #134 | Reviewer 与 Review 状态 | author/assignee/reviewer 分离，支持 request review、comment、approve、request changes、dismiss/re-request；本人批准和过期批准策略显式配置 |
| ⬜ #135 | Mergeability 与冲突检测 | 汇总 draft、source/target 变化、conflict、required approval/check 和 branch policy；状态异步刷新但合并时必须同步复核 |
| ⬜ #136 | Merge commit 与 Squash 服务 | 所有 ref 写入收敛到受控 merge service，生成可审阅 message，校验目标未变化，写入失败不留下半完成状态，并触发 hook/audit/index queue |
| ⬜ #137 | Issue 关联与自动关闭 | PR 显示 related Issue，merge 成功后按 closing keywords 幂等关闭目标 Issue；close/reopen 未合并 PR 不关闭 Issue |
| ⬜ #138 | Fork 与跨仓库 PR | 在 M10 namespace 和 #106 fork 生命周期上支持同 fork network 的 source repository，删除 fork/source branch 后保留审计和可诊断状态 |
| ⬜ #139 | PR/Review/Merge 集成验证 | 覆盖并发 push/merge、outdated thread、冲突、权限撤销、branch 删除、merge/squash、hook 失败、大 diff 和真实 Git fetch/push 后 ref 结果 |

验收：

- 开发者能 push branch、创建 draft PR、转 ready、请求 reviewer，并查看 Conversation/Commits/Files changed。
- reviewer 能留下稳定的行内 thread、request changes 和 approve；新 commit 后 thread 正确重映射或明确 outdated。
- merge/squash 只在最新 head、权限、approval/check 和 branch policy 满足时执行，并能用真实 Git 客户端 fetch 到正确结果。
- 私有仓库、fork、source branch 和 review 内容不通过 PR 列表、引用、通知或 diff API 越权暴露。

### ⬜ Milestone 13：合并治理、外部集成与发布基础

目标：让 Issue/PR 不只是页面功能，而能接入外部 CI、自动化和仓库治理，并形成可审计、可诊断的团队开发入口。

定位：

- 先做外部 CI 所需的 PAT、webhook 和 commit status/check API，不在本阶段自研 runner 或兼容 GitHub Actions workflow。
- branch protection 必须同时作用于 Git HTTP/SSH push 和 Web merge；视图隐藏按钮不能替代 pre-receive/update 侧服务端策略。
- webhook 使用版本化 event envelope、签名、delivery ID、重试和脱敏记录；接收方失败不能回滚已成功的 Git push/PR merge。
- 通知、审计、search 和 release 复用 repository 权限；私有资源不能进入无权用户的索引、payload 或失败诊断。

#### ⬜ M13 拆分

| 编号 | 主题 | 验收重点 |
| --- | --- | --- |
| ⬜ #140 | Personal Access Token 与 API auth | scoped PAT 只存 hash，支持创建时一次显示、到期、撤销、last-used、审计和 API/Git Basic 独立 scope；token 不进入 URL 或日志 |
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

### ⬜ Milestone 16：Agent Memory / Codebase Intelligence

目标：在 GitCandy 作为 Git 代码托管服务的基础上，落地面向 AI Agent 和开发者的代码库记忆与智能检索能力。系统应能摄入 Git 仓库、设计文档、ADR、CI 变更、代码评审记录和 Agent 会话，并通过 Web UI、HTTP API 和 MCP 查询“代码是什么、谁调用谁、为什么这么设计、改这里会影响哪里”。

定位：

- ⬜ M16 纳入 GitCandy 应用层产品路线，作为迁移稳定后的重点能力建设，并复用 M11-M13 产生的 Issue、PR、review、check 和审计数据。
- 实施顺序排在 ASP.NET Core/EF Core/Identity/Git HTTP/SSH 主迁移跑通之后，不阻塞前置迁移验收。
- 第一版优先做只读索引、检索、符号查询、调用关系和影响分析，再扩展 Agent 记忆写入、审计和 Explorer 管理界面。
- 存储优先落在 `GitCandy.Data` 的 EF Core schema 中，默认保留 SQLite 路径，同时保持 SQL Server 可行。
- 全文、向量和混合检索通过清晰的应用服务接口接入；具体 provider 和部署方式作为独立 PR 决策，不能污染 Git HTTP/SSH 的稳定性。

设计原则：

- GitCandy 产品能力优先。Code Memory schema、ingest、MCP tools、API 和 UI 都按 GitCandy 的仓库、用户、团队、权限和部署模型设计。
- 数据库能力抽象优先。若出现全文、向量、混合检索、审计、权限过滤等共性需求，应沉淀为通用服务接口和可替换 provider，而不是把某个搜索引擎或向量库写死在 controller 中。
- Core 轻依赖边界不破坏。`GitCandy.Core` 不直接引入 Roslyn、tree-sitter、libgit2 等大型运行时依赖；代码解析与 Git 扫描放在 `GitCandy.CodeIntelligence`、独立 ingest 工具、后台 worker 或扩展包中。
- 结构化优先，向量补充。文件、符号、调用边、引用边、commit、ADR、会话、工具调用都以结构化表、文档或边表落库；embedding 用于语义召回，不替代确定性的 symbol/edge 查询。
- 安全只读起步。MCP memory tools 第一版默认只读，按 repository/team/owner/branch 隔离；代码片段读取要有大小限制、路径白名单、权限复核和审计事件。
- 增量索引优先。索引任务以 repository、branch、commit、file hash 为边界增量执行，支持 `CancellationToken`，不能阻塞 clone/fetch/push 和 Web 登录。
- 权限一致。搜索、片段读取、调用关系、影响分析和 Agent memory 查询必须复用 GitCandy 的公开仓库、私有仓库、owner、team、administrator 权限语义。

数据模型草案：

| 类型 | 建议实体 | 用途 |
| --- | --- | --- |
| 仓库与文件 | `code_repositories`、`code_files`、`code_file_versions` | repo/project/branch/commit、路径、语言、hash、mtime、大小、license 元数据 |
| 符号与结构 | `code_symbols`、`code_symbol_locations` | namespace/type/method/property/endpoint/test 等符号定义与位置 |
| 关系边 | `code_edges` | calls/references/implements/tests/imports/routes_to/owns 等边 |
| 文本与向量 | `code_chunks` | 代码块、注释、README、docs、embedding、BM25/Hybrid Search 元数据 |
| Git 演化 | `code_commits`、`code_changes` | commit 时间线、作者、文件变更、热点模块、变更趋势 |
| 决策与记忆 | `code_decisions`、`agent_memories`、`agent_tool_events` | ADR、设计决策、review 结论、Agent 会话摘要和工具调用审计 |

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

验收：

- 能对 GitCandy 自身仓库完成首次索引和增量索引。
- 私有仓库内容不会被无权限用户或 Agent tool 查询到。
- `code_search`、`symbol_search`、`code_callers`、`code_callees`、`code_impact`、`code_snippet` 至少有 API 或 MCP smoke tests。
- 代码片段读取有路径归一化、仓库根目录边界检查、长度限制和审计记录。
- 大仓库索引不会影响 Git HTTP clone/fetch/push 的流式行为。
- SQLite 默认路径可运行；SQL Server schema 或 migration SQL 可生成。

## 推荐里程碑

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
| ⬜ M12 | Pull Request 与 Review | draft、commits、files changed、行内 review、approval、merge/squash 和并发/权限闭环 |
| ⬜ M13 | 合并治理与集成 | PAT、webhook、status/check、branch protection、CODEOWNERS、审计、release 和外部 CI 闭环 |
| ⬜ M14 | 企业组织与身份 | 四级团队角色、Microsoft Entra ID、企业微信、飞书、钉钉登录/目录同步和管理员连接界面闭环 |
| ⬜ M15 | 远程仓库连接 | GitHub/GitLab/Gitee 账号绑定、导入、Pull/Push mirror、持久化 job、webhook 和故障诊断闭环 |
| ⬜ M16 | 代码智能产品能力 | Agent Memory / Codebase Intelligence 完成 schema、ingest、MCP/API、Hybrid Search、Explorer、IDE 接入样例和规模验证 |

## 首轮建议实施顺序

建议第一批实际代码工作只做这几件事：

| 编号 | 工作项 |
| --- | --- |
| ✅ #000 到 ✅ #009 | 增加测试数据、行为清单、迁移分支和验证模板 |
| ✅ #010 | 新建 `src/GitCandy` ASP.NET Core 10 MVC 主程序项目 |
| ✅ #011 到 ✅ #014 | 引入 `Directory.Build.props`、`Directory.Packages.props`、`global.json`、`.slnx` |
| ✅ #015 到 ✅ #019 | 建立新 `Program.cs`、标准 pipeline、认证/授权占位、空路由、`System.Web` 门禁和空壳构建验证 |
| ✅ #030 到 ✅ #039 | 建立 EF Core `GitCandyDbContext` + Identity/领域 schema，完成 SQLite 新库、存储/权限 smoke tests 和 SQL Server migration SQL 闭环 |
| ✅ #040 到 ✅ #049 | 迁移登录、当前用户和权限服务，打通 Web 登录/登出 |
| ✅ #050 到 ✅ #059 | 已迁移 `Repository/Index` 和其余 MVC Controllers/Razor Views |
| ✅ #060 到 ✅ #069 | 已迁移 Git Smart HTTP，并验证 `git clone/fetch/push` 与大 pack streaming |
| ✅ #070 到 ✅ #079 | 已迁移内置 SSH hosted service，并验证 SSH clone/fetch/push 和 graceful shutdown |

这样能尽快发现真正的风险点：Identity/领域模型关系、ASP.NET Core routing 差异、Git streaming 差异，而不是在 40 多个 view 迁完后才发现协议层不通。

当前校准后的短线顺序：

1. ✅ M1 已完成：`src/GitCandy` 空壳、标准 ASP.NET Core MVC pipeline、兼容占位路由、`System.Web` 门禁和 `.slnx` 构建验证已闭环。
2. ✅ M3 已完成：以 SQLite 为短期运行 provider，Identity/领域 migration、权限与 Identity store smoke tests、SQL Server migration SQL 已闭环。
3. ✅ M4 已完成：Identity cookie、独立 Git Basic scheme、当前用户、resource authorization handlers、Session 收敛和行为测试已闭环。
4. ✅ M5 已完成：MVC controllers、Razor Views、静态资源和主要页面 smoke tests 已闭环。
5. ✅ M6 已完成：Smart HTTP endpoint、独立 Basic 授权、受控 Git backend、clone/fetch/push 和大 pack streaming 已闭环。
6. ✅ M7 已完成：内置 SSH、统一 Git backend、SSH clone/fetch/push 和 graceful shutdown 已闭环。
7. ✅ M8 已完成：部署、migration SQL、备份/恢复、回滚和发布文档已闭环。
8. ✅ M9 已完成：`#106-#108` 仓库生命周期/代码工作区与独立 `#109` Git LFS 均已有真实客户端和边界测试。
9. ✅ M10 已完成：稳定 namespace/alias、改名限频、历史地址和 Web/Git HTTP/SSH 提示均已闭环；随后按 M11 Issue -> M12 PR/Review -> M13 合并治理与外部 CI 形成研发协作主链。
10. 企业身份、远程 mirror 和 Code Intelligence 顺延到 M14-M16，复用前面形成的稳定 repository ID、PAT、通知、审计、Issue/PR/review/check 数据。

⬜ M16 的代码智能能力从 ⬜ #170 schema 和 ⬜ #171 ingest 垂直切片开始推进；其 repository/team/owner 外键、代码片段权限和 review 数据必须直接复用 M10-M15 的稳定 ID、权限与连接边界。

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
- ⬜ M16 索引与检索必须严格复用仓库权限，避免私有仓库、未发布分支、代码片段或 Agent 会话被越权读取
- Roslyn、tree-sitter、libgit2 等代码智能依赖必须隔离在代码智能模块、worker 或工具中，避免拖慢 Web 启动和污染核心领域层
- embedding/vector provider 可能带来数据驻留、成本、部署和可复现性问题，必须按 provider 独立评估
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
