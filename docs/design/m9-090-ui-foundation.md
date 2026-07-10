# M9 #090 UI 信息架构与双主题原型基线

状态：进行中

对应 ROADMAP：Milestone 9 / `#090`

生产代码边界：本阶段不修改 `src/GitCandy/Views`、生产 CSS/JavaScript、公开路由或认证授权行为。

## 1. 目标

在逐页实现 UI 前冻结 GitCandy 的信息架构、浅色/深色主题语义、整体应用框架和原型验收契约，避免在 Razor 页面中边做视觉设计边决定导航、组件和状态。

本阶段交付：

- 页面、角色和关键状态矩阵。
- Light/Dark 共用的 semantic design tokens。
- 桌面与移动端应用框架规则。
- 可直接在浏览器打开的交互式框架原型。
- 后续 `#096`、`#100` 到 `#105` 的实现边界、兼容性和回滚约束。

本阶段不交付：

- 不替换 Bootstrap 3 或现有 Razor 标记。
- 不选择或安装 npm/Vite/esbuild 等生产资产管线。
- 不改变 Identity、Git Basic Auth、SSH、数据库或权限语义。
- 不把原型中的示例数据、文案或交互直接视为生产实现。

## 2. 产品受众与工作模式

GitCandy 是面向开发者和管理员的轻量自托管 Git 工作台。界面应安静、紧凑、可重复操作，优先支持扫描、比较和快速进入仓库，不采用营销站式 hero、装饰性大卡片或低信息密度布局。

| 角色 | 首要任务 | 导航/操作边界 |
| --- | --- | --- |
| 匿名用户 | 查看公开仓库、登录或注册 | 不显示私有仓库和管理入口 |
| 普通用户 | 访问有权限的仓库、管理自己的账户和 SSH key | 不显示系统管理入口 |
| Repository owner | 管理仓库元数据、协作者和团队权限 | 仓库上下文中显示管理操作 |
| Team administrator | 管理团队成员及团队仓库关系 | 团队上下文中显示成员操作 |
| Administrator | 管理用户、团队、设置和所有仓库 | 全局导航中显示 Administration |

所有隐藏入口只改善界面噪声；服务端仍必须独立执行授权。

## 3. 信息架构

### 3.1 全局层级

```text
GitCandy
├── Repositories
│   ├── Repository list
│   └── Repository workspace
├── Teams
├── Administration
│   ├── Users
│   ├── Teams
│   └── Settings
├── Account
│   ├── Profile
│   ├── Password
│   └── SSH keys
└── About
```

### 3.2 应用框架

桌面端由三个稳定区域组成：

1. 全局 header：品牌、全局搜索、创建命令、主题模式和账户入口。
2. 左侧 primary navigation：Repositories、Teams；管理员额外显示 Administration 分组。
3. 主内容区：breadcrumb、页面标题、主操作、筛选/视图控制和实际工作内容。

进入仓库后，主内容区增加 repository context header 和二级导航，至少预留 Code、Commits、Branches、Tags、Settings；不得把仓库导航塞进全局导航。

移动端规则：

- header 保留品牌、搜索入口和 menu command。
- primary navigation 进入可关闭的抽屉；打开后必须管理焦点并支持 `Escape` 关闭。
- 页面主操作保持可见，次要操作收拢到 command menu。
- 数据表优先转换为可扫描行；无法合理转换时允许内容区水平滚动，不压缩到不可读。

### 3.3 页面框架

```text
Breadcrumb (可选)
Page heading
  Title + compact metadata
  Primary command + secondary command menu
Filter / view toolbar (可选)
Inline status region
Primary content
Pagination / continuation (可选)
```

标题只描述当前对象或任务，不承担产品宣传。仓库名、用户名和团队名必须在第一屏清晰可见。

## 4. 双主题策略

界面提供 `System / Light / Dark` 三种模式；实际视觉主题为 Light 和 Dark，System 跟随 `prefers-color-scheme`。

生产实现放在 `#100`：

