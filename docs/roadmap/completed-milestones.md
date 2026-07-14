# GitCandy 已完成里程碑索引

更新日期：2026-07-14

本文件保存已经退出活动路线图的里程碑、验收结论和后续文档入口。任务的原始目标、拆分表和当时的验收口径完整保存在 [2026-07-13 路线图快照](roadmap-archive-2026-07-13.md)；根目录 [ROADMAP.md](../../ROADMAP.md) 只维护尚未完成的工作。

## 完成口径

“已完成”表示对应任务在退出活动路线图时满足以下条件：

- 实现、migration、配置或文档产物已经存在于活动主线；
- 对应自动化测试或专项验收记录存在；
- 当前 `master` 能完成 Release 构建和完整测试；
- 没有已登记、会推翻该里程碑完成结论的功能缺陷。

这不表示软件绝对不存在未知缺陷，也不表示未来功能不会扩展这些能力。发现回归时应创建新的修复任务，不把已经完成的历史里程碑重新伪装成进行中。

## 本次退出活动路线图的验证

2026-07-14 在 Windows / .NET SDK 10.0.301 上重新执行：

```powershell
dotnet build GitCandy.slnx --configuration Release -p:UseArtifactsOutput=true -p:ArtifactsPath=.artifacts/m14-verified
dotnet test GitCandy.slnx --configuration Release --no-build --no-restore -p:UseArtifactsOutput=true -p:ArtifactsPath=.artifacts/m14-verified
```

结果：

- Release 构建成功，0 warning、0 error；
- `GitCandy.Data.Tests`：91/91 通过；
- `GitCandy.Tests`：139/139 通过；
- 合计 230/230 通过，无跳过、无失败；
- SQLite、SQL Server、SonnetDB 均确认当前模型与最后 migration 一致。
- 隔离 SQLite host 的团队成员和企业连接桌面/390px 浏览器 smoke 通过，控制台 0 error、0 warning。

M12.6 的远程生产部署、真实外部 DNS/TLS、Web/Git HTTP/SSH/LFS、生产备份恢复和镜像回滚已于 2026-07-13 执行，证据见 [M12.6 生产验收记录](../migration/m12-6-sonnetdb-production-acceptance.md)。本次仍没有重新执行跨操作系统 CI；该证据以现有 workflow 和 CI 记录为准。

## 已完成产品基线

| 范围 | 已完成能力 |
| --- | --- |
| 平台 | ASP.NET Core 10 MVC、SDK-style 项目、`.slnx`、central package management、nullable、warnings-as-errors |
| 数据 | EF Core + ASP.NET Core Identity、SQLite 默认 provider、SQL Server migration 验证、SonnetDB 专用生产 profile、provider-neutral 数据层 |
| 认证与权限 | Identity cookie、独立 Git Basic/SCIM Bearer、2FA、恢复码、可选 OIDC、四级团队治理、企业身份联邦与同步停用 |
| Git 服务 | Smart HTTP、内置 SSH、clone/fetch/push、protocol v2、大 pack 流式传输、统一 transport backend |
| 仓库 | 稳定 namespace、创建/导入/fork/删除、tree/blob/raw/history/diff/blame/compare/archive、Branches/Tags/Contributors、LFS basic transfer |
| 协作 | Issues、评论、Markdown、labels、milestones、assignees、PR、跨 fork、行内 review、approval、merge/squash；私人 `/me`、Todo、统一通知、Feed、公开个人页、Stars、仓库发现；M13 PAT/webhook/check/gate/CODEOWNERS/通知/审计/Release/search |
| 运维 | Docker Compose、Linux systemd、Windows Service、TLS proxy、health checks、OpenTelemetry、migration、`gitcandy.com` 生产部署和恢复演练 |

## 已完成里程碑

