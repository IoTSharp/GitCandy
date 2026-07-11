# 企业仓库命名空间、身份与镜像能力规划

评估日期：2026-07-11

本文补充 `ROADMAP.md` 中的 M10、M14 和 M15，明确 GitCandy 面向企业内部 Git 管理时的仓库 URL、名称变更、团队角色、企业身份和远程仓库同步边界。本文是产品与架构规划，不表示对应能力已经实现。

## 1. 产品决策摘要

- 新的规范仓库 URL 为 `https://{host}/{namespace}/{repository}`，`namespace` 可以属于用户或团队；Web 和 Git Smart HTTP 同时接受带 `.git` 与不带 `.git` 的仓库路径。
- 用户、团队、仓库都使用稳定内部 ID。URL slug 只是可变名称，权限、审计、任务和外部绑定不得以 slug 作为外键。
- 用户和团队共用一个大小写不敏感的全局命名空间；系统路由、当前名称和保留期内的历史别名都不能被再次占用。
- 用户或团队 URL slug 在滚动 7 天窗口内最多成功修改 3 次。失败的改名不计数；紧急恢复只能由系统管理员通过单独的审计流程执行。
- 历史 namespace/repository alias 的默认保留期为 365 天并可配置。保留期内，旧 Web URL、Git HTTP URL 和 SSH URL都必须继续工作并提示迁移到规范 URL。
- 团队角色固定为 `TeamOwner`（最高管理员/团队所有者）、`Leader`（组长）、`DeputyLeader`（副组长）和 `Member`（成员）。团队至少保留一名 `TeamOwner`。
- 企业身份分为登录联邦和目录供应两个平面：OIDC/SAML 解决登录，SCIM 或厂商通讯录 API 解决用户、组织和成员生命周期。二者不得混成一套不可替换的 provider 代码。
- 远程仓库同步第一阶段只支持明确的单向 `Pull` 或 `Push` mirror。双向 mirror 因竞态、历史改写和冲突风险默认禁用，待单向能力和冲突保护通过后再单独试验。
- GitHub、GitLab、Gitee 连接既支持用户绑定账号，也支持管理员配置的组织/服务账号。凭据必须加密或引用外部 secret store，不得进入日志、命令行参数或普通配置导出。

## 2. 仓库 URL 与路由契约

### 2.1 规范 URL

| 场景 | URL 形态 | 预期行为 |
| --- | --- | --- |
| Web 仓库页 | `/{namespace}/{repository}` | 展示仓库主页并输出 canonical link |
| Web 兼容页 | `/{namespace}/{repository}.git` | 重定向到不带 `.git` 的规范 Web 页面 |
| Git HTTP | `/{namespace}/{repository}[.git]/info/refs` | Smart HTTP service advertisement |
| Git HTTP RPC | `/{namespace}/{repository}[.git]/git-upload-pack` | clone/fetch 流式响应 |
| Git HTTP RPC | `/{namespace}/{repository}[.git]/git-receive-pack` | push 流式请求/响应 |
| SSH | `ssh://git@{host}:{port}/{namespace}/{repository}[.git]` | clone/fetch/push |

路由解析顺序必须先识别系统保留路由和 Git protocol verb，再解析 namespace/repository。`account`、`team`、`repository`、`setting`、`git`、`health`、`api`、`assets` 等应用路由应进入可版本化的保留名称表，避免新增 controller 后抢占已有仓库 URL。

现有 `/git/{project}[.git]/{*verb}` 和 MVC controller 路由不能直接删除。迁移时为已有仓库生成明确的 legacy route mapping；当不同 namespace 下出现同名仓库时，不允许进行猜测，应依赖已有映射或返回可诊断冲突。

### 2.2 名称与显示名称