- 使用非敏感 cookie `.GitCandy.Theme` 保存 `system|light|dark`，便于 Razor 首屏直接输出主题属性。
- System 模式仅通过 CSS media query 跟随操作系统。
- 主题切换不得刷新当前页面、改变 URL 或丢失表单内容。
- JavaScript 不可用时仍能按服务端已保存模式正常渲染。
- 主题 token 只使用语义名称；页面组件不得直接复制主题色值。

### 4.1 Semantic tokens

| Token | Light | Dark | 用途 |
| --- | --- | --- | --- |
| `canvas` | `#f4f6f7` | `#0f1316` | 应用背景 |
| `surface` | `#ffffff` | `#171c20` | 主内容和控件背景 |
| `surface-muted` | `#f8fafb` | `#1d2429` | 表头、选中行、次级区域 |
| `text` | `#1b252c` | `#e7ecef` | 主文本 |
| `text-muted` | `#65727c` | `#9aa7af` | 元数据和辅助文本 |
| `border` | `#d7dee2` | `#313a41` | 分隔线和控件边界 |
| `border-strong` | `#b9c4ca` | `#46525a` | hover、输入边界 |
| `brand` | `#176b4d` | `#66c99b` | 主命令、链接和选中状态 |
| `brand-hover` | `#10543b` | `#83d8b0` | brand hover |
| `accent` | `#14788a` | `#5eb8c8` | 信息状态、辅助数据 |
| `success` | `#217a50` | `#66c99b` | 成功/公开状态 |
| `warning` | `#8a5b12` | `#e3b65d` | 私有/注意状态 |
| `danger` | `#b4233b` | `#ee7187` | 删除、失败状态 |
| `focus` | `#2d7fd3` | `#79b8ff` | 键盘焦点环 |
| `code-surface` | `#edf1f3` | `#111619` | code/diff 背景 |

色值在 `#100` 实现前必须通过 WCAG 2.2 AA 对比度检查。状态不得只靠颜色表达，必须同时提供文本、图标或形状。

### 4.2 尺寸与密度

- 基础 spacing unit：`4px`；主要节奏使用 `8/12/16/24/32px`。
- 应用 header：`56px`；桌面 sidebar：`224px`。
- 主内容最大宽度：列表/管理页面 `1280px`；代码和 diff 页面可使用全部可用宽度。
- 控件默认高度：`36px`；紧凑表格行目标高度：`44px`。
- radius：控件 `4px`，工具面板和菜单不超过 `6px`，不使用大圆角卡片。
- 阴影只用于 menu、dialog、popover 等浮层；页面区块使用边界和背景层级。
- 字号不随 viewport 宽度缩放；正文 `14px`，辅助文本 `12px`，页面标题 `24px`。

字体在 `#096` 评估自托管成本前使用系统字体 fallback；代码使用等宽字体 fallback。不得为字体引入未经评估的外部 CDN 运行时依赖。

## 5. 核心组件契约

| 组件 | 必须状态 | 关键约束 |
| --- | --- | --- |
| Global header | default、search focused、account menu open | 移动端不与品牌/菜单重叠 |
| Sidebar navigation | default、hover、active、collapsed/mobile | active 同时使用形状和颜色 |
| Theme segmented control | system、light、dark | 使用 `aria-pressed`，键盘可操作 |
| Button | primary、secondary、quiet、danger、disabled、busy | 熟悉图标命令优先 icon button；陌生图标有 tooltip |
| Input/select/textarea | default、focus、invalid、disabled | validation 紧邻字段且不导致布局遮挡 |
| Table/list | loading、empty、populated、filtered、error | header 和 action 列尺寸稳定 |
| Status badge | public、private、archived、disabled | 文本不能只用颜色区分 |
| Inline alert | info、success、warning、danger | 不泄漏内部路径和异常细节 |
| Command menu | closed、open、keyboard active | 支持 Escape 和 focus return |
| Dialog | confirm、destructive、busy、failure | destructive command 明确对象名 |

页面不得嵌套装饰性 card。允许被框定的对象仅包括重复实体、dialog、menu 和真正需要边界的工具区。