| 里程碑 | 编号 | 完成结论 | 主要文档 |
| --- | --- | --- | --- |
| M0 基线冻结与迁移保护网 | `#000-#009` | 行为清单、样例数据、权限/MVC/Git 验证入口和 PR 安全模板完成 | [M0 文档目录](../migration/)；原始拆分见历史快照 |
| M1 ASP.NET Core 10 MVC 外壳 | `#010-#019` | `net10.0` host、`.slnx`、构建属性、CPM、MVC pipeline 和 `System.Web` 门禁完成 | [solution migration](../migration/m1-012-solution-migration.md)、[build validation](../migration/m1-019-shell-build-validation.md) |
| M2 横切基础设施 | `#020-#029` | 配置、路径、日志、缓存、DI、Quartz、SSH 生命周期、profiler 和诊断完成 | [M2 migration records](../migration/) |
| M3 EF Core 数据层 | `#030-#039` | Identity/domain schema、SQLite/SQL Server migration、约束、生命周期和 smoke tests 完成 | [migration strategy](../migration/m3-038-migration-strategy.md)、[data smoke tests](../migration/m3-039-data-layer-smoke-tests.md) |
| M4 认证、授权和会话 | `#040-#049` | Identity cookie、Git Basic、权限 handlers、无 Session 路径和安全行为测试完成 | [authentication record](../migration/m4-040-049-authentication-authorization-session.md) |
| M5 MVC/Razor 迁移 | `#050-#059` | Controllers、Razor、资源、本地化、静态资源和页面 smoke 完成 | [MVC migration record](../migration/m5-050-059-mvc-controllers-and-razor-views.md) |
| M6 Git Smart HTTP | `#060-#069` | 流式协议、headers、认证、路径安全、clone/fetch/push 和大 pack 验证完成 | [Git HTTP record](../migration/m6-060-069-git-smart-http.md) |
| M7 SSH 与后台任务 | `#070-#079` | 同进程 SSH、DI/权限/backend 复用、Quartz 取消和真实 SSH Git 验证完成 | [SSH/background record](../migration/m7-070-079-ssh-and-background.md) |
| M8 部署、运维和文档 | `#080-#088` | Compose/systemd/Windows Service、health、migration、备份、恢复和回滚完成 | [deployment record](../migration/m8-080-088-deployment-operations.md)、[deployment guide](../deployment.md) |
| M9 迁移后独立改进 | `#090-#109` | UI 基线、Identity 增强、现代 SSH、架构拆分、观测、资产管线、仓库工作区和 LFS 完成 | [UI implementation](../design/m9-ui-implementation.md)、[repository workspace](../migration/m9-106-108-repository-workspace.md)、[LFS](../migration/m9-109-git-lfs.md) |
| M10 稳定命名空间 | `#110-#119` | 稳定 ID、规范 Web/Git/SSH 地址、改名限频、alias 占用和统一 resolver 完成 | [stable namespace record](../migration/m10-stable-namespaces.md) |
| M11 Issues | `#120-#129` | Issue CRUD、讨论、Markdown、metadata、订阅、通知、模板、关系、治理和权限验证完成 | [Issues record](../migration/m11-issues.md) |
| M12 Pull Request/Review/Merge | `#130-#139` | PR、diff、行内 review、review status、mergeability、merge/squash、Issue closing 和跨 fork 完成 | [PR baseline](../migration/m12-130-131-pull-request-baseline.md)、[review threads](../migration/m12-133-inline-review-threads.md)、[merge workflow](../migration/m12-135-139-pull-request-merge-workflow.md) |
| M12.5 稳定性与仓库基本面 | `#139A-#139I` | Git HTTP 稳定性、Issue 原子限流、账号恢复、TLS、CI/覆盖率、恢复演练、Branches/Tags/Contributors 完成 | [M12.5 record](../migration/m12-5-stability-and-repository-basics.md) |
| M12.6 SonnetDB 生产部署 | `#139J-#139L` | provider 选择、独立 migration、兼容保护网、`gitcandy.com` Web/HTTP/SSH/LFS、资源边界和一致恢复/回滚完成 | [M12.6 production acceptance](../migration/m12-6-sonnetdb-production-acceptance.md)、[database providers](../database-providers.md)、[production profile](../../deploy/sonnet-vip/README.md) |
| M12.7 个人工作台与仓库发现 | `#139M-#139W` | `/me`、Todo、统一通知、版本化 Feed、工作恢复、公开个人页、Stars/Packages 边界、隐私指标、推荐快照、`/explore` 和桌面/移动验收完成 | [M12.7 workspace/discovery acceptance](../migration/m12-7-workspace-discovery.md) |
| M13 合并治理与外部集成 | `#140-#149` | PAT/deploy key、versioned webhook、status/check、branch protection、required check/review、CODEOWNERS、通知外投、审计、Release/search 和外部 CI gate 总验收完成 | [machine credentials/push gate](../migration/m13-140-143-machine-credentials-push-gate.md)、[webhook/check](../migration/m13-141-143-webhooks-checks-required-gate.md)、[CODEOWNERS](../migration/m13-144-codeowners-required-reviews.md)、[M13 final acceptance](../migration/m13-145-149-collaboration-extensions-external-ci.md) |
| M14 团队治理与企业身份联邦 | `#150-#159` | 四级团队角色、统一授权、企业连接、Entra ID/SCIM、企业微信/飞书/钉钉、目录对账、同步停用和安全 fixture 总验收完成 | [team role baseline](../migration/m14-150-team-governance-roles.md)、[M14 final acceptance](../migration/m14-151-159-enterprise-identity.md) |

