# GitCandy 活动产品路线图

更新日期：2026-07-13

本文件只维护尚未完成的工作。已经退出活动路线图的 M0-M12.7、验收结论和专题文档入口见 [已完成里程碑索引](docs/roadmap/completed-milestones.md)；重组前的完整路线图见 [2026-07-13 历史快照](docs/roadmap/roadmap-archive-2026-07-13.md)。

## 状态与维护规则

- `🚧`：正在实施，至少还有一个验收项未闭环。
- `⬜`：尚未开始，或没有可验证的完成记录。
- 活动路线图不保留完成任务表；里程碑完成后整体迁入 `docs/roadmap/completed-milestones.md`。
- 不能用“没有发现”表述“绝对没有缺陷”。完成结论必须附构建、测试、专项验收和已知剩余风险。
- 一个变更只对应一个编号或一个明确垂直切片，不把 schema、UI、协议、部署和无关重构混在一起。
- 默认继续使用 `master`，不擅自创建或切换分支。

## 当前边界

- 唯一活动 solution 是 `GitCandy.slnx`，目标 `net10.0`。
- 默认运行数据库仍是 SQLite；SQL Server 保留 migration SQL 路径；SonnetDB 只由专用配置显式启用。
- GitCandy 继续采用单进程 host：Web UI、Git Smart HTTP、内置 SSH、Quartz 和后台入口共同启动和停止。
- Git HTTP/SSH 继续复用统一 repository resolver、权限和 `IGitTransportBackend`，不能在业务层新增散落的进程调用。
- M13 的 PAT、deploy key、versioned webhook、status/check API、CODEOWNERS 和 required review/check gate 已建立；当前进入通知、审计、release、search 和外部 CI 总验收。M14-M16 继续按依赖顺序推进。

## 已确认的产品决策与冲突处理

| 主题 | 决策 | 解决的冲突 |
| --- | --- | --- |
| 登录首页 | `/me` 是仅当前用户可见的私人工作台；登录后访问 `/` 默认进入 `/me` | 不再把 `/me` 定义成公开个人页的 Repositories tab |
| 公开个人页 | `/{username}` 表示公开个人页；使用 allowlist `tab` 展示 repositories、stars、packages、teams | 公开身份展示与私人工作入口分离，避免隐私和导航语义混杂 |
| Todo | 表示“仍需要我处理”的可行动状态，支持完成、恢复和 snooze；读过通知不会自动完成 Todo | Todo 不等于未读通知 |
| Notification | 表示某件事已经发生并投递给当前用户，维护 read/unread 和投递原因 | 通知不承担工作状态，不因 Todo 完成而删除历史 |
| Feed | 表示关注、参与、团队和仓库活动的上下文时间线，可筛选但不是收件箱 | Feed 不参与 unread 计数，也不替代审计日志 |
| 团队通知 | 使用统一通知的 `team` 来源/筛选维度，不建立第二套团队收件箱 | 避免重复投递、重复已读状态和权限撤销后的数据泄漏 |
| 需要关注的仓库 | 根据 Todo、未读通知、近期交互、Issue/PR 和更新时间排序，并显示原因 | 不把 owner 仓库的页面流量或 Star 人气误当成个人工作优先级 |
| 公开推荐 | `/explore` 和 dashboard 推荐只读取公开候选集及版本化快照 | 登录用户的私有权限不能污染公开排行榜 |
| “开源”文案 | 没有显式 SPDX/许可证证据时只能称“公开仓库”；确认开放许可证后才称“开源项目” | 避免把 public 错误等同于 open source |
| Activity 与 Audit | Activity 为用户可见产品事件；Audit 为不可篡改安全证据，可共享事件 envelope 但必须分开存储和保留 | M12.7 Feed 不能替代 M13 审计 |
| Packages | M12.7 只提供真实目录和空状态；OCI push/pull、存储、GC 由 M15.6 实现 | 不提前展示无后端的上传能力 |
| 通知阶段 | M12.7 建立统一 inbox 和 dashboard 摘要；M13 `#145` 扩展 PR/review/check/release 与外部投递 | 避免 M12.7 和 M13 各建一套通知模型 |

