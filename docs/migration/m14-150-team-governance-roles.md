# M14 #150 四级团队角色 schema 与权限矩阵

## 范围

本切片启动 Milestone 14，只建立团队治理角色的数据与规则基线：

- `TeamOwner`、`Leader`、`DeputyLeader`、`Member` 四级角色；
- 团队改名、转移、删除、成员治理、团队仓库、团队策略和企业连接权限矩阵；
- 旧二级 `IsAdministrator` 数据迁移；
- 最后一位 TeamOwner 的服务层保护。

统一授权服务、角色管理 UI、批量操作和治理审计属于 `#151`。企业连接、OIDC、SCIM 和国内 provider 属于 `#152-#159`，不在本切片中。

## Schema 与数据影响

`UserTeamRoles.IsAdministrator` 替换为最长 20 字符的必填 `Role`：

| 旧值 | 新值 |
| --- | --- |
| `true` | `TeamOwner` |
| `false` | `Member` |

迁移将 `IX_UserTeamRoles_TeamId` 替换为 `IX_UserTeamRoles_TeamId_Role`，使按团队和角色检查 owner 的查询使用同一索引。SQLite、SQL Server 和 SonnetDB 都增加 `CK_UserTeamRoles_Role`，只接受四个已定义角色。随仓库固定的 SonnetDB source dependency 已补齐 `CREATE/ALTER TABLE ... CHECK`、存量数据验证、DML enforcement、DROP 和重启恢复；其 table schema codec 从 v5 向后兼容升级到 v6。

新建团队的创建者直接成为 TeamOwner。现有 `TeamAdministrator` 授权策略在 `#151` 前保持严格兼容，只把 TeamOwner 视为原“团队管理员”；不会提前让 Leader 获得所有旧管理员路由。

## 权限边界

- TeamOwner 可以管理团队身份、所有治理角色、团队 slug/生命周期、团队仓库和策略。
- Leader 可以只读企业连接，管理 DeputyLeader/Member，并创建团队仓库和管理团队策略。
- DeputyLeader 只因团队角色获得 Member 管理权；额外团队策略委派尚未实现。
- Member 不因团队角色获得治理权限。
- 团队角色不隐式授予仓库成员管理权；仓库 read/write/owner 关系仍独立校验。

移除或降级 TeamOwner 时，`TeamService` 在 serializable transaction 中检查同一团队是否还有其他 TeamOwner。没有其他 owner 时操作失败；系统管理员也不能绕过该不变量。

## 升级与回滚

升级前应备份数据库。SQLite、SQL Server 和 SonnetDB migrations 会先新增 `Role=Member`，再回填旧管理员为 TeamOwner，最后删除旧列，因此不会依赖邮箱、显示名或 namespace 字符串识别成员。

down migration 会把 TeamOwner 恢复为 `IsAdministrator=true`，其余三个角色恢复为 `false`。该降级会丢失 Leader 与 DeputyLeader 的区分；生产回滚仍建议恢复升级前数据库备份并部署上一版本应用。

## 验证

- 四级权限矩阵与角色管理层级单元测试；
- SQLite 从上一条 M13 migration 升级，验证旧 owner/member 的真实回填；
- 最后 TeamOwner 删除、降级拒绝，以及存在第二位 owner 时允许降级；
- SQLite、SQL Server、SonnetDB migration snapshot 无 pending model change；
- SQL Server idempotent migration SQL 包含默认值、回填、索引和 check constraint；
- 完整 `GitCandy.Data.Tests` 数据层测试。