## M13 验收切片

M13 已整体退出活动路线图；下列切片共同构成最终完成证据：

| 编号 | 完成结论 | 证据入口 |
| --- | --- | --- |
| `#140` | hash-only scoped PAT、一次显示、到期/撤销/last-used/审计、Bearer API policy 和 Git Basic read/write scope 隔离完成 | [M13 machine credentials and push gate](../migration/m13-140-143-machine-credentials-push-gate.md)、`GitBasicAuthenticationHandlerTests`、`GitSmartHttpIntegrationTests` |
| `#140A` | 仓库级只读/可写 deploy key、全局 fingerprint claim、到期/撤销/last-used/审计，以及内置 SSH/OpenSSH 机器身份完成 | [M13 machine credentials and push gate](../migration/m13-140-143-machine-credentials-push-gate.md)、`CredentialGovernanceServiceTests`、`SshGitIntegrationTests` |
| `#141` | versioned envelope、HMAC-SHA256、delivery ID、timeout、持久化 retry/replay、one-time protected secret、SSRF/redirect 边界和 owner 诊断 UI 完成 | [M13 webhook/check/required gate](../migration/m13-141-143-webhooks-checks-required-gate.md)、`WebhookIntegrationTests`、`CredentialGovernanceServiceTests` |
| `#142` | PAT Bearer status/check API、SHA/context 幂等、repository resource authorization、credential rate limit、commit/target URL 边界完成 | [M13 webhook/check/required gate](../migration/m13-141-143-webhooks-checks-required-gate.md)、`GitSmartHttpIntegrationTests` |
| `#143` | branch pattern、force/delete、allowed push/merge、管理员显式 bypass、exact-SHA required checks/approvals 和 HTTP/SSH/Web 共用 gate 完成 | [M13 machine credentials and push gate](../migration/m13-140-143-machine-credentials-push-gate.md)、[M13 webhook/check/required gate](../migration/m13-141-143-webhooks-checks-required-gate.md)、[M13 CODEOWNERS/required review](../migration/m13-144-codeowners-required-reviews.md) |
| `#144` | 受控 CODEOWNERS、merge-base changed-path owner、最少批准、stale approval 和可解释 required-review blocker 完成 | [M13 CODEOWNERS/required review](../migration/m13-144-codeowners-required-reviews.md)、`CodeOwnersTests`、`CredentialGovernanceServiceTests`、`PullRequestGitRepositoryTests` |
| `#145-#149` | 统一通知扩展/外投、独立审计、tag Release/assets、权限优先搜索，以及真实 webhook -> PAT check -> required gate/revoke/retry 验收完成 | [M13 final acceptance](../migration/m13-145-149-collaboration-extensions-external-ci.md)、`CollaborationExtensionServiceTests`、`ReleaseAssetStoreTests`、`GitSmartHttpIntegrationTests.ExternalCi_*` |

## M14 验收切片

M14 已整体退出活动路线图；登录联邦、目录供应和团队治理保持独立边界：

