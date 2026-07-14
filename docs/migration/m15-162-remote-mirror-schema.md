# M15 #162 Remote/Mirror EF schema

## 对应 ROADMAP

- Milestone 15 / #162：Remote/mirror EF schema。
- 本切片只建立持久化契约，不实现 provider 绑定 UI、Git 同步进程、Mirror job、Webhook 或调度执行。

## 变更点

- 新增 `RemoteAccountConnections`，保存用户或团队归属、provider/server、稳定外部账号 ID、账号展示资料、认证方式、opaque credential reference、已授权 scope、启用状态和连接诊断状态。
- 新增 `RepositoryMirrors`，保存本地仓库、远程连接、稳定远程仓库 ID、当前远程名称/URL、单向 `Pull`/`Push`、权威侧、ref filter、周期/时区、divergence/prune 策略和最近运行状态。
- SQLite、SQL Server、SonnetDB 分别新增 `M15RemoteMirrorSchema` migration 和 model snapshot。
- 数据库不保存 token、密码、SSH 私钥或 provider 响应；`CredentialReference` 只允许后续应用服务写入 vault/secret-store 引用。

## 数据约束

- 每个远程连接必须且只能归属于一个 Identity 用户或一个团队。
- `(Provider, ServerUrl, ExternalAccountId)` 唯一，远端改名不改变连接身份；`ServerUrl` 限制为 512 字符，使 SQL Server 组合索引保持在 1700-byte key limit 内。
- `Pull` 只能以 `Remote` 为权威侧，`Push` 只能以 `GitCandy` 为权威侧；第一阶段没有双向枚举值。
- `AllowList`/`RegularExpression` ref filter 必须保存 filter 内容，`AllRefs`/`ProtectedBranches` 不保存额外表达式。
- 周期间隔与时区必须同时为空或同时存在；间隔存在时限制为 5 至 10080 分钟。
- 同一本地仓库、连接、稳定远程仓库和方向只能存在一条 mirror 配置。
- 删除仓库或显式删除连接会级联删除 mirror；删除用户/团队前必须先通过应用流程断开连接，以便后续凭据撤销流程不会被数据库级联绕过。

## 数据影响

- 升级只创建两张空表及其索引、外键和 CHECK constraints，不回填旧数据库，也不读取或改写旧账号数据。
- 不创建后台任务，不访问远端，不修改本地 Git refs，也不改变公开 Web/Git HTTP/SSH/LFS URL 或权限行为。
- 第一阶段仍只规划 Git refs；LFS、Issues、PR/MR、Wiki、Releases、CI 和 Packages 不进入 mirror schema。

## 部署与回滚

1. 升级前同时备份数据库、repositories、cache 和 secret store/vault。
2. 使用正常启动迁移或 `GitCandy --migrate` 应用 provider 对应 migration。
3. 验证两张新表、唯一索引和 CHECK constraints 后再启用后续连接 UI 或同步服务。
4. 回滚应用时恢复升级前数据库和上一版本应用。不要在已经保存连接/mirror 配置后直接执行 down migration，否则这些配置会被删除；vault secret 的撤销/清理由后续连接服务负责，不能仅依赖数据库回滚。

## 验证

- SQLite migration smoke：有效 Pull mirror 可写入并读取，方向/权威侧不匹配、owner 双重归属、周期缺少时区均被拒绝。
- SQL Server：离线 idempotent migration SQL 包含两张表、唯一索引和 owner/direction/schedule/ref CHECK constraints。
- SonnetDB：实际执行 migrations，写入 Gitee remote account 与 Push mirror，并验证 provider 的 BOOL/CHECK 兼容性。
- `dotnet build`、完整 `dotnet test`、pending-model-change 检查和最终 migration SQL 结果记录在本次变更说明中。
