# M15 #164-#165 单向 Pull/Push Mirror

## 对应 ROADMAP

- Milestone 15 / #164：remote 权威的 Pull mirror。
- Milestone 15 / #165：GitCandy 权威的 Push mirror。
- 本切片不实现 #166 的通用持久化 job lease/退避/重启恢复，也不实现 #167 的 webhook 和运维 UI。

## 变更点

- 新增 `IRemoteMirrorService`，注册 mirror 后通过同一个应用边界执行初始同步、到期 Pull 扫描、pending Push 扫描和 stable remote ID 对应的 rename/profile 更新。
- Pull/Push 都先把远端 branches/tags fetch 到 `refs/gitcandy/mirrors/{mirrorId}/`。Git Smart HTTP、内置 SSH 和 OpenSSH transport 使用 `transfer.hideRefs` 隐藏该 namespace；操作结束后清理 staging refs。
- Pull 只更新 `refs/heads/*` 和 `refs/tags/*`。`Stop` 在任何分叉时不修改正式 refs，`KeepDivergent` 跳过分叉 ref，`OverwriteTarget` 才覆盖目标并写 `mirror.pull.force` 审计；`Prune` 关闭时不删除本地 ref。
- 启用 Pull mirror 后，EF 权限查询和统一 push gate 都拒绝本地写入，管理员也不能绕过。Web merge、分支/标签删除、HTTP Git、内置 SSH 和 OpenSSH 复用同一判断。
- receive-pack 继续先执行受控 `pre-receive` gate；成功后执行 `post-receive` 子命令。后者不访问远端网络，只把 ref 创建、更新、删除按 `(MirrorId, ReferenceName)` 合并到 `RemoteMirrorRefUpdates` 并递增 generation。
- Push 后台执行重新读取当前本地 ref 和远端 staging ref，处理 stale/合并事件。删除只在 `Prune` 开启时传播；non-fast-forward 默认停止，`KeepDivergent` 保留远端分叉，`OverwriteTarget` 使用显式 force refspec 并写 `mirror.push.force` 审计。
- Quartz `remote-mirror-sync` 每 5 秒唤醒 pending Push，并检查到期 Pull。单个进程内同一 mirror 串行；跨实例 lease、重试次数、退避和 crash recovery 由 #166 增加。

## 数据与兼容性

- SQLite、SQL Server、SonnetDB 新增 `M15PullPushMirrors` migration，只增加 `RemoteMirrorRefUpdates` 表、复合主键、mirror 级联外键和 `UpdatedAtUtc` 索引。
- pending 表不保存 token、remote URL、stderr 或 pack 数据；object ID 支持 SHA-1/SHA-256 最大 64 字符。
- 公开 Web/Git URL、Identity cookie、Basic Auth、SSH key 和现有仓库布局不变。
- Mirror 只同步 Git objects、branches 和 tags，不隐式同步 LFS、Issues、PR/MR、Wiki、Releases、CI 或 Packages。

## 迁移与回滚

1. 升级前一致备份数据库、repository root 和 Data Protection key ring。
2. 审阅 SQL Server idempotent migration SQL，再升级单实例应用。
3. 回滚前停止写流量和 mirror 调度，恢复升级前数据库与仓库快照，再回滚应用。
4. 只执行 down migration 会删除尚未推送的 pending ref 事件；只回滚二进制但保留新表不会被旧版本消费。

## 验证重点

- SQLite migration 写入/读取 pending ref generation，SQL Server migration SQL 和 SonnetDB migration 执行。
- Pull 初始/fast-forward、divergence stop、force audit、prune、ref filter、只读权限和 staging namespace 清理。
- Push post-receive 合并、失败不回滚本地 push、force、删除传播、pending generation 和成功后消费。
- `dotnet build`、完整 `dotnet test`、Git HTTP/SSH clone/fetch/push 结果记录在本次变更说明中。
