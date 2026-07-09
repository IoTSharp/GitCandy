# M0 #002 Web 行为清单

记录日期：2026-07-09

## 验收结论

- 已记录旧 ASP.NET MVC 5 Web 行为基线，覆盖登录、登出、注册、改密码、用户/团队/仓库 CRUD、仓库浏览页面行为。
- 本清单作为后续 ASP.NET Core MVC 迁移的行为保护网。迁移实现可以采用 ASP.NET Core Identity 和新的服务分层，但公开 URL、权限语义、主要页面结果和错误行为应能对照本文件说明差异。
- 本任务只做静态行为梳理，没有修改旧 MVC5 运行代码，也没有启动旧 Web 站点做浏览器 smoke test。可重复自动化 smoke test 由 M0 #007 继续补齐。
- Git Smart HTTP clone/fetch/push 行为不在本文件展开，由 M0 #003 和 M0 #008 覆盖。SSH clone/fetch/push 行为由 M0 #004 覆盖。

## 行为来源

主要读取的旧实现：

- `GitCandy/App_Start/RouteConfig.cs`
- `GitCandy/App_Start/FilterConfig.cs`
- `GitCandy/Global.asax.cs`
- `GitCandy/Controllers/CandyControllerBase.cs`
- `GitCandy/Controllers/AccountController.cs`
- `GitCandy/Controllers/TeamController.cs`
- `GitCandy/Controllers/RepositoryController.cs`
- `GitCandy/Filters/*.cs`
- `GitCandy/Models/UserModel.cs`
- `GitCandy/Models/TeamModel.cs`
- `GitCandy/Models/RepositoryModel.cs`
- `GitCandy/Data/MembershipService.cs`
- `GitCandy/Data/RepositoryService.cs`
- `GitCandy/Views/Account/*.cshtml`
- `GitCandy/Views/Team/*.cshtml`
- `GitCandy/Views/Repository/*.cshtml`
- `GitCandy/Views/Shared/_Layout.cshtml`

## 全局 Web 行为

### 路由

| URL 模式 | Controller/Action | 备注 |
| --- | --- | --- |
| `/` | `Home/Index` | 调用 `RedirectToStartPage()`，默认重定向到 `/Repository/Index`。 |
| `/Account/{action}/{name}` | `AccountController` | `name` 用于用户名，多个 action 支持为空时取当前用户。 |
| `/Team/{action}/{name}` | `TeamController` | `name` 用于团队名。 |
| `/Repository/{action}/{name}/{*path}` | `RepositoryController` | `name` 用于仓库名，`path` 用于 ref、commit、目录和文件路径。 |
| `/Setting/{action}` | `SettingController` | M0 #002 不展开设置页，仅记录它在管理员导航中出现。 |
| `/{controller}/{action}/{id}` | 默认路由 | 默认值为 `Home/Index`。 |

Git HTTP 路由 `git/{project}.git/{*verb}` 和 `git/{project}/{*verb}` 属于 M0 #003。

### 导航与布局

- 顶部品牌链接指向 `/`。
- 所有人都能看到 `Repositories` 和 `About` 导航。
- 系统管理员登录后额外看到 `Users`、`Teams`、`Settings`。
- 匿名用户右侧显示 `Register` 和 `Login`；`Login` 链接带当前 `PathAndQuery` 作为 `ReturnUrl`。
- 登录用户右侧显示昵称链接，指向自己的账户详情页，并显示 `Logout` 链接；`Logout` 同样带当前 `PathAndQuery` 作为 `ReturnUrl`。
- 页脚语言菜单支持 `zh-cn`、`en-us`、`fr-fr`，请求 `Home/Language?lang=...` 后写入 `Lang` cookie，清空 session 中的 culture，并回到来源页面或起始页。

### 认证 cookie 与授权

- 旧 Web 登录使用自定义 `_gc_auth` cookie，值为授权 GUID 的 base64 字节串。
- 写入登录 cookie 时设置 `HttpOnly=true` 和授权过期时间。
- 登出、过期或无效 token 会把 `_gc_auth` 设置为 1980-01-01 过期，并 `Session.Abandon()`。
- token 会缓存在 `HttpRuntime.Cache`，数据库表 `AuthorizationLog` 保存有效性、过期时间、签发 IP 和最近 IP。
- 迁移目标不兼容旧 `_gc_auth`、旧 `AuthorizationLog` 或旧密码 hash；本节仅用于旧行为基线。

