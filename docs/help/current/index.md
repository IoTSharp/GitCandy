---
layout: default
title: 当前版本概览
description: GitCandy 当前帮助版本、支持范围和非目标。
permalink: /current/index.html
help_root: ../
section: 开始使用
owner: docs
audience: all
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/current/index.md
---

# 当前版本概览

GitCandy 是单进程、自托管的 Git 服务。一个 ASP.NET Core host 同时承载 Web UI、Git Smart HTTP、内置 SSH、Quartz 作业和后台入口，默认使用 SQLite，也保留 SQL Server 与显式 SonnetDB Provider 路径。

## 当前能力

- ASP.NET Core Identity 登录、注册、2FA、恢复码、PAT、SSH key 与可选 OIDC。
- 公有/私有仓库、稳定 namespace、团队权限、分支规则、审计、Release 和代码浏览。
- Git Smart HTTP、内置 SSH、clone/fetch/push、协议 v2 和基本 Git LFS transfer。
- Issue、Pull Request、行内 review、required review、commit check、Webhook 与通知。
- 私人 `/me` 工作台、Todo、通知、Feed、公开个人页和公开仓库发现。
- Docker Compose、Linux systemd 和 Windows Service 三类正式部署。

## 尚未作为当前能力发布

- GitHub/GitLab/Gitee 远程账号绑定和可运维 mirror job 仍属于 Milestone 15 的后续切片。
- OCI Container Registry 与真实 Packages 数据属于 Milestone 15.6。
- 文档知识库、Agent Memory 与 MCP host 属于 Milestone 16。
- 外部 OpenSSH 只是可选适配；默认 SSH 路线仍是随主进程启动的内置服务。

## URL 约定

仓库页面使用 `/{namespace}/{repository}`，Git HTTP 与 LFS 使用 `/{namespace}/{repository}.git`，SSH 使用 `ssh://git@host:port/{namespace}/{repository}.git`。历史 `/git/{project}` 和无 namespace 地址不再作为当前兼容路由。