## 当前实施顺序

1. `#145-#149`：扩展通知/审计/release/search，并完成外部 CI webhook -> check -> gate 总验收。
2. M13 完成后再进入 M14/M15；M15.5 文档体系在相关产品契约稳定后实施。
3. M15.6 Registry 完成后接入 Packages 实际数据；M16 最后接入知识库和 MCP。

## 🚧 Milestone 13：合并治理、外部集成与发布基础

目标：让 Issue/PR 接入外部 CI、自动化和仓库治理，并形成可审计、可诊断的团队开发入口。

`#140-#144` 的机器凭据、webhook/check、branch protection、CODEOWNERS 和 required review/check gate 已完成，验收记录见 [机器凭据与 push gate](docs/migration/m13-140-143-machine-credentials-push-gate.md)、[webhook/check/required gate](docs/migration/m13-141-143-webhooks-checks-required-gate.md)及 [CODEOWNERS/required review](docs/migration/m13-144-codeowners-required-reviews.md)。当前执行顺序：`#145 -> #149`。保护分支必须同时作用于 Git HTTP、SSH push 和 Web merge；webhook 失败不能回滚已经成功的 push/merge。

| 编号 | 状态 | 主题 | 验收重点 |
| --- | --- | --- | --- |
| #145 | ⬜ | 通知事件扩展与外部投递器 | 在 M12.7 统一 inbox 上增加 PR/review/check/release、偏好、邮件/webhook 投递和失败诊断；不新建第二套 inbox |
| #146 | ⬜ | 协作审计日志 | 不可由普通用户篡改的关键变更证据；与 Feed 分离存储、保留和查询 |
| #147 | ⬜ | Releases 与 assets | tag release、Markdown、受限附件、权限、路径/大小和孤儿清理 |
| #148 | ⬜ | 协作搜索 | repository/issue/PR/commit/code 搜索，所有结果先做 repository 权限过滤 |
| #149 | ⬜ | 外部 CI 端到端验证 | fixture 收 webhook、回写 check，required review/check 控制 push/merge，并覆盖撤销、重试、并发和私有数据 |

完成门槛：外部 CI 能用最小 scope PAT 完成 webhook -> check -> required gate；所有 bypass、force/delete 和规则变化可审计；通知、search、release 和 payload 不泄漏私有资源。

详细设计见 [协作路线设计](docs/product/collaboration-roadmap.md)。

## ⬜ Milestone 14：团队治理与企业身份联邦

目标：提供 `TeamOwner/Leader/DeputyLeader/Member` 四级角色，以及 Microsoft Entra ID、企业微信、飞书、钉钉登录与目录同步。

登录联邦与目录供应分层；外部同步默认只授予 Member，至少保留一个本地 break-glass TeamOwner。M14 复用 M12.7 通知和 M13 PAT/审计，不直接改写 namespace 字符串外键。

| 编号 | 状态 | 主题 | 验收重点 |
| --- | --- | --- | --- |
| #150 | ⬜ | 四级团队角色 schema 与权限矩阵 | 角色迁移、成员/仓库/改名/连接权限和最后 owner 保护 |
| #151 | ⬜ | 团队授权服务与 UI | 统一角色比较、服务端复核、批量操作和审计 |
| #152 | ⬜ | 企业连接与 secret 边界 | provider 接口、稳定 external ID、secret reference、游标和管理 UI |
| #153 | ⬜ | Microsoft Entra ID 登录 | tenant/issuer、claims、冲突处理、连接测试和组织启用 |
| #154 | ⬜ | SCIM 2.0 Users/Groups | bearer、create/query/PATCH、active、分页、幂等 externalId 和 Entra smoke |
| #155 | ⬜ | 企业微信 adapter | OAuth、稳定用户绑定、部门/成员同步、最小 scope 和诊断 |
| #156 | ⬜ | 飞书 adapter | tenant/OAuth、稳定 ID、增量同步、token 轮换和事件去重 |
| #157 | ⬜ | 钉钉 adapter | CorpId/unionId/userId 作用域、同步、轮换和去重 |
| #158 | ⬜ | Deprovision 与对账作业 | 停用、session/凭据撤销、故障隔离、恢复和 break-glass |
| #159 | ⬜ | 企业连接安全与集成验证 | secret 脱敏、state/PKCE、签名、限流、冲突、最后 owner 和 provider fixture |

