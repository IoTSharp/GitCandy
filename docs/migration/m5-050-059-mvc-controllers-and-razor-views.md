# M5 #050-#059 MVC Controllers 和 Razor Views 迁移

## 对应 ROADMAP

- Milestone 5：MVC Controllers 和 Razor Views 迁移。
- 垂直切片：账户、团队、仓库元数据 CRUD 页面，Razor、本地化和 Bootstrap 3 静态资源。

## 变更点

- 将 `AccountController`、`HomeController`、`TeamController`、`RepositoryController` 和 `SettingController` 迁移到 `Microsoft.AspNetCore.Mvc.Controller`。
- 以 `IUserAdministrationService`、`ITeamService`、`IRepositoryManagementService` 承载 EF Core/Identity 查询和写入，Controller 只负责 HTTP、模型验证和授权编排。
- 使用 `NotFound()`、`Challenge()`、`Forbid()`、`Request.Headers`、`Request.Host`、`Response.Headers` 和 `Response.Cookies.Append` 替换旧 `System.Web` API。
- 保持 `/Account/{action}/{name?}`、`/Team/{action}/{name?}`、`/Repository/{action}/{name?}/{*path}` 和 `/Setting/{action}` 路由形状；`/` 按旧行为重定向到 `/Repository`。
- 所有删除、成员和协作者变更改为 antiforgery 保护的 POST，不再接受 GET 删除确认参数。
- 增加 `_ViewImports.cshtml`，页面改用 Tag Helper 和强类型 view model，不迁入 `Views/Web.config`、`MvcHtmlString` 或旧 HTML helper。
- 将旧 `App_GlobalResources/SR*.resx` 迁移到 `Resources/SharedResource*.resx`，语言切换使用标准 `.AspNetCore.Culture` cookie，同时临时写入旧 `Lang` cookie。
- 将 Bootstrap 3、bootstrap-switch、jQuery、highlight.js、marked、common.js 和 Glyphicon 字体迁到 `wwwroot`，布局使用直接静态引用，不引入前端构建链。
- 账户和团队公开详情中的仓库名称按当前查看者权限过滤，匿名用户不会看到私有仓库名称。
- 设置页迁为只读配置视图；配置写回、restart 和 host key 再生成继续归属 M8/M7，Web 请求不会直接改写 `appsettings.json` 或卸载进程。

## Git 边界

- M5 只创建、更新和删除 EF Core 仓库元数据及授权关系。
- bare repository 创建、导入、仓库文件系统删除、cache 删除和 Git helper 执行必须在 M6 经 `IGitTransportBackend` 受控实现。
- `/Repository/Tree/{name}` 在 Git 浏览服务迁移前兼容重定向到仓库详情；Git Smart HTTP 路由仍返回 M6 占位响应。

## URL 与资源兼容

- 布局中的登录 `returnUrl` 使用 `PathBase + Path + QueryString` 生成，不再依赖 `Request.Url.PathAndQuery`。
- `/Home/Language?lang=zh-cn|en-us|fr-fr` 支持本地 return URL，并拒绝未知 culture 和外部重定向。
- 旧 bundle 内容改为以下直接引用：`/Content/*.css`、`/Scripts/*.js`、`/fonts/*`。

## 测试说明

- `MvcPageSmokeTests` 使用真实 Kestrel、SQLite、Identity cookie 和 antiforgery token 验证：
  - 匿名仓库列表只显示公开仓库。
  - 账户和团队详情不泄漏私有仓库名称。
  - 管理员账户列表和设置页可打开。
  - 仓库/团队表单可创建、编辑、删除，错误输入显示验证信息。
  - 中文 culture cookie 和中文资源生效。
  - Bootstrap、bootstrap-switch、highlight、marked、common.js 和字体静态路径返回成功。
- 已运行：`dotnet build GitCandy.slnx -c Release --no-restore`。
- 已运行：`dotnet test GitCandy.slnx -c Release --no-restore`。
- Debug build 未作为最终证据：已有用户启动的 `GitCandy` 进程占用 Debug 输出 DLL；未终止该进程，改用独立 Release 输出完成验证。
- 未运行 Git HTTP clone/fetch/push 和 SSH clone/fetch/push：本变更不实现 M6/M7 协议后端。

## 是否破坏兼容

- 公开页面路由和 Bootstrap 3 资产路径保持兼容。
- 删除操作由 GET 改为 POST，这是有意的安全收紧；旧书签中的 `?Conform=Yes` 不再执行删除。
- 语言切换新增标准 culture cookie，并暂时保留旧 `Lang` cookie。
- 回滚方式：回退 M5 Controller/View/Service/Resource 变更即可；本切片不新增 migration，不修改数据库 schema。