- `DisplayName` 用于界面展示，修改它不改变 URL，也不消耗改名次数。
- `Slug` 用于 URL，修改它会创建历史别名、写入 rename event 并消耗改名次数。
- slug 比较使用明确的大小写不敏感归一化规则；保存原始大小写只用于展示，不能产生两个逻辑相同的名称。
- namespace 由用户或团队拥有，但同一时刻只能有一种 owner，不能同时出现同名用户 namespace 和团队 namespace。

## 3. 改名、别名和名称占用

### 3.1 数据边界

建议以稳定 ID 建模，而不是把旧名称串联到新名称：

| 实体 | 关键字段 | 说明 |
| --- | --- | --- |
| `Namespaces` | `Id`、`OwnerType`、`OwnerId`、`Slug`、`NormalizedSlug` | 用户/团队统一命名空间和全局唯一占用 |
| `NamespaceAliases` | `NamespaceId`、`Slug`、`NormalizedSlug`、`CreatedAt`、`ExpiresAt` | 旧用户/团队 URL 直接指向稳定 namespace ID |
| `RepositoryAliases` | `RepositoryId`、`NamespaceId`、`Slug`、`NormalizedSlug`、`ExpiresAt` | 旧仓库名在对应 namespace 内保留 |
| `RenameEvents` | `SubjectType`、`SubjectId`、`ActorUserId`、`OldSlug`、`NewSlug`、`OccurredAt` | 限频、审计和管理员恢复依据 |

别名不得形成 alias-to-alias 链。每次解析都直接得到当前稳定实体；这样连续改名三次仍只有一次数据库定位，不需要递归追踪。

### 3.2 保留与释放

- 默认配置：`AliasRetentionDays=365`、`RenameLimit=3`、`RenameWindowDays=7`。
- 保留期从改名事务提交时开始计算；时间统一存 UTC。
- 当前名称、未过期 namespace alias、未过期 repository alias 和保留系统名称共同参与占用检查。
- 到期清理必须是幂等后台任务。过期前在管理页显示到期时间；释放、延长或提前撤销都要写审计日志。
- 新名称冲突时返回明确原因，但对无权限用户不能泄漏私有用户、团队或仓库的详细信息。
- 用户/团队删除、停用或从上游目录移除时，名称不能立即释放；至少遵守剩余别名保留期和组织的数据保留策略。

改名限频按主体计算成功事件：任意滚动 7 天内最多 3 次。并发改名必须在数据库事务和唯一索引保护下完成，不能靠应用层先查后写。普通管理员 UI 不提供绕过；灾难恢复使用单独命令或受控页面，要求理由、二次确认和完整审计。

### 3.3 兼容访问与提示

| 通道 | 旧地址行为 | 用户提示 |
| --- | --- | --- |
| Web GET | `308 Permanent Redirect` 到规范 URL，保留安全 query 参数 | 新页面显示一次“地址已变更，请更新书签/remote”提示 |
| Git HTTP discovery | 同 host `308` 到规范 `info/refs` | 真实 Git 客户端必须显示 redirect/update-remote warning |
| Git HTTP RPC | 正常流程应已使用 discovery 后的新基址；旧 RPC 仍需安全解析，不能丢请求体 | 使用协议允许的提示机制；不得往 pkt-line/pack 数据中直接插文本 |
| SSH | 内部解析 alias，继续执行相同权限检查和 transport backend | 通过独立 stderr channel 输出规范 remote URL |
| API | 返回规范资源 ID/URL；是否跟随 redirect 由 API 版本明确规定 | `Location` 和可机器读取的 canonical 字段 |

提示不能取代兼容性。别名访问必须复用当前仓库的认证、权限、路径边界、审计、hook 和限流逻辑，不能复制一套旧路径授权规则。Web、HTTP Git 和 SSH 的 alias hit 都记录脱敏审计事件，便于管理员找到仍未更新的客户端。

## 4. 团队角色模型

团队角色和仓库权限是两个维度。团队角色决定谁能管理团队；团队对仓库的 read/write/admin 权限仍由仓库授权关系决定，普通成员不能因为加入团队就自动获得所有仓库权限。

