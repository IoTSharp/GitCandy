# M0 #007 MVC smoke test 基线

记录日期：2026-07-09

## 验收结论

- 已新增可重复运行的 MVC Web smoke 脚本：`tools/migration/m0-007/Invoke-M0MvcSmokeTests.ps1`。
- smoke 覆盖旧 ASP.NET MVC 5 站点的首页重定向、仓库列表、登录页、登录失败 POST、注册表单、可选公开仓库 Tree 页面、404 错误页。
- 提供管理员凭据时，脚本还会覆盖登录后主要表单入口：仓库创建、团队创建、设置页、用户列表和登出重定向。
- 本任务不修改旧 MVC5 运行代码，不迁移 controller/view，也不改变数据库、认证、Git HTTP/SSH 或文件系统布局。

## 运行前置条件

先启动旧 ASP.NET MVC 5 GitCandy 站点，确认目标 URL 可以从本机访问。脚本是外部黑盒 HTTP smoke test，不负责启动 IIS Express、IIS 或 ASP.NET Development Server。

推荐使用 M0 #001 的样例语义准备测试数据：

- `admin`：系统管理员，用于登录后表单 smoke。
- `public-demo`：公开仓库，用于可选仓库 Tree 页面 smoke。

不要把真实密码写入仓库文件。管理员密码可以通过本机环境变量传入：

```powershell
$env:GITCANDY_M0_ADMIN_PASSWORD = '<local-admin-password>'
```

## 基础命令

只跑匿名和公开页面 smoke：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\migration\m0-007\Invoke-M0MvcSmokeTests.ps1 `
  -BaseUrl http://localhost:<port>
```

带管理员凭据和公开仓库 Tree smoke：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\migration\m0-007\Invoke-M0MvcSmokeTests.ps1 `
  -BaseUrl http://localhost:<port> `
  -AdminUser admin `
  -AdminPassword $env:GITCANDY_M0_ADMIN_PASSWORD `
  -PublicRepository public-demo
```

结果默认写入：

- `artifacts/migration/m0-007/mvc-smoke-results.json`

`artifacts/` 已由 `.gitignore` 排除，结果文件不提交。

## 覆盖场景

| 编号 | 场景 | 期望 |
| --- | --- | --- |
| MVC-001 | GET `/` | 302/等价重定向到 `/Repository/Index`。 |
| MVC-002 | GET `/Repository/Index` | 200，页面包含仓库列表表格和匿名登录入口。 |
| MVC-003 | GET `/Account/Login?ReturnUrl=/Repository/Index` | 200，页面包含 `ID`、`Password` 和提交按钮。 |
| MVC-004 | POST `/Account/Login` 使用错误账号密码 | 200，仍返回登录表单，不设置成功登录跳转。 |
| MVC-005 | GET `/Account/Create` | 默认公开注册配置下返回 200，并包含用户创建字段；若配置禁用注册则记录为 skipped。 |
| MVC-006 | GET `/Repository/Tree/{publicRepository}` | 传入 `-PublicRepository` 时返回 200，显示仓库名和 HTTP Git URL。 |
| MVC-007 | GET 随机不存在页面 | 返回 404 和错误响应体。 |
| MVC-008 | POST `/Account/Login` 使用管理员凭据 | 提供管理员密码时重定向到 `/Repository/Index`。 |
| MVC-009 | GET `/Repository/Create` | 管理员登录后返回 200，并包含仓库创建字段。 |
| MVC-010 | GET `/Team/Create` | 管理员登录后返回 200，并包含团队创建字段。 |
| MVC-011 | GET `/Setting/Edit` | 管理员登录后返回 200，并包含核心配置字段。 |
| MVC-012 | GET `/Account/Index` | 管理员登录后返回 200，用户列表可打开。 |
| MVC-013 | GET `/Account/Logout?ReturnUrl=/Repository/Index` | 重定向回仓库列表。 |

## 错误页说明

旧 `Application_Error` 的错误响应优先使用 `GitCandy/CustomErrors/{status}.html`，找不到状态专用文件时使用 `000.html`。但 `LocalSkipCustomError=true` 且请求被 ASP.NET 判定为本地请求时，旧代码会跳过自定义错误页。

因此脚本默认只硬断言：

- HTTP 状态码是 404。
- 响应体非空。

如果要强制验证 `CustomErrors/404.html`，可以追加：

```powershell
-ExpectCustomErrors
```

此时响应体必须包含 `HTTP Error 404`。

## ASP.NET Core 迁移验收要求

后续 M1/M5 迁移到 ASP.NET Core MVC 时，至少要保留或明确说明以下行为：

- `/` 仍进入仓库列表，或者有明确兼容重定向说明。
- `/Repository/Index` 可匿名打开公开仓库列表。
- `/Account/Login` 保持本地 `ReturnUrl` 登录流。
- 登录失败不会跳转到成功页，也不会写入有效登录 cookie。
- 注册、仓库创建、团队创建、设置页和用户列表的权限边界有 smoke 覆盖。
- 错误页返回正确 HTTP 状态码，不把内部异常、物理路径、token 或 header 泄漏给普通用户。

## 兼容性影响

本任务只新增 smoke 脚本、迁移文档并更新路线图状态，不改变：

- 公开路由和 Git URL。
- 数据库 schema、索引、默认值或 seed 行为。
- Identity cookie、Basic Auth、SSH public key 或权限语义。
- 配置键、环境变量或部署方式。
- Git HTTP/SSH 协议行为和响应 header。
- repository、cache、App_Data、host keys、logs 等文件系统布局。