## 6. 页面与状态矩阵

| 页面组 | 首批原型 | 角色差异 | 必须状态 |
| --- | --- | --- | --- |
| App shell | repository list context | anonymous/user/admin | Light/Dark、desktop/mobile、navigation open |
| Repository | list、detail、workspace、relationships | anonymous/reader/owner/admin | empty/private/not-found/denied/destructive |
| Account | login、register、profile、password、SSH keys | anonymous/current user/admin | validation/lockout/empty keys/delete key |
| Team | list、detail、members | member/team admin/system admin | empty/denied/remove member/delete team |
| Administration | users、teams、settings | administrator only | search empty/read-only/error |
| Shared | error/access denied/not found | all | safe message/request id/recovery command |

`#090` 先完成 App shell；其余高保真页面原型按 `#101`、`#102`、`#103` 的实现顺序补齐，并在各自生产实现前评审。

## 7. 框架原型

入口：[M9 application shell prototype](prototypes/m9-shell/index.html)

原型必须：

- 可直接从本地文件打开，不依赖开发服务器或外部 CDN。
- 提供 System/Light/Dark 模式并保存本地原型偏好。
- 展示桌面 sidebar、移动导航、page heading、toolbar、repository list 和状态样式。
- 使用固定示例数据，不访问 GitCandy API，不复用生产 cookie。
- 只作为布局、层级、密度和主题评审材料。

## 8. 后续实施顺序

1. `#090`：评审并冻结本文件和 App shell 原型。
2. `#096`：根据冻结后的资产需求选择生产资产管线、依赖更新和离线部署策略。
3. `#100`：只实现主题运行机制和应用框架，不迁移具体业务页面。
4. `#101/#102/#103`：按仓库、账户、管理三个独立垂直切片逐页实现。
5. `#104`：补齐响应式、无障碍和完整状态矩阵。
6. `#105`：建立视觉回归并在确认无生产引用后移除 Bootstrap 3 运行时加载。

## 9. 兼容性与回滚

`#090` 只有文档和隔离原型，不改变运行时，删除 `docs/design/` 新增文件即可回滚。

后续生产实现必须保持：

- 公开 URL、controller/action 和 route values。
- 表单字段名、HTTP method、antiforgery 和 validation 行为。
- Identity cookie、Git Basic Auth、权限 policy 和角色语义。
- Git HTTP/SSH 路由、header、streaming 和 challenge 行为。
- 不要求部署者连接 CDN；生产资产必须可随 GitCandy 离线部署。

每个 UI 实现项保留上一版静态资源一段可回滚窗口；只有 `#105` 完成运行时引用扫描、浏览器回归和发布说明后才删除 Bootstrap 3 文件。

## 10. #090 验收清单

- [ ] 页面、角色和状态矩阵经评审确认。
- [ ] Light/Dark semantic tokens 经对比度检查。
- [x] 桌面 `1440x900` 和移动 `390x844` 框架原型无重叠、截断和不可达操作。
- [x] System/Light/Dark 切换、刷新持久化和键盘操作通过。
- [ ] 匿名、普通用户、owner、administrator 的导航差异有原型或明确规范。
- [x] `#096`、`#100` 到 `#105` 的边界和回滚方式已记录，等待评审确认。
- [x] 未修改生产 Razor/CSS/JavaScript，未引入运行时依赖。

### 2026-07-10 验证记录

- `node --check docs/design/prototypes/m9-shell/prototype.js`：通过。
- Playwright CLI + Microsoft Edge，Light/Dark，`1440x900`：通过；无水平溢出。
- Playwright CLI + Microsoft Edge，Light/Dark，`390x844`：通过；无水平溢出。
- 移动导航打开、backdrop、`Escape` 关闭和焦点返回：通过。
- Dark 模式刷新持久化：通过，prototype local storage 值保持 `dark`。
- 干净浏览器会话 console：0 errors，0 warnings。
- Playwright 截图只用于本次评审检查，验证后已删除，未作为仓库产物提交。