| 操作 | TeamOwner | Leader | DeputyLeader | Member |
| --- | --- | --- | --- | --- |
| 修改团队 slug、转移或删除团队 | 是 | 否 | 否 | 否 |
| 管理企业身份连接和目录映射 | 是 | 只读 | 否 | 否 |
| 任命/移除 TeamOwner | 是，且不能移除最后一人 | 否 | 否 | 否 |
| 管理 Leader | 是 | 否 | 否 | 否 |
| 管理 DeputyLeader/Member | 是 | 是 | Member | 否 |
| 创建团队仓库、管理团队级策略 | 是 | 是 | 按显式委派 | 否 |
| 管理仓库成员和日常协作 | 是 | 是 | 按仓库权限 | 按仓库权限 |

目录同步默认只能创建 `Member`。允许管理员配置外部组到 `Leader`/`DeputyLeader` 的映射，但不得由上游目录自动产生或删除最后一个 `TeamOwner`；必须保留本地 break-glass owner。

## 5. 企业身份与组织连接

### 5.1 统一连接模型

企业连接建议分成以下接口，provider 只实现自己支持的部分：

- `IExternalAuthenticationProvider`：登录跳转、callback、账号绑定、解绑和 claims 映射。
- `IEnterpriseDirectoryProvider`：用户、部门/组、成员关系的全量与增量同步。
- `IEnterpriseProvisioningService`：幂等 upsert、冲突处理、停用、恢复和角色映射。
- `IExternalIdentitySecretStore`：凭据引用、轮换和读取；业务表只保存 secret reference。

每个连接记录 tenant/corp 标识、provider 类型、显示名称、启用状态、认证方式、同步游标、最近成功/失败、下一次运行和健康状态。外部主体以 `(ConnectionId, ExternalObjectType, ExternalObjectId)` 唯一标识，不能用可变邮箱、手机号或显示名作为主键。

### 5.2 Provider 路线

| Provider | 第一阶段 | 后续目录能力 | 关键边界 |
| --- | --- | --- | --- |
| Microsoft Entra ID | OIDC 登录；管理员绑定 tenant | SCIM 2.0 Users/Groups provisioning，或受控 Graph 同步适配 | 优先标准 OIDC + SCIM；支持 `active=false` 停用和 group mapping |
| 企业微信 | 企业 OAuth 登录并取得稳定 UserId | 通讯录部门、成员、变更回调/增量同步 | CorpId + UserId 是身份键；敏感字段按最小权限获取 |
| 飞书 | OAuth 登录并绑定 tenant | 通讯录用户、部门和变更事件/增量同步 | 区分 tenant/access token 与 user token；处理 open_id/user_id 等 ID 类型 |
| 钉钉 | OAuth 登录并绑定组织 | 通讯录用户、部门和事件订阅/增量同步 | CorpId/unionId/userId 映射要有明确作用域 |

上游停用默认立即阻止新登录，并撤销 GitCandy Web sessions；PAT、SSH key、Git Basic 和外部连接 token 的撤销策略必须单独配置并可审计。不得因为一次临时 API 故障批量删除成员；目录缺失先进入隔离/待确认状态，只有可信的显式 deprovision 事件或连续完整同步确认后才停用。

### 5.3 管理界面

管理员需要以下可操作页面，而不只是 `appsettings.json`：

- 连接列表：provider、tenant、启用状态、最近同步、健康状态和告警。
- 新建/编辑连接：callback URL、scope、secret reference、同步周期、用户匹配策略和默认团队角色。
- “测试连接”和“预览同步”：显示将新增、更新、停用和冲突的数量，不显示 token。
- 映射规则：外部组/部门到 GitCandy team 和角色；冲突必须人工确认。
- 同步作业：立即运行、暂停、重试、查看脱敏错误和审计事件。
- break-glass：至少一个不依赖外部 IdP 的本地系统管理员，防止 IdP 配置错误锁死实例。

