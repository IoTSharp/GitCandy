---
layout: default
title: 企业身份与数据库 Provider
description: 企业连接、SCIM、OIDC 和 SQLite、SQL Server、SonnetDB Provider 边界。
permalink: /current/admin/providers/index.html
help_root: ../../../
section: 管理指南
owner: data
audience: administrators, operators
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/current/admin/providers.md
---

# 企业身份与数据库 Provider

Provider 配置必须显式启用。数据库、企业身份和远程 Git Provider 是三套不同边界，不要复用 token、secret、回调或健康状态。

## 企业身份

当前版本支持可选 OpenID Connect、Microsoft Entra ID/SCIM，以及企业微信、飞书和钉钉的企业连接抽象。目录同步可创建、更新、停用账号并对账团队成员，权限撤销必须在下一次请求生效。

企业 secret 通过 reference 解析，不在设置页回显。Webhook/event callback 要求签名、时间窗口和去重。连接测试失败时只记录分类错误，不记录 token、签名原文或用户敏感字段。

## 数据库 Provider

| Provider | 当前定位 | 关键要求 |
| --- | --- | --- |
| SQLite | 默认运行路径 | 单实例优先；数据库文件与仓库一起备份 |
| SQL Server | 正式 migration 路径 | 发布前生成并审阅 idempotent migration SQL |
| SonnetDB | 专用配置显式启用 | 未配置时核心 Git 和帮助中心必须正常 |

所有 Provider 使用同一个 EF Core 模型和 ASP.NET Core Identity schema。应用启动前检查并应用 pending migrations；不会自动改写旧 MVC5 数据库。

## 远程 Git Provider 状态

Remote account/provider 抽象和 Remote/Mirror schema 已建立，但 GitHub/GitLab/Gitee 绑定 UI、受控 sync backend 和完整 mirror job 尚未作为当前功能发布。不要根据数据库表存在就宣称 remote import 或 mirror 可用。

## 变更流程

切换 Provider 前先停止写流量、完成一致备份、生成目标 schema、迁移数据并在隔离环境验证 Identity、权限和 clone/fetch/push。回滚必须同时恢复数据库与仓库快照，不能只回退二进制。
