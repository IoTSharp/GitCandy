# M15.5 帮助中心与全量文档发布验收记录

更新日期：2026-07-14

## 对应 ROADMAP

- Milestone 15.5 / `#169A-#169H`
- 本切片只建立文档 inventory、当前帮助内容、静态生成、Web 路由、发布打包和质量门，不实现 M15 remote mirror、M15.6 Registry 或 M16 MCP 后端。

## 变更点

- `docs/help/_data/document-inventory.json` 逐项登记仓库 86 个 Markdown 源的 owner、audience、public、canonical、archived、version 和 permalink；迁移记录、完成历史和路线图快照不进入当前导航。
- `docs/help` 提供 13 篇 canonical 当前文档，覆盖账号/dashboard、仓库/Git/LFS、Issue/PR/review/发现、权限/安全/provider、三类部署、TLS、恢复/观测/排障、API/webhook/pagination/error/rate limit、MCP matrix、架构与贡献。
- 固定 local tool `jekyllnet 0.2.5`；MSBuild 在 build/publish 阶段生成站点并复制到应用产物 `wwwroot/help`。生产启动和请求不解析 Markdown。
- 主题、logo、CSS、JavaScript 和搜索索引全部自托管，无 CDN；asset version 防止升级时复用旧主题缓存。
- `/help` 规范化为 `/help/`，仅允许 GET/HEAD；目录 permalink、PathBase、匿名访问、404、extension allowlist、路径边界、ETag、cache、CSP、content type、`nosniff` 和 referrer policy 均有测试。
- 应用 shell 与公开 landing page 都提供 Help 入口；帮助页可返回同一 PathBase 下的 GitCandy。
- Docker、framework-dependent publish、Linux 和 Windows package 共用项目生成目标；release/container smoke 同时请求帮助首页与 manifest。

## 文档版本与历史

- `/help/current/` 描述随当前产物发布的功能。
- 稳定冻结版本使用 `/help/v{major}.{minor}/`，不得让旧版本覆盖 `current`。
- 历史 Markdown 保留在 Git 仓库和 inventory 中并标记 archived；公共当前导航只链接仍适用的指南。
- `help-manifest.json` 记录 generator、documentation version、asset version、entry point、搜索索引和发布页面。

## 测试说明

- 已运行 JekyllNet standalone build：13 pages、5 static files，生成成功。
- 已运行隔离 Release build：0 warning、0 error，帮助站点复制到 Web build output。
- 已运行 `HelpDocumentationTests` 与 `HelpEndpointTests`：5/5 通过。
- 已运行 framework-dependent `dotnet publish`：发布目录包含 18 个帮助文件和 manifest，API 文档 permalink 存在。
- 已用真实 Chrome 验证 1440x900 与 390x844：重定向、搜索、移动目录、内部跳转、无横向溢出，console 0 error、0 warning；截图位于本地 `output/playwright/help-*.png`，不提交生成物。
- 已运行完整 Release solution build：0 warning、0 error；`GitCandy.Data.Tests` 101/101、`GitCandy.Tests` 144/144，合计 245/245 通过，无跳过、无失败。
- 本切片未改动数据库 schema、Git transport 或 SSH，因此未重复执行 Git HTTP/SSH clone/fetch/push。

## 是否破坏兼容

- 新增公开 URL `/help` 与 `/help/{**path}`；没有改变现有公开路由。
- 发布构建新增固定 JekyllNet local tool 恢复要求；CI、Docker 和 README 已同步执行 `dotnet tool restore`。
- 发布包新增约 100 KiB 的静态帮助内容，不引入生产运行时依赖。

## 回滚

1. 回退 `MapGitCandyHelp`、导航入口和 Help endpoint/options 文件。
2. 回退 Web 项目的 JekyllNet build/publish targets、local tool manifest 与 CI/Docker tool restore。
3. 删除 `docs/help` canonical 内容和本记录，并恢复 ROADMAP M15.5 活动项。
4. 重新 build/publish；旧版本不会触碰数据库、repositories、LFS、Identity、SSH host key 或 Data Protection keys。

## 剩余风险

- 跨 Windows/Linux 的生成和 package 路径由 CI 持续验证；本地只执行了 Windows SDK 验收。
- 当前搜索是公开 metadata 的客户端子串匹配，不是 M16 知识库、全文检索或语义检索。
- 未来每次用户可见功能、配置、公开 URL、API 或运维变化都必须同步 canonical 帮助页、inventory/manifest 和搜索 metadata，否则文档会随应用一起发布错误信息。