### 全局授权结果

旧 `SmartAuthorizeAttribute.HandleUnauthorizedRequest` 的结果：

| 场景 | 结果 |
| --- | --- |
| 匿名用户访问需登录页面 | 302 到 `/Account/Login`，非根路径带 `ReturnUrl`。 |
| 已登录系统管理员访问不被允许的资源 | 抛出 404 `HttpException`。 |
| 已登录普通用户访问不被允许的资源 | 抛出 `UnauthorizedAccessException`。 |

`PublicServerAttribute` 是全局 filter：

- `IsPublicServer=true` 时，不额外要求整站登录。
- `IsPublicServer=false` 时，除 `AllowAnonymous` 和 `SmartGit` action 外，其他 action 都要求登录。

### 其他全局请求行为

- 若 `ForceSsl=true` 且请求不是 HTTPS，请求会重定向到 `https://{host}:{SslPort}{PathAndQuery}`。
- 每个 action 前会写响应头 `X-GitCandy-Version`。
- 本地请求在 `LocalSkipCustomError=true` 时可跳过自定义错误页。
- 没有在旧 controller 或 view 中发现 `AntiForgeryToken` 或 `ValidateAntiForgeryToken`。迁移到 ASP.NET Core 时若补上 CSRF 防护，需要在兼容性说明中标明这是安全增强，不是旧行为。

## 账户行为

### 注册

| 行为 | 旧实现 |
| --- | --- |
| GET | `/Account/Create`，匿名可访问，但受 `AllowRegisterUser` 控制；系统管理员始终可访问。 |
| POST | `/Account/Create`，提交 `Name`、`Nickname`、`Password`、`ConformPassword`、`Email`、`Description`。 |
| 成功 | 若提交者已登录，重定向到新用户 `/Account/Detail/{name}`；匿名注册成功后创建授权并进入起始页。 |
| 失败 | 表单原样返回；用户名重复给 `Name` 加模型错误，邮箱重复给 `Email` 加模型错误。 |

字段约束：

| 字段 | 约束 |
| --- | --- |
| `Name` | 必填，2 到 20 字符，正则 `(?i)^[a-z][a-z0-9\-_]+$`。 |
| `Nickname` | 2 到 20 字符，允许空字符串。 |
| `Email` | 必填，最长 50 字符，旧 email 正则校验。 |
| `Password` | 必填，6 到 100 字符。 |
| `ConformPassword` | 必填，6 到 100 字符，必须等于 `Password`。 |
| `Description` | 最长 500 字符，允许空字符串。 |

### 登录

| 行为 | 旧实现 |
| --- | --- |
| GET | `/Account/Login?ReturnUrl=...`。如果已经登录，直接进入 `ReturnUrl` 或仓库列表。 |
| POST | 提交 `ID` 和 `Password`；`ID` 可以是用户名或邮箱。 |
| 成功 | 创建授权记录，设置 `_gc_auth` cookie，然后进入本地 `ReturnUrl` 或 `/Repository/Index`。 |
| 失败 | 返回登录页，添加 `Account_LoginFailed` 模型错误。 |

`RedirectToStartPage()` 只接受本地 URL。空、非本地或根路径 fallback 到 `/Repository/Index`。

### 登出

- GET `/Account/Logout?ReturnUrl=...`。
- 允许匿名访问。
- 清空当前 token、使当前授权失效、删除缓存 token、废弃 session。
- 重定向到本地 `ReturnUrl`；无有效 `ReturnUrl` 时进入 `/Repository/Index`。

### 改密码

| 行为 | 旧实现 |
| --- | --- |
| GET | `/Account/Change/{name?}`，当前用户或系统管理员可访问。 |
| POST | 提交 `OldPassword`、`NewPassword`、`ConformPassword`。 |
| 默认用户 | `name` 为空时使用当前登录用户名。 |
| 普通用户 | 只能修改自己，旧密码必须是自己的密码。 |
| 系统管理员改别人密码 | 旧密码校验管理员自己的密码。 |
| 成功 | 调用 `SetPassword`，旧授权全部失效；自己改密码时重新创建当前授权；重定向到账户详情。 |
| 失败 | 返回表单，旧密码错误时给 `OldPassword` 加模型错误。 |

新旧密码字段均为必填，6 到 100 字符；确认密码必须等于新密码。

### 用户列表和详情