## 6. GitHub、GitLab、Gitee 账号与仓库连接

### 6.1 账号绑定

仓库连接与“用 GitHub/GitLab 登录 GitCandy”是不同用途。连接记录必须明确主体和授权范围：

- 用户连接：用户通过 OAuth/OAuth PKCE 绑定个人账号，仅能选择自己有权访问的远程仓库。
- 组织连接：系统管理员绑定 GitHub App、GitLab application/service account 或 Gitee 企业/服务账号，供多个仓库复用。
- 临时 PAT：只作为 provider 不支持更安全授权时的兼容方式，必须限制 scope、加密、可轮换并显示到期时间。
- GitHub 自动化优先 GitHub App 的细粒度权限和短期 token，而不是长期个人 PAT。

管理 UI 提供 provider 启用、OAuth client 配置、callback、scope、连接测试、token 轮换/撤销、可访问仓库发现和审计。普通用户只能管理自己的连接；管理员不能读取明文 token。

### 6.2 Remote 与 mirror 模型

| 字段 | 说明 |
| --- | --- |
| `Direction` | `Pull` 或 `Push`；第一阶段不能同时选 |
| `Authority` | 明确 GitCandy 或 remote 哪一侧是权威源 |
| `RemoteRepositoryId` | provider 稳定 repository ID，URL 只作为可变属性 |
| `RefFilter` | all refs、protected branches 或显式 allowlist/regex |
| `Schedule` | 周期、时区、启用状态；支持 webhook 加速但仍周期对账 |
| `DivergencePolicy` | 默认停止并告警；显式选择 keep-divergent 或强制覆盖 |
| `PrunePolicy` | 是否删除目标端已不存在的 refs，默认关闭 |
| `LastObservedRemoteHead` | 幂等、差异判断和诊断依据 |

第一阶段只同步 Git commits、branches 和 tags。Issues、PR/MR、Wiki、Releases、Actions/CI、Packages 和 Git LFS 对象不隐式跟随；每项都需要独立 API、权限和数据一致性设计。界面必须明确告诉管理员“仓库镜像不等于项目数据迁移”。

### 6.3 作业执行

- `Pull` mirror：远程为权威源，GitCandy 仓库默认只读，定时 fetch 并按策略更新 refs。
- `Push` mirror：GitCandy 为权威源，`post-receive` 只入队，不在用户 push 请求中等待远程网络；后台 job 合并重复 ref 更新。
- 手动“立即同步”与计划任务走同一 job pipeline，不能绕过并发限制、审计和权限。
- job 状态持久化到 EF Core；Quartz 只负责唤醒。即使单进程重启，也不能丢失待执行同步或失败原因。
- 每个 remote 串行执行，实例级限制并发；使用指数退避、随机抖动、超时、`CancellationToken`、租约和最大重试次数。
- webhook 只作为低延迟触发器，必须验签、去重并通过后续周期对账弥补漏事件。
- provider 限流、token 到期、远程 404/403、host key 变化、non-fast-forward、保护分支拒绝和网络错误必须分类，不得统一显示“同步失败”。
- 外部 Git 命令只能进入 `IRemoteRepositorySyncBackend`（或等价单一抽象），使用 `ProcessStartInfo.ArgumentList`、受控环境变量/credential helper 和流式 I/O。token 不得出现在 remote URL、进程参数或日志。

强制覆盖必须默认关闭。启用时要求 repository owner/administrator 二次确认，展示可能丢失的 refs，限制目标和分支范围，并写入审计。双向同步即使后续提供，也应标记为实验性并要求双方保护分支、webhook 降延迟和明确冲突恢复手册。

## 7. 竞品能力对照

