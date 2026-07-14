# M15 #161 远程账号连接与仓库发现

## 对应 ROADMAP

- Milestone 15 / #161：GitHub、GitLab、Gitee 绑定 UI。
- 本切片完成个人账号连接、测试、撤销和仓库发现，不执行 import、fetch、push、mirror job、webhook 或调度。

## 变更点

- 新增登录用户专用的 `/me/remotes`，支持选择已启用 Provider、提交 PAT 或已有 OAuth access token、声明 granted scopes、测试连接、刷新账号资料、分页发现仓库和撤销连接。
- GitHub、GitLab、Gitee adapter 通过 `IHttpClientFactory` 请求管理员固定的 API origin；不接受用户自定义 endpoint，不跟随 authenticated redirect，token 只进入授权 header，不进入 URL、进程参数、日志、EF entity 或 MVC 输出模型。
- Provider 只解析有界账号/仓库 JSON，单页最多 100 个仓库、响应最多 4 MiB，并把 credential、权限、限流、超时、网络、redirect 和无效响应分类为稳定错误码。
- `IRemoteConnectionService` 强制按当前 Identity user ID 查询和变更连接，使用 provider stable account/repository ID，应对远端登录名和仓库名变化。
- 连接生命周期写入脱敏 `CredentialAuditEvents`；仍被 `RepositoryMirrors` 使用的连接不能从 UI 撤销。

## 凭据边界

- token 仅在一次 POST 和受控 provider/vault 边界中存在。失败重绘会显式清除 secret 的 view model 与 ModelState；成功后 UI 不展示 token 或 opaque credential reference。
- 默认 vault 在持久化 Data Protection key ring 下创建 `remote-credentials/*.credential`，每个文件使用 GitCandy 专用 purpose 加密；Linux 文件权限收敛为 owner read/write。
- reference 使用 `dp-file:<guid>`，EF 只保存 reference、认证类型、scope 和到期元数据。撤销会用无 secret 的加密记录覆盖原文件，再删除 EF connection。
- Data Protection key ring 和 `remote-credentials` 必须按同一时间点备份。丢失 key ring 后不能恢复 token，应在远端撤销旧 token 并重新连接。

## Provider 配置

配置节为 `GitCandy:Remotes`，包含 `RequestTimeout` 以及 `GitHub`、`GitLab`、`Gitee` 的 `Enabled`、`ServerUrl` 和 `ApiBaseUrl`。正式环境只允许 HTTPS；loopback HTTP 仅供受控测试。自托管 GitLab/Gitee 必须由管理员设置固定 origin，普通用户不能覆盖以避免 SSRF。

当前 scope 基线：GitHub 仓库发现为 `repo`；GitLab 为 `read_api`；Gitee 为 `user_info, projects`。这份声明用于最小权限预检查，远端 API 测试仍是实际授权结果的依据。完整 OAuth consent、GitHub App installation 生命周期、token 过期/刷新、rename/delete webhook 和 rate-limit 运维属于 #168。

## 数据与兼容性

- 复用 #162 的 `RemoteAccountConnections`，不新增 migration，不改变现有公开 Web/Git HTTP/SSH/LFS URL。
- 首次连接会新增一条 remote connection、一份加密 credential 文件和一条脱敏审计记录；撤销连接会删除 remote connection 并保留已写审计证据。
- 远程仓库发现只读取元数据，不创建本地仓库，不写 Git refs，也不隐式同步 LFS、Issues、PR/MR、Wiki、Releases、CI 或 Packages。

## 回滚

1. 在旧版本回滚前从 `/me/remotes` 撤销不再使用的连接，并在对应 Provider 侧撤销 token。
2. 停止 GitCandy，恢复同一时间点的应用、数据库和 Data Protection key ring；不要混用旧数据库与较新的 credential 文件。
3. 若只回滚二进制而保留 #162 schema，旧版本不会使用连接记录，但远端 token 仍然有效，必须在 Provider 侧显式撤销。

## 验证

- Data：连接、stable identity、scope 拒绝、用户隔离、仓库发现、审计、撤销与既有 Remote/Mirror schema 测试。
- Web：三 Provider loopback API fixture、header-only credential、redirect suppression、分页/稳定 ID、4 MiB 边界、跨宿主 Data Protection 解密/撤销、失败页面 secret 清除和 `/me/remotes` 认证/MVC smoke。
- 本记录的最终构建、完整测试和未执行项以变更说明为准。