完成门槛：最后 TeamOwner 不可被删除、降级或同步停用；管理员能测试和诊断连接但不能读取 secret；同步按稳定外部 ID 幂等，不因邮箱或显示名变化创建重复用户。

## ⬜ Milestone 15：远程账号连接与单向 Mirror

目标：绑定 GitHub/GitLab/Gitee 账号，通过可观测、可取消、可审计的后台 job 完成导入、单向 pull 和单向 push。

第一阶段只同步 Git refs，不隐式同步 LFS、Issues、PR/MR、Wiki、Releases、CI 或 Packages。Pull 以远端为权威并默认禁止本地 push；Push 在本地成功后异步入队；divergent ref 默认停止告警，双向 mirror 默认禁用。

| 编号 | 状态 | 主题 | 验收重点 |
| --- | --- | --- | --- |
| #160 | ⬜ | Remote account/provider 抽象 | 用户/组织连接、稳定 remote ID、最小 scope、secret 轮换和撤销 |
| #161 | ⬜ | GitHub/GitLab/Gitee 绑定 UI | provider 配置、用户绑定、仓库发现、测试连接且不回显 token |
| #162 | ⬜ | Remote/mirror EF schema | direction、authority、ref/schedule/divergence/prune、状态和 migration |
| #163 | ⬜ | 受控 remote sync backend | `ArgumentList`/credential helper、流式 I/O、取消、超时、路径和日志脱敏 |
| #164 | ⬜ | Pull mirror | 初始导入、周期 fetch、ref policy、rename、只读保护和 divergence |
| #165 | ⬜ | Push mirror | post-receive 入队、事件合并、ref filter、删除和显式 force policy |
| #166 | ⬜ | 持久化 job pipeline | lease、串行、并发限制、退避、重启恢复和 graceful shutdown |
| #167 | ⬜ | Webhook 与运维视图 | 验签/去重、周期兜底、状态、暂停/重试/取消和分类错误 |
| #168 | ⬜ | Provider connectors | GitHub App、GitLab/Gitee OAuth/PAT、rate limit、过期、rename/delete |
| #169 | ⬜ | Mirror 故障与规模验证 | 三 provider fixture、凭据撤销、并发、大仓库、恢复和双向 go/no-go |

详细设计见 [企业仓库路线设计](docs/product/enterprise-repository-roadmap.md)。

## ⬜ Milestone 15.5：帮助中心与全量文档发布

目标：把 README、完成历史、迁移记录、产品决策和运维说明整理为版本化 `/help` 静态站点。Markdown 是唯一事实来源；帮助站点在构建/发布阶段生成并随应用产物部署，不在生产请求或启动时生成。

本次路线图重组产生的 `docs/roadmap/completed-milestones.md` 和历史快照进入文档 inventory，但公共帮助只链接仍适用于当前版本的规范指南；历史记录必须带 archived/version metadata。