| 页面 | 权限 | 行为 |
| --- | --- | --- |
| `/Account/Index?query=&page=` | 系统管理员 | 按用户名排序分页，`query` 匹配用户名、昵称、邮箱、描述；每页数量来自 `NumberOfItemsPerList`。 |
| `/Account/Detail/{name}` | 公有站点匿名可见，私有站点需登录 | 显示用户名、昵称、邮箱、可见仓库、团队、描述；系统管理员额外看到系统管理员标记。 |

账户详情中的可见仓库会按查看者过滤：公开仓库可见；私有仓库只在查看者是系统管理员、仓库用户角色可读或团队角色可读时出现。

### 编辑用户

| 行为 | 旧实现 |
| --- | --- |
| GET | `/Account/Edit/{name?}`，当前用户或系统管理员可访问；`name` 为空时编辑自己。 |
| POST | 提交隐藏 `Name`、`Password`、`Nickname`、`Email`、`Description`，系统管理员还可提交 `IsSystemAdministrator`。 |
| 密码校验 | 普通用户编辑自己时输入自己的密码；管理员编辑别人时输入管理员自己的密码。 |
| 成功 | 更新昵称、邮箱、描述和系统管理员标记；自己编辑后刷新当前 token；重定向到账户详情。 |
| 防自降级 | 系统管理员不能在编辑自己时移除自己的管理员身份。 |

编辑页移除了 `ConformPassword` 的模型校验，但 `Password` 仍按 `UserModel` 要求必填，6 到 100 字符。

### 删除用户

- GET `/Account/Delete/{name}` 显示确认页，仅系统管理员可访问。
- GET `/Account/Delete/{name}?Conform=Yes` 执行删除。
- 系统管理员不能删除自己，页面返回模型错误。
- 删除会清空该用户的团队角色、仓库角色、授权记录和 SSH key，然后重定向到 `/Account/Index`。
- 删除确认是 GET 行为，不是 POST。

### 账户附属 JSON 行为

| Endpoint | 权限 | 成功结果 | 失败结果 |
| --- | --- | --- | --- |
| POST `/Account/Search` with `query` | 公有站点匿名可访问，私有站点需登录 | 返回最多 10 个用户名字符串，按名称包含和匹配度排序。 | 未定义空 query 保护。 |
| GET `/Account/Ssh/{name?}` | 当前用户或系统管理员 | 显示当前 SSH key fingerprint 列表。 | 用户不存在时 404。 |
| POST `/Account/ChooseSsh` | 当前用户或系统管理员 | `act=add` 返回 fingerprint；`act=del` 返回 `"success"`。 | HTTP 400 和错误文本。 |

SSH key 的协议认证行为不在 M0 #002 展开。

## 团队行为

### 团队列表、创建、详情

| 页面 | 权限 | 行为 |
| --- | --- | --- |
| `/Team/Index?query=&page=` | 系统管理员 | 按团队名排序分页，`query` 匹配团队名和描述。 |
| GET `/Team/Create` | 系统管理员 | 显示创建表单。 |
| POST `/Team/Create` | 系统管理员 | 提交 `Name`、`Description`；成功后当前管理员成为团队管理员，并重定向到详情页。 |
| `/Team/Detail/{name}` | 公有站点匿名可见，私有站点需登录 | 显示团队名、成员、可见仓库和描述。 |

字段约束：

| 字段 | 约束 |
| --- | --- |
| `Name` | 必填，2 到 20 字符，正则 `(?i)^[a-z][a-z0-9\-_]+$`。 |
| `Description` | 最长 500 字符，允许空字符串。 |

团队详情中的仓库列表同样会按查看者过滤私有仓库。

### 编辑和删除团队

| 行为 | 旧实现 |
| --- | --- |
| GET `/Team/Edit/{name}` | 团队管理员或系统管理员可访问。 |
| POST `/Team/Edit/{name}` | 更新描述；成功后仍返回编辑页，不重定向。 |
| GET `/Team/Delete/{name}` | 系统管理员显示确认页。 |
| GET `/Team/Delete/{name}?Conform=Yes` | 系统管理员执行删除并重定向到团队列表。 |

旧 view 只有系统管理员会在团队详情页看到 `Edit` 和 `Members` 按钮，但 controller 允许团队管理员直接访问编辑和成员管理 URL。

### 团队成员管理

- GET `/Team/Users/{name}`：团队管理员或系统管理员可访问，显示成员 chooser。
- POST `/Team/ChooseUser`：团队管理员或系统管理员可访问。