| 能力 | GitHub | GitLab | Gitea/Forgejo | Gitee | GitCandy 取舍 |
| --- | --- | --- | --- | --- | --- |
| 仓库改名 | Web 与 clone/fetch/push 旧地址继续重定向；复用旧仓库名会破坏 redirect | 用户、组、项目路径变化会重定向，Git 客户端收到更新 remote 提示；旧路径被再次占用会失效 | 轻量 self-hosted 体验，具体 alias 生命周期需实现时核对版本 | 国内用户熟悉的仓库导入/同步体验 | 旧 namespace/repository alias 默认保留 365 天且期间禁止复用，稳定性强于“可被覆盖的 redirect” |
| 用户改名占用 | 旧 username 可被其他人立即认领，个人 profile 不保证跳转 | namespace redirect 依赖旧路径未被占用 | 实例管理员可控 | 企业实例强调组织管理 | 企业内网不能接受立即抢注；统一 namespace claim + 限频 + 保留期 |
| 团队/组织角色 | Owner/member、team maintainer、repository roles，企业版有自定义组织角色 | Guest/Reporter/Developer/Maintainer/Owner 等分级和继承 | Owner team、admin/general team、按 unit 权限 | 国内企业组织和项目协作 | 先落地 Owner/Leader/Deputy/Member 四级管理角色，仓库权限独立，不一开始复制复杂自定义角色系统 |
| 企业身份 | SAML/SCIM、Enterprise Managed Users；SCIM 负责自动增删成员 | SAML/SCIM，可同步 group membership | LDAP/OAuth 等自托管集成取向 | 国内企业身份生态 | 标准 OIDC/SAML + SCIM 为核心，企业微信/飞书/钉钉使用 provider adapter；停用和 break-glass 是必测项 |
| Pull mirror | 没有与 GitLab 相同的通用计划 pull mirror 管理面，通常使用 import/API/automation | 原生 pull mirror、分支过滤、手动触发和状态 | 创建 mirror 后周期 pull，可手动同步 | 重点对照 GitHub 导入/同步工作流，API 细节在 connector spike 核实 | M15 首批能力，remote 权威、GitCandy 只读、周期对账和可诊断状态 |
| Push mirror | 通常由外部自动化推送到 GitHub | 原生 push mirror、保护分支过滤、divergent ref 策略 | 周期/推送触发的 push mirror；文档明确可能 force overwrite | 重点对照国内网络和企业账号授权体验 | post-receive 异步入队，默认 non-destructive，强制覆盖需显式审计确认 |
| 双向 mirror | 依赖外部自动化 | 支持但官方明确警告竞态和冲突 | 通常通过组合 pull/push，风险由管理员承担 | 实施前验证产品/API 行为 | 不进入第一阶段；只在单向镜像稳定后做实验性切片 |
| 自动化凭据 | GitHub App 优先，细粒度权限和短期 token | OAuth/PAT/deploy key 等 | PAT/账号或 SSH，能力因方向而异 | OAuth v5/企业授权 | provider-neutral connection + secret reference；GitHub 优先 App，避免长期个人 token |

值得直接吸收的能力是：GitHub 的改名兼容和 GitHub App 最小权限、GitLab 的路径迁移提示与 mirror 策略、Gitea 的轻量部署和“立即同步”操作、Gitee 的国内企业账号和网络场景。明确不吸收的是会被名称复用破坏的无限 redirect、默认 force push、把双向 mirror 描述成无冲突同步，以及为了企业集成拆出大量必须独立部署的服务。

## 8. 实施顺序与验收重点