| 编号 | 状态 | 主题 | 验收重点 |
| --- | --- | --- | --- |
| #169A | ⬜ | 文档 inventory 与信息架构 | owner/audience/public/canonical/archived、`/help` 导航、permalink 和版本策略 |
| #169B | ⬜ | 用户与协作文档 | 账号、dashboard、仓库、Git/LFS、Issue、PR/review、发现页 |
| #169C | ⬜ | 管理、部署与排障文档 | 权限、安全、provider、三类部署、TLS、恢复、观测和回滚 |
| #169D | ⬜ | API、MCP 与开发者文档 | API/webhook/pagination/error/rate limit、MCP matrix、架构与贡献 |
| #169E | ⬜ | JekyllNet local tool 与主题 | 固定 local tool、layouts、导航、代码高亮、搜索 metadata 和无 CDN 主题 |
| #169F | ⬜ | 帮助菜单与安全静态路由 | `/help`、PathBase、404、cache/CSP/content type、路径边界和匿名访问 |
| #169G | ⬜ | 多产物构建与打包 | Docker/publish/Linux/Windows 统一生成 help + manifest，失败阻止发布 |
| #169H | ⬜ | 文档质量与发布验收 | 链接、锚点、permalink、示例、截图、a11y、敏感信息、版本和回滚一致 |

## ⬜ Milestone 15.6：SonnetDB-backed OCI Container Registry

目标：由 GitCandy 暴露 OCI Distribution `/v2/`，EF Core 保存 registry metadata，SonnetDB bucket 保存内容寻址 blob；最终用户权限始终由 GitCandy enforcement，不把 bucket policy 当作安全边界。

大 layer 全程流式。upload session、digest、manifest/tag、multi-arch、配额、GC、reconciliation、备份恢复和真实客户端/conformance 必须共同闭环。M12.7 Packages 在本阶段才显示实际 image 操作。

| 编号 | 状态 | 主题 | 验收重点 |
| --- | --- | --- | --- |
| #169I | ⬜ | OCI 兼容矩阵与架构决策 | API/media type/client、URL/域名、SonnetDB SDK、非目标和回滚 |
| #169J | ⬜ | Blob abstraction 与 SonnetDB hardening | stream/Range/multipart/SHA-256/conditional create/promote、crash/disk-full |
| #169K | ⬜ | OCI metadata schema | blob/manifest/link/tag/upload 状态机、唯一索引、并发和三 provider migration |
| #169L | ⬜ | Registry token 与权限 | bearer challenge、PAT 换短 token、pull/push/delete/admin scope 和审计 |
| #169M | ⬜ | Blob push/pull | probe、HEAD/GET/Range、resumable/monolithic upload、digest 和 mount |
| #169N | ⬜ | Manifest、tag 与 multi-arch | media negotiation、OCI index、child graph、tags/list、delete 和并发更新 |
| #169O | ⬜ | Packages/Container UI 与文档 | image/tag/platform/size/pull、visibility、retention，并同步 `/help` 和 CHANGES |
| #169P | ⬜ | 配额、GC 与 reconciliation | logical/physical quota、upload expiry、mark/sweep、修复、health 和低优先级 job |
| #169Q | ⬜ | Conformance 与客户端矩阵 | OCI suite、Docker/Podman、TLS/proxy、resume、Range、mount 和 multi-arch |
| #169R | ⬜ | 安全、故障、规模与恢复 | 越权、spoof、逃逸、并发、大镜像、slow client、kill/disk-full、恢复和资源隔离 |

## ⬜ Milestone 16：Agent Memory、文档知识库与 MCP

目标：摄入 Git 仓库、规范文档、ADR、CI、review 和 Agent 会话，在独立 SonnetDB knowledge schema 中建立结构化、全文和向量索引，并通过 Web/API/MCP 提供可引用的代码与文档查询。

原则：结构化优先、向量补充；只读起步；增量索引不进入 Git 热路径；所有查询复用 repository 权限；生成 HTML、third-party、secret、临时日志和重复副本不摄入。MCP 是应用服务适配层，不是 endpoint 自动反射代理。

