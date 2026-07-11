# M9 #106-#108 仓库生命周期与代码工作区

## 变更点

- 新增 `IRepositoryLifecycleService`，协调 bare 创建、credential-free remote import、同实例 fork、默认分支和安全删除；物理创建失败会补偿清理，删除先在 repository 根内隔离目录。
- `Repositories` 新增 nullable `ForkedFromRepository`、`ForkNetworkRoot`，SQLite 与 SQL Server 均有显式 migration。
- 新增 `IRepositoryBrowserService` 和稳定 DTO，controller 不直接操作 LibGit2Sharp。
- 实现 tree/blob/raw、commit history/detail、parent diff、blame、compare、branch/tag revision、ZIP archive 和固定 commit permalink。
- blob 页面支持 `#Lx-Ly` 选择和代码片段复制；highlight.js 仅打包常用语言模块。
- binary、未知编码、large blob、large diff、large archive、symlink、submodule 和非规范 Git path 均有显式边界。

## 对应 ROADMAP

- Milestone 9 / #101、#106、#107、#108。

## 测试说明

- `RepositoryLifecycleServiceTests`：fork network、对象复制、默认分支、安全删除、非法 import source。
- `RepositoryBrowserServiceTests`：tree/blob/raw/commit/diff/blame/compare/archive、symlink、submodule、binary、未知编码、大文件和路径逃逸。
- `MvcPageSmokeTests`：表单创建真实 bare repository、空 tree、删除元数据和物理目录。
- `GitLfsIntegrationTests` 同时访问真实 tree/blob/raw/commits/commit/blame/compare/archive 页面，覆盖固定 SHA、行锚点和 Kestrel archive streaming。

## 兼容与迁移

- 保留 `/Repository/{action}/{name}/{**path}` 公开路由；revision 使用可选 query string，旧 tree/blob/commit URL 继续匹配。
- 新建 repository 不再是 metadata-only，会同步创建 `{name}.git` bare 目录。旧 `{name}` 与 `{name}.git` 布局仍可读取。
- migration 只增加两个 nullable fork 字段，不改写现有 repository 行。
- remote import 不接受 file URL、URL 内凭据或非 HTTP(S)/SSH/Git scheme；fork 仍在 Web 边界执行 source repository read authorization。

## 回滚

- 应用可回滚到上一版本；新增 nullable 列不会影响旧二进制读取。
- 若必须回滚 schema，先确认没有需要保留的 fork network 元数据，再执行对应 migration `Down` 删除两列。
- 回滚 UI/读取服务不会删除 Git objects；新建仓库仍是普通 bare repository，可由旧 Smart HTTP/SSH 路径继续服务。