1. M10 先建立稳定 namespace ID、统一 resolver、alias 和真实 Git 客户端兼容测试。没有这层，企业目录改名和远程 repository rename 都会继续扩散字符串外键。
2. M11-M13 先建立 Issue、PR/Review、PAT、通知、审计、webhook 和 branch protection，使企业身份接入后能直接复用完整协作权限与审计边界。
3. M14 在 namespace 基础上建立团队四级角色、connection abstraction，再先交付 Microsoft Entra OIDC + SCIM 垂直切片，随后接企业微信、飞书、钉钉 adapter。
4. M15 先做通用 remote/account/secret/job 模型和 GitHub 连接，再扩 GitLab、Gitee；Pull 与 Push 分两个垂直切片，双向最后单独评估。
5. M16 Code Intelligence 继续使用稳定 repository ID 和新权限模型，不自己保存 namespace slug 或外部账号 token。

跨里程碑必测：

- Web、Git HTTP、SSH 对当前 URL、历史 namespace alias、历史 repository alias 均能 clone/fetch/push 或正确访问。
- 改名第 1-3 次成功，第 4 次在同一滚动 7 天窗口失败；并发抢名和大小写变体不能突破唯一约束。
- alias 到期、延长、释放和再次占用行为可重复验证，SQLite 与 SQL Server migration SQL 都明确表达索引与时间字段。
- 最后一个 TeamOwner 不能被删除、降级或被目录同步停用；角色提升和外部组映射有审计。
- 企业连接 token 不出现在日志、数据库明文字段、错误页面、命令行和配置导出。
- Pull mirror 不允许本地写入造成静默分叉；Push mirror 失败不回滚已经成功的本地 push。
- GitHub/GitLab/Gitee token 撤销、限流、仓库改名、权限丢失和远程删除都有明确状态与恢复操作。
- 大仓库镜像仍保持流式 I/O，后台并发不会饿死 Git HTTP/SSH 请求。

## 9. 本阶段非目标

- 不在同一变更中实现 namespace schema、UI redesign、企业目录和 mirror backend。
- 不承诺同步 Issues、PR/MR、Wiki、CI、Packages、Releases 或 LFS。
- 不把 GitHub/GitLab/Gitee 账号 token 当作 GitCandy Web 登录 cookie。
- 不让目录 provider 直接写 EF entities 或授予系统管理员/最后一个 TeamOwner。
- 不在第一阶段开放任意自定义角色表达式、双向 mirror 或跨实例分布式 scheduler。

## 10. 官方资料

- GitHub repository rename redirects: https://docs.github.com/en/repositories/creating-and-managing-repositories/renaming-a-repository
- GitHub username changes: https://docs.github.com/en/account-and-profile/concepts/username-changes
- GitHub organization roles: https://docs.github.com/en/organizations/managing-peoples-access-to-your-organization-with-roles/roles-in-an-organization
- GitHub OAuth/GitHub App guidance: https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps
- GitHub organization SCIM: https://docs.github.com/en/enterprise-cloud@latest/organizations/managing-saml-single-sign-on-for-your-organization/about-scim-for-organizations
- GitLab repository path changes: https://docs.gitlab.com/user/project/repository/#repository-path-changes
- GitLab roles and permissions: https://docs.gitlab.com/user/permissions/
- GitLab repository mirroring: https://docs.gitlab.com/user/project/repository/mirror/
- GitLab bidirectional mirroring risks: https://docs.gitlab.com/user/project/repository/mirror/bidirectional/
- GitLab SCIM: https://docs.gitlab.com/user/group/saml_sso/scim_setup/
- Gitea repository mirror: https://docs.gitea.com/usage/repo-mirror
- Gitea organization permissions: https://docs.gitea.com/usage/permissions
- Microsoft Entra SCIM endpoint guidance: https://learn.microsoft.com/entra/identity/app-provisioning/use-scim-to-provision-users-and-groups
- 企业微信 OAuth: https://developer.work.weixin.qq.com/document/path/91022
- 飞书用户身份 API: https://open.feishu.cn/document/server-docs/authentication-management/login-state-management/get
- 钉钉用户身份凭证: https://open.dingtalk.com/document/development/obtain-identity-credentials
- Gitee OAuth v5: https://gitee.com/api/v5/oauth_doc
