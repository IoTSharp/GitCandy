---
layout: default
title: 架构与贡献
description: GitCandy 单进程架构、项目边界、开发环境、测试和贡献约定。
permalink: /current/developers/architecture/index.html
help_root: ../../../
section: 开发者文档
owner: maintainers
audience: contributors
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/current/developers/architecture.md
---

# 架构与贡献

GitCandy 目标为 `net10.0`，唯一活动 solution 是 `GitCandy.slnx`。默认在 `master` 上按 ROADMAP 垂直切片演进，不把框架、schema、UI、协议和部署重写混入一次不可审查的变更。

## 项目边界

| Project | 职责 |
| --- | --- |
| `GitCandy` | ASP.NET Core MVC host、Web、认证、endpoint、静态帮助 |
| `GitCandy.Core` | 领域模型、权限、配置抽象和服务 contract |
| `GitCandy.Data` | EF Core DbContext、Identity、业务服务与 model configuration |
| `GitCandy.Git` | 仓库、LFS、统一 `IGitTransportBackend` 与安全路径 |
| `GitCandy.Ssh` | 内置 SSH hosted service 与 Git session |
| Provider projects | SQLite、SQL Server、SonnetDB migration/provider 边界 |

Controller 保持薄；后台线程使用独立 scope/DbContext；Git HTTP 与 SSH 复用 resolver、权限和 transport backend。业务层不得散落 `Process.Start`，Git helper 必须使用 `ArgumentList` 和流式 I/O。

## 本地构建

需要 .NET 10 SDK 与 Node.js 20 或更高版本。JekyllNet 是固定 local tool：

```bash
dotnet tool restore
dotnet restore GitCandy.slnx
dotnet build GitCandy.slnx --configuration Release
dotnet test GitCandy.slnx --configuration Release --no-build --no-restore
```

Web 项目构建会先生成 client bundle 和帮助站点。帮助 HTML 只进入 `obj/bin/publish`，不要提交生成目录。

## 变更与测试

先读根目录 `AGENTS.md` 与 `ROADMAP.md`，执行 `git status --short`，保留用户已有改动。新 Core/Data/Auth/Git 服务目标 80% 行覆盖；涉及 Git HTTP/SSH 至少验证 clone、fetch、push；涉及帮助中心要验证 inventory、链接、锚点、CSP、PathBase、404 与 publish manifest。

Commit 使用 Conventional Commits。用户、部署、数据库、认证、公开 URL 或协议行为变化要更新 `CHANGES.md` 和对应帮助页，并说明迁移、回滚与未验证风险。