| 编号 | 完成结论 | 证据入口 |
| --- | --- | --- |
| `#150-#151` | `TeamOwner/Leader/DeputyLeader/Member` schema、权限矩阵、统一授权、原子批量成员操作、治理审计、完整 UI，以及最后 owner 和本地 break-glass owner 保护完成 | [team role baseline](../migration/m14-150-team-governance-roles.md)、[M14 final acceptance](../migration/m14-151-159-enterprise-identity.md)、`TeamGovernanceTests` |
| `#152` | 企业连接 schema/provider 边界、稳定 external ID、secret reference、连接诊断、游标/状态、管理 UI 和审计完成 | [M14 final acceptance](../migration/m14-151-159-enterprise-identity.md)、`EnterpriseConnectionServiceTests` |
| `#153-#154` | Entra OIDC state/PKCE/nonce/discovery/JWKS/tenant 校验、稳定 `oid/sub` 绑定、冲突拒绝，以及独立 SCIM Bearer Users/Groups/PATCH/active/分页完成 | [M14 final acceptance](../migration/m14-151-159-enterprise-identity.md)、`EnterpriseLoginSecurityTests`、`ScimProvisioningTests` |
| `#155-#157` | 企业微信 `userid`、飞书租户级 `user_id`、钉钉 `unionId` 稳定绑定，登录、目录分页、操作级 token 和 provider event 去重完成 | [M14 final acceptance](../migration/m14-151-159-enterprise-identity.md)、`DomesticEnterpriseProviderTests` |
| `#158-#159` | 15 分钟有界对账、游标恢复、连接级故障隔离、缺失用户停用、session/PAT/SSH 撤销、签名/时间窗/大小/限流/去重和安全 fixture 完成 | [M14 final acceptance](../migration/m14-151-159-enterprise-identity.md)、`ScimProvisioningTests`、`EnterpriseLoginSecurityTests` |

## 已完成但仍需持续观察的风险

- M12.5 的 CI 对每份测试报告执行 80% 整体 line-rate 门禁；Core、Data、Web/Auth 单独 assembly 尚未全部达到 80%。这是持续测试质量目标，不推翻 M12.5 已交付的 CI 门禁和当前测试通过结论。
- SQL Server 已验证 migration SQL，但真实生产部署验证仍不是当前默认 SQLite 路径的完成条件。
- Git、SSH、TLS、备份恢复和跨平台能力会受外部 Git/OpenSSH、代理、操作系统和文件系统版本影响；发布门禁必须持续运行，不能把本文件当作永久豁免。
- M12.6 生产 SSH 已用 RSA-3072 完成协议验收；Ed25519 public key 和 post-quantum hybrid key exchange 尚未进入当前内置 SSH 支持矩阵，后续扩展时需保持 RSA 路径回归覆盖。
- 通知邮件依赖部署者配置 SMTP；未配置时 delivery 明确失败为 `smtp_not_configured`，不会影响站内 inbox。
- Entra ID、企业微信、飞书和钉钉已用本地协议与签名 fixture 验收；真实 tenant 的 consent、scope、回调域名和网络策略仍需每个部署环境单独 smoke，不能用本记录替代。

## 后续文档编写入口

| 文档主题 | 推荐资料来源 |
| --- | --- |
| 安装、升级、备份与回滚 | [deployment guide](../deployment.md)、M8/M12.5 migration records |
| 数据库与 Identity | [database providers](../database-providers.md)、M3/M4 migration records |
| Git HTTP/SSH/LFS | M6、M7、M9 SSH/backend/LFS migration records |
| 用户、团队和稳定 URL | M10 migration record、[enterprise repository design](../product/enterprise-repository-roadmap.md) |
| Issues、PR 与评审 | M11/M12 migration records、[collaboration design](../product/collaboration-roadmap.md) |
| 私人工作台、通知、Feed、公开个人页与发现 | [M12.7 workspace/discovery acceptance](../migration/m12-7-workspace-discovery.md) |
| 团队治理、企业登录、SCIM 与目录同步 | [M14 final acceptance](../migration/m14-151-159-enterprise-identity.md)、[deployment guide](../deployment.md) |
| UI、主题和可访问性 | M9 design records and visual baselines |

## 状态维护规则

- 完成任务只在本文件和对应专题文档中维护，不回填根路线图。
- 根路线图只允许 `进行中`、`未完成`、`阻塞` 三种活动状态。
- 新发现的缺陷使用新的修复编号，写明影响的历史能力和回归测试，不篡改历史验收记录。
- 一个进行中里程碑只有在所有验收项完成后才能整体迁入本文件。
