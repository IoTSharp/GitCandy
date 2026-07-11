# M12 #135-#139 Pull Request 合并工作流迁移记录

日期：2026-07-11

## 变更点

- Pull Request 增加稳定、可在 source 删除后置空的 `SourceRepositoryId`，并保存不可变 source namespace/repository 快照；既有同仓库 PR 在 migration 中回填为自身 `RepositoryId` 和当前地址快照。
- Repository 增加可空 `ForkedFromRepositoryId` 和 `ForkNetworkRootRepositoryId`；新建 fork 同时写入稳定 ID，旧名称字段暂时保留用于展示和兼容。
- mergeability 同步汇总 draft、source/target tip、冲突、有效批准、request changes、未解决 thread 和 checks 占位状态。
- merge commit 与 squash 通过 LibGit2Sharp merge tree 创建，仓库级锁内复核不可变 base/head 后更新目标 ref；数据库提交失败时仅在 ref 仍指向本次 merge commit 时回滚。
- 合并成功后对 PR 标题、描述和 merge message 中的 `fixes/closes/resolves #N` 执行幂等 Issue 自动关闭。
- 跨 fork PR 仅允许同一稳定 fork network，作者必须能读 target 且能写 source；source 对象通过受控本地 fetch 导入 target 的只读 `refs/pull/{number}/head`。

## 数据影响

- SQLite 和 SQL Server migration 均以可空 `SourceRepositoryId` 加入并用 `RepositoryId` 回填既有行；外键使用 `SET NULL`，source fork 删除后仍由地址快照和 `refs/pull/{number}/head` 保留可诊断历史。
- 旧 fork 名称可能存在跨 namespace 歧义，因此 migration 不猜测历史字符串关系。既有 fork 的稳定 ID 可由后续显式修复工具或重新建立 fork 关系补齐；新 fork 自动写入。
- 新配置 `GitCandy:Application:RequiredPullRequestApprovals` 默认 `1`，范围 `0..100`。当前没有 M13 check/branch policy 时 required checks 视为已满足。

## 部署与回滚

1. 升级前备份数据库和 repositories 根目录。
2. 停止写入后运行应用数据库 migration，再启动新版本。
3. 验证既有 PR 的 `SourceRepositoryId = RepositoryId`、新 fork 的稳定 ID、merge/squash 和目标 ref。
4. 回滚时先停止新版本写入，恢复数据库与 repositories 同一时间点的配套备份，再部署旧版本。不能只回滚数据库，因为新版本可能已经更新 Git branch ref。

## 验证

- SQLite migration 创建/读写测试。
- SQL Server idempotent migration SQL 生成与审阅。
- 服务测试覆盖 approval、mergeability、Issue 自动关闭和跨 fork 稳定关系。
- 真实 bare repository 测试覆盖 merge commit、squash、冲突、跨 fork对象导入和 ref 结果。
- Kestrel MVC 测试覆盖 draft、review thread、approve、ready、squash 和私有仓库匿名拒绝。