| `act` | 行为 | 成功结果 |
| --- | --- | --- |
| `add` | 添加用户为普通团队成员。 | JSON `"success"`。 |
| `del` | 移除用户。非系统管理员不能移除自己。 | JSON `"success"`。 |
| `admin` | 设置成员为团队管理员。 | JSON `"success"`。 |
| `member` | 设置成员为普通成员。非系统管理员不能把自己降级。 | JSON `"success"`。 |

失败时返回 HTTP 400，正文为具体错误或 `Shared_SomethingWrong`。

### 团队搜索

- POST `/Team/Search` with `query`。
- 返回最多 10 个团队名字符串，按名称包含和匹配度排序。
- 与账户搜索相同，公有站点匿名可访问，私有站点需登录。

## 仓库 CRUD 行为

### 仓库列表

GET `/Repository/Index`：

| 访问者 | `Collaborations` | `Repositories` | Create 按钮 |
| --- | --- | --- | --- |
| 匿名 | 空数组 | 所有 `IsPrivate=false` 仓库 | 不显示 |
| 普通登录用户 | 当前用户或团队拥有可读且可写角色的仓库 | 公开仓库中不属于 collaborations 的仓库 | `AllowRepositoryCreation=true` 时显示 |
| 系统管理员 | 当前用户或团队拥有可读且可写角色的仓库 | 所有不属于 collaborations 的仓库 | 始终显示 |

列表项显示仓库名链接到 `Repository/Tree`，并显示描述。

### 创建仓库

| 行为 | 旧实现 |
| --- | --- |
| GET | `/Repository/Create`，登录用户需 `AllowRepositoryCreation=true`，系统管理员不受该开关限制。 |
| 默认值 | `IsPrivate=false`，`AllowAnonymousRead=true`，`AllowAnonymousWrite=false`。 |
| POST | 提交 `Name`、`IsPrivate`、`AllowAnonymousRead`、`AllowAnonymousWrite`、`Description`、`HowInit`、`RemoteUrl`。 |
| 初始化 | `HowInit=Import` 时把 `RemoteUrl` 传给 Git 创建逻辑；其他值创建空仓库。 |
| 成功 | 创建数据库仓库记录，创建 Git repository，当前用户成为 owner/read/write；重定向到仓库详情。 |
| Git 创建失败 | 删除刚创建的数据库仓库记录，返回创建页。 |
| 重名 | 给 `Name` 加 `Repository_AlreadyExists` 模型错误。 |

字段约束：

| 字段 | 约束 |
| --- | --- |
| `Name` | 必填，2 到 50 字符，正则 `(?i)^[a-z][a-z0-9\-\._]+(?<!\.git)$`，不能以 `.git` 结尾。 |
| `Description` | 最长 500 字符，允许空字符串。 |

### 仓库详情、编辑、协作、删除

| 页面 | 权限 | 行为 |
| --- | --- | --- |
| `/Repository/Detail/{name}` | 仓库可读 | 显示仓库名、私有标记、匿名读写标记、默认分支、描述、用户协作者、团队协作者。 |
| GET `/Repository/Edit/{name}` | 仓库 owner 或系统管理员 | 显示私有标记、匿名读写标记、默认分支下拉、描述；仓库名不可编辑。 |
| POST `/Repository/Edit/{name}` | 仓库 owner 或系统管理员 | 更新私有标记、匿名读写标记、描述，并设置 HEAD 默认分支；成功重定向到详情页。 |
| `/Repository/Coop/{name}` | 仓库 owner 或系统管理员 | 显示用户和团队协作者 chooser。 |
| GET `/Repository/Delete/{name}` | 仓库 owner 或系统管理员 | 显示删除确认页。 |
| GET `/Repository/Delete/{name}?Conform=Yes` | 仓库 owner 或系统管理员 | 删除数据库仓库记录、Git repository 和 Git cache，记录日志，重定向到仓库列表。 |

仓库详情页只有 owner 或系统管理员会看到 `Edit` 和 `Relationship` 按钮。

### 仓库协作者 JSON 行为

POST `/Repository/ChooseUser`：