| 编号 | 状态 | 主题 | 验收重点 |
| --- | --- | --- | --- |
| #170 | ⬜ | Code Memory 方案与 schema | repo/file/symbol/edge/chunk/commit/decision/memory、权限和规模边界 |
| #171 | ⬜ | Git/文件/文档 ingest | 基础增量扫描、hash、取消和状态 |
| #172 | ⬜ | C# 符号索引器 | Roslyn 输出 namespace/type/member/test/endpoint 和位置 |
| #173 | ⬜ | 调用与引用边 | calls/references/implements/tests/imports/routes_to 和影响查询 |
| #174 | ⬜ | Code Memory MCP tools | search/symbol/callers/callees/impact/snippet/decision tools |
| #175 | ⬜ | Hybrid Search | BM25、embedding KNN、metadata filter 和可解释融合 |
| #176 | ⬜ | Agent Memory API | 会话摘要、memory 读写、tool audit 和 repo/branch 权限 |
| #177 | ⬜ | Explorer UI | 索引状态、文件/符号、调用关系、影响、决策和 memory |
| #178 | ⬜ | VS Code/Copilot 样例 | 当前符号、调用者、影响和历史决策场景 |
| #179 | ⬜ | 规模验证 | GitCandy 和中大型 C# 仓库的 ingest/query/profile 报告 |
| #180 | ⬜ | 文档 corpus manifest | canonical/version/audience/public、hash 和排除规则 |
| #181 | ⬜ | SonnetDB knowledge schema | documents/chunks/state、BM25/HNSW、配额、备份和模型维度 |
| #182 | ⬜ | 自动增量摄入与诊断 | upsert/delete、幂等、取消、限流、失败恢复、重建和健康页 |
| #183 | ⬜ | 文档 Search 与 MCP | `docs_search/get/topics`、引用、低置信度、版本/语言/权限 |
| #184 | ⬜ | API inventory 与 MCP matrix | 每个业务/协议 endpoint 的映射、scope、风险、版本或排除原因 |
| #185 | ⬜ | MCP host 与只读业务 tools | Streamable HTTP、PAT/Bearer、授权、限流、分页、审计和 tracing |
| #186 | ⬜ | MCP 写工具治理 | 最小 scope、幂等、并发、确认、审计、可禁用和 secret 边界 |
| #187 | ⬜ | MCP parity、安全与规模 | 权限 parity、schema、prompt injection、越权、枚举、撤销、并发和大结果 |

完成门槛：私有内容不越权；每个 MCP 结果可追溯到稳定资源或同版本 `/help` 引用；未配置 SonnetDB 时核心 Git 和帮助站点正常且知识功能明确不可用；索引和查询不破坏 clone/fetch/push 延迟、流式和内存边界。

## 跨里程碑风险

- 任何新的全局列表、Feed、Todo、Notification、search、webhook、MCP 或 recommendation 都可能泄漏私有资源，必须在查询时重新校验权限。
- Todo/Notification/Feed/Audit/Webhook 若各自直接监听业务表会产生重复和漂移；应共享版本化事件 envelope，但保持各自状态和事务边界。
- public 不等于 open source；License 不明确时不能使用“开源排行”或“开源推荐”。
- Star、访问、下载和导入 commit 容易被刷；排名必须使用成功事件聚合、时间衰减、异常降权、算法版本和稳定快照。
- PAT、deploy key、provider token、registry token、MCP token 和企业 secret 必须独立 scope、可撤销、可审计且不进入 URL/日志。
- Mirror、Registry、Knowledge ingest 和 GC 都是资源密集后台任务，必须限流、可取消、可恢复，并保护 Git HTTP/SSH 热路径。
- M15.5 文档必须区分当前规范与历史快照；不能让已归档迁移说明覆盖当前操作指南。

## 参考文档

- [已完成里程碑索引](docs/roadmap/completed-milestones.md)
- [重组前路线图历史快照](docs/roadmap/roadmap-archive-2026-07-13.md)
- [协作路线设计](docs/product/collaboration-roadmap.md)
- [企业仓库与身份路线设计](docs/product/enterprise-repository-roadmap.md)
- [数据库 provider](docs/database-providers.md)
- [部署、备份与回滚](docs/deployment.md)
- [CHANGES](CHANGES.md)
