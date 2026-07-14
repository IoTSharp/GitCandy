---
layout: default
title: 帮助中心
description: GitCandy 当前版本的用户、管理员、运维和开发者入口。
permalink: /index.html
help_root: ./
section: 文档首页
owner: docs
audience: all
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/index.md
---

# 先找到正确的操作手册

这里发布的是随 GitCandy 应用交付的当前文档。内容在构建阶段由 Markdown 生成；运行中的 GitCandy 只提供已生成的静态文件，因此帮助中心不依赖外部站点、CDN、数据库或 SonnetDB。

## 按任务进入

| 你要完成的工作 | 从这里开始 |
| --- | --- |
| 登录、管理 Todo 或理解私人工作台 | [账号与工作台](current/users/account-workspace/) |
| 创建仓库、配置 remote、使用 Git 或 LFS | [仓库、Git 与 LFS](current/users/repositories-git/) |
| 使用 Issue、Pull Request、review 与发现页 | [协作与发现](current/users/collaboration/) |
| 配置角色、PAT、SSH key 或企业身份 | [权限与安全](current/admin/access-security/) |
| 部署 Docker、Linux 或 Windows 服务 | [部署与 TLS](current/operations/deployment/) |
| 备份、恢复、回滚或诊断服务 | [恢复与排障](current/operations/recovery-troubleshooting/) |
| 集成 commit checks 或 Webhook | [HTTP API](current/developers/api/) 与 [Webhook](current/developers/webhooks/) |

## 版本规则

`/help/current/` 始终描述随当前应用产物发布的功能。稳定发布后，冻结的文档可以复制到 `/help/v{major}.{minor}/`；旧版页面不得覆盖 `current`。迁移记录、路线图快照和历史验收材料保留在文档 inventory 中并标记 `archived`，但不会混入当前操作导航。

> 页面内容与应用版本不一致时，以同一发布产物内的 `help-manifest.json` 为准，不要把其他分支或旧 Release 的帮助目录覆盖到正在运行的应用。

## 发布边界

- 当前帮助页面允许匿名读取，但不会暴露私有仓库、用户、token 或本机路径。
- 搜索只使用随应用发布的 `search-index.json`，不会把查询发送到服务器或第三方。
- API、MCP 或 Provider 页面明确区分“当前可用”和“规划中”，没有后端的能力不会写成可用功能。
- 仓库中的 Markdown 是唯一事实来源；`wwwroot/help`、`bin` 和发布目录中的 HTML 都是可重建产物。