| `act` | 行为 | 成功结果 | 自保护 |
| --- | --- | --- | --- |
| `add` | 添加用户协作者，默认 `AllowRead=true`、`AllowWrite=true`、`IsOwner=false`。 | 返回 `{ AllowRead, AllowWrite, IsOwner }`。 | 无 |
| `del` | 删除用户协作者。 | JSON `"success"`。 | 非系统管理员不能删除自己。 |
| `read` | 设置用户 `AllowRead`。 | JSON `"success"`。 | 无 |
| `write` | 设置用户 `AllowWrite`。 | JSON `"success"`。 | 无 |
| `owner` | 设置用户 owner。 | JSON `"success"`。 | 非系统管理员不能把自己的 owner 置为 false。 |

POST `/Repository/ChooseTeam`：

| `act` | 行为 | 成功结果 |
| --- | --- | --- |
| `add` | 添加团队协作者，默认 `AllowRead=true`、`AllowWrite=true`。 | 返回 `{ AllowRead, AllowWrite }`。 |
| `del` | 删除团队协作者。 | JSON `"success"`。 |
| `read` | 设置团队 `AllowRead`。 | JSON `"success"`。 |
| `write` | 设置团队 `AllowWrite`。 | JSON `"success"`。 |

JSON 操作失败时返回 HTTP 400 和 `Shared_SomethingWrong` 或自保护错误文本。

## 仓库浏览行为

所有仓库浏览 action 都使用 `ReadRepositoryAttribute`：

- 匿名用户只能访问 `AllowAnonymousRead=true` 的仓库。
- 登录用户可通过直接用户角色、团队角色或系统管理员身份读取仓库。
- 删除 branch/tag 的 POST 需要写权限。
- 不可读时，匿名用户重定向到登录页，普通登录用户抛出未授权，系统管理员访问不存在资源时得到 404。

### Tree

GET `/Repository/Tree/{name}/{*path}`：

- `path` 为空或根目录时显示仓库根。
- `path` 通常是 `{ref}` 或 `{ref}/{file-or-directory-path}`。
- 如果 Git 层返回没有 entries 且 `ReferenceName != "HEAD"`，会重定向到规范化后的 `ReferenceName`。
- 空仓库显示 Git URL 按钮和空仓库提示。
- 根目录显示描述、commit/branch/tag/contributor 数量、branch/tag selector、zip 下载、compare 按钮和 Git URL。
- 目录列表显示 entry 图标、名称、最近 commit、作者和日期。
- `Tree` entry 链接继续到 `Tree`，`Blob` entry 链接到 `Blob`，`GitLink` 只显示名称不跳转。
- 根目录有 README 时内嵌预览。

### Blob、Raw、Blame

| 页面 | 行为 |
| --- | --- |
| `/Repository/Blob/{name}/{*path}` | 显示 path bar、branch selector、commit 摘要、作者、大小、History/Blame/Raw 按钮和 blob 预览。 |
| `/Repository/Raw/{name}/{*path}` | 返回文件原始内容；二进制使用 `FileHelper.BinaryMimeType` 并带文件名，文本直接返回 raw data。 |
| `/Repository/Blame/{name}/{*path}` | 显示每个 hunk 的 commit、跳转 blame、作者、日期、消息和代码；提供 History、NormalView、Raw。 |

Blob 预览类型：

| 类型 | 行为 |
| --- | --- |
| Text | `<pre><code>` 代码块，带语法 brush。 |
| MarkDown | `<div id="md">`，由前端 markdown 脚本渲染。 |
| Image | `<img>`，源地址是 Raw URL。 |
| Binary | 显示二进制提示和 Raw 链接。 |

### Commit、Commits、Compare

| 页面 | 行为 |
| --- | --- |
| `/Repository/Commits/{name}/{*path}?page=` | 显示指定 path 的 commit 列表，按 `NumberOfCommitsPerPage` 分页，包含消息、作者、时间、短 SHA 和 Tree 链接。 |
| `/Repository/Commit/{name}/{*path}` | 显示完整 commit message、作者/提交者、短 SHA、Tree 链接、父 commit 列表和 diff。 |
| `/Repository/Compare/{name}/{*path}` | `path` 解析为 `start...end`；如果没有 `...`，只设置 start。分支名中的 `;` 会替换为 `/`。显示 base/compare branch selector、提交列表和 diff；无差异时显示空比较文案。 |

### Branches、Tags、Contributors、Archive

