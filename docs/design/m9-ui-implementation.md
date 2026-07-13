# M9 #096 / #100-#105 UI 实施记录

状态：`#096`、`#100-#105` 已完成。`#101` 所需代码树、提交与 diff 后端已由 M9 `#106-#108` 补齐，本文件下方的“当时尚无读取服务”仅保留为实施顺序记录。

对应 ROADMAP：Milestone 9 / `#096`、`#100`、`#101`、`#102`、`#103`、`#104`、`#105`

## 1. #096 前端资产管线

生产资产采用 npm lockfile + esbuild：

- `src/GitCandy/ClientApp/app.css` 和 `app.js` 是唯一生产入口。
- `esbuild` 仅作为构建依赖；`lucide` 在构建时打包，不连接外部 CDN。
- MSBuild 在输入变化时执行 `npm ci --ignore-scripts` 和 `npm run build`，输出到忽略提交的 `wwwroot/assets`。
- Docker 使用独立 Node 24 stage 生成资产，再复制到 .NET publish stage；最终运行镜像不包含 Node/npm。
- 发布包包含已经构建的 CSS/JavaScript，部署和运行不需要联网。

未选择 Vite，因为当前生产 UI 是 Razor MVC，不需要 SPA dev server、模块图和 HMR；未选择 WebOptimizer，因为 npm lockfile、Lucide tree-shaking 和 CSS/JavaScript 单入口由 esbuild 更直接地覆盖。新增维护面只有两个固定版本的开发依赖，不增加运行时服务。

本地构建要求 Node.js 20 或更高版本。单独重建资产：

```powershell
Set-Location src/GitCandy/ClientApp
npm ci --ignore-scripts
npm run build
```

## 2. #100 应用框架与主题

- 全局框架由 sticky header、权限感知 sidebar、主内容区和 footer 构成。
- `.GitCandy.Theme` 非敏感 cookie 仅接受 `system|light|dark`；Razor 在首屏输出 `data-theme`，无 JavaScript 时仍按保存模式渲染。
- System 通过 `prefers-color-scheme` 跟随操作系统；切换 Light/Dark 不刷新页面或改变 URL。
- 匿名用户的注册入口服从 `AllowRegisterUser`；Users、Teams、Settings 仍只对 administrator 显示。
- 原型中的全局搜索未进入生产 UI，因为当前没有带授权过滤的全局搜索服务。

## 3. #101-#103 垂直切片

仓库 UI 已迁移列表、空状态、可见性、详情、Smart HTTP clone URL、创建/编辑、用户/团队关系和删除确认。公开 route values、表单字段、antiforgery 和 repository policy 未改变。

本 UI 切片实施时，ASP.NET Core 主线还没有返回 tree、commit、diff 数据的 controller action 或应用服务，因此当时没有伪造 Code/Commits/Diff 页面。后续 M9 `#106-#108` 已按独立垂直切片补齐读取服务、页面和边界测试，`#101` 随之关闭。

账户 UI 覆盖登录、注册、资料、密码、Identity security、外部登录、恢复码和 SSH public key；团队/管理 UI 覆盖用户、团队、成员角色、团队关系和只读运行配置。认证 cookie、Identity、SSH key 校验和服务端授权没有改变。

## 4. #104 响应式与状态

- 桌面 sidebar 在 `860px` 以下变为抽屉；关闭时使用 `inert`，打开后锁定 Tab 焦点，支持 backdrop、`Escape` 和焦点返回。
- 所有交互控件具有 `:focus-visible`，主题/账户菜单使用原生 button/details 语义。
- 表格在窄屏允许受控横向滚动；短页面 footer 保持在视口底部。
- CSS 覆盖 `prefers-reduced-motion`；Light/Dark token 沿用 #090 已核查的 WCAG 对比度。
- 生产页面覆盖 empty、validation/error、access denied、destructive confirmation 和只读状态；loading 组件使用 `aria-busy` 时可复用 `.loading-state`。

## 5. #105 视觉基线与旧资产移除

Playwright CLI + Microsoft Edge 基线：

- [Light desktop 1440x900](../../tests/GitCandy.Tests/VisualBaselines/repositories-light-1440x900.png)
- [Dark desktop 1440x900](../../tests/GitCandy.Tests/VisualBaselines/repositories-dark-1440x900.png)
- [Light mobile 390x844](../../tests/GitCandy.Tests/VisualBaselines/repositories-light-390x844.png)
- [Dark mobile 390x844](../../tests/GitCandy.Tests/VisualBaselines/repositories-dark-390x844.png)

浏览器验证结果：四种组合均无水平溢出；Dark 刷新后保持；移动抽屉关闭时不可聚焦，打开后焦点进入首项，`Escape` 返回 menu button；console 为 0 errors / 0 warnings。

生产布局不再引用 Bootstrap 3、bootstrap-switch、jQuery 2、Glyphicons、marked 或旧 highlight.js，这些静态文件已删除。MVC smoke tests 会断言新资产存在且 HTML 不包含 Bootstrap/jQuery 引用。

## 6. 兼容性与回滚

本次不改变公开路由、Identity cookie、Git Basic Auth、Git HTTP/SSH header/streaming、数据库 schema 或文件系统布局。新增 `.GitCandy.Theme` 是无认证意义的显示偏好 cookie。

回滚方式：部署上一版本应用包即可恢复旧 Razor 与静态资产；无需数据库回滚。源码级回滚时必须同时恢复旧 `_Layout.cshtml`、`wwwroot/Content`、`wwwroot/Scripts`、`wwwroot/fonts` 和 smoke test 断言，避免布局引用不存在的文件。

## 7. 2026-07-11 验证记录

- `npm ci --ignore-scripts && npm run build`：通过，压缩 CSS/JavaScript 与 sourcemap 可重复生成。
- `node --check src/GitCandy/ClientApp/app.js`：通过。
- `dotnet build GitCandy.slnx --no-restore`：通过，0 warnings / 0 errors。
- `dotnet test GitCandy.slnx --no-restore`：通过，Data 41 项、Web/Core/Git/SSH 82 项。
- `MvcPageSmokeTests`：通过 3 项，覆盖新资产、主题首屏、匿名可见性和管理员 CRUD。
- Playwright CLI + Microsoft Edge：Light/Dark、`1440x900`/`390x844` 通过；无水平溢出，console 0 errors / 0 warnings。
- `docker build --tag gitcandy:ui-validation .`：未能完成；Docker Desktop 在读取 Dockerfile 前获取 Docker Hub `node:24-alpine` OAuth token 超时。已按本机代理流程重启并重试，失败点不变；Docker stage 内的相同 npm 构建命令已在主机通过。