| 页面 | 行为 |
| --- | --- |
| GET `/Repository/Branches/{name}` | 显示默认分支和其他分支相对 ahead/behind。HEAD 未设置时显示提示。可写用户或系统管理员看到删除按钮。 |
| POST `/Repository/Branches/{name}` | 需要写权限，提交 `path` 删除 branch，返回 JSON `"success"`。 |
| GET `/Repository/Tags/{name}` | 显示 tag 列表、日期、消息、commit 链接和 zip 下载。可写用户或系统管理员看到删除按钮。 |
| POST `/Repository/Tags/{name}` | 需要写权限，提交 `path` 删除 tag，返回 JSON `"success"`。 |
| GET `/Repository/Contributors/{name}/{*path}` | 显示当前 ref contributor commit 数，以及默认分支和当前 ref 的 commit、contributor、file、source size、repository size。 |
| GET `/Repository/Archive/{name}/{*path}?eol=` | 返回 zip 文件；`eol` 支持 `LF`、`CR`、`CRLF`，非法值按默认换行处理；文件名为 `{repo}-{ref}[-{eol}].zip`。 |

### Git URL 展示

Tree 根页面和空仓库页面显示 Git URL 按钮：

- HTTP URL：当前请求 scheme/host/port + `~/git/{name}.git`。
- SSH URL 仅在 `EnableSsh=true` 时显示。
- `SshPort=22` 时 SSH URL 为 `git@{host}:git/{name}.git`。
- 非 22 端口时 SSH URL 为 `ssh://git@{host}:{port}/git/{name}.git`。

## M0 样例数据 smoke 场景建议

这些场景应在 M0 #007 或后续 Playwright/integration smoke test 中自动化。样例用户和仓库来自 M0 #001。

| 编号 | 场景 | 期望 |
| --- | --- | --- |
| WEB-001 | 匿名访问 `/` | 302 到 `/Repository/Index`。 |
| WEB-002 | 匿名访问 `/Repository/Index` | 只看到公开仓库，不显示创建按钮。 |
| WEB-003 | 匿名访问公开仓库 `/Repository/Tree/public-demo` | 200，显示根目录、Git URL、README 或文件列表。 |
| WEB-004 | 匿名访问私有仓库 `/Repository/Tree/private-demo` | 重定向到 `/Account/Login?ReturnUrl=...`。 |
| WEB-005 | 登录失败 | 返回登录页并显示登录失败模型错误。 |
| WEB-006 | 登录 `alice` 后访问本地 ReturnUrl | 设置登录 cookie 并回到 ReturnUrl。 |
| WEB-007 | 登出 | 清空登录 cookie/session 并回到本地 ReturnUrl 或仓库列表。 |
| WEB-008 | 允许注册时匿名注册新用户 | 创建用户并进入起始页；用户名或邮箱重复时回到表单。 |
| WEB-009 | `alice` 修改自己的密码 | 旧密码正确时成功并重新登录，旧密码错误时留在表单。 |
| WEB-010 | `admin` 查看用户列表、创建用户、编辑用户、删除非自己用户 | 列表分页和 CRUD 行为符合本文件。 |
| WEB-011 | `admin` 创建团队，添加 `bob`，设置/取消管理员 | JSON 返回 success，自我移除和自我降级保护生效。 |
| WEB-012 | `alice` 创建仓库 | 默认公开、匿名可读、匿名不可写，成功后进入详情页。 |
| WEB-013 | `alice` 编辑自己 owner 的仓库 | 可更新私有标记、匿名读写和默认分支。 |
| WEB-014 | `alice` 在仓库协作页添加 `bob` 或 `core` 团队 | JSON 返回默认 read/write 权限，可切换权限。 |
| WEB-015 | 非 owner 普通用户直接访问仓库编辑页 | 未授权或登录跳转结果符合全局授权结果。 |
| WEB-016 | 仓库浏览页 `Tree`、`Blob`、`Raw`、`Blame`、`Commits`、`Commit`、`Compare`、`Branches`、`Tags`、`Contributors`、`Archive` | 公开仓库可读用户均可访问；不存在 ref/path 返回 404。 |
| WEB-017 | 可写用户删除临时 branch/tag | POST 返回 JSON `"success"`，无写权限用户失败。 |

## 兼容性影响

本任务只新增迁移文档并更新路线图状态，不改变：

- 公开路由和 Git URL。
- 数据库 schema、索引、默认值或 seed 行为。
- Identity cookie、Basic Auth 或权限语义。
- 配置键、环境变量或部署方式。
- Git HTTP/SSH 协议行为和响应 header。
- repository、cache、App_Data、host keys、logs 等文件系统布局。
