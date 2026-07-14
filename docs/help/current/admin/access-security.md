---
layout: default
title: 权限、凭据与安全
description: 管理员和仓库 owner 的角色、认证、凭据与安全边界指南。
permalink: /current/admin/access-security/index.html
help_root: ../../../
section: 管理指南
owner: security
audience: administrators
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/current/admin/access-security.md
---

# 权限、凭据与安全

GitCandy 的浏览器、Git HTTP、SSH、API 和后台任务使用不同认证入口，但复用同一套仓库解析与权限判断。隐藏按钮不是授权边界，每个写操作都会在服务端重新校验。

## 角色与可见性

- 全局 administrator 可以管理用户、团队、设置和企业连接。
- 团队治理使用明确的 owner/administrator/maintainer/member 级别。
- 仓库 owner 管理成员、deploy key、分支规则、Webhook、审计和删除。
- 公有仓库允许匿名读；私有仓库要求有效的 user、team 或 administrator 权限。

稳定 namespace、repository claim 和 alias 解析发生在授权之前。过期 alias、未知仓库和无权访问的私有仓库应返回不泄漏资源存在性的结果。

## 认证入口

Web 登录使用 ASP.NET Core Identity cookie，Cookie 名为 `.GitCandy.Identity`，始终 `HttpOnly` 且生产路径要求 Secure。Git Basic、PAT、SCIM bearer 和 SSH public key 是独立 scheme，不能依赖浏览器 cookie。

旧 MVC5 密码 hash、`_gc_auth` cookie 和 `AuthorizationLog` 不兼容。迁移后应重新创建账号，或只通过单独工具导入非密码资料。

## 机器凭据

PAT 是用户身份，支持 `api:read`、`api:write`、`git:read`、`git:write`。Deploy key 只作用于一个仓库。Webhook secret、provider secret 和 Data Protection key 应使用独立存储和生命周期，不得进入日志或 URL。

## 分支与出站请求

Required review、required check、受保护 ref 和 force/delete policy 在 push/merge 入口执行。Webhook 与 check target URL 经过 SSRF policy，默认拒绝无效 scheme、本机、保留和不允许的目标。

## 管理检查表

1. 公网入口启用 TLS，并只信任明确的 reverse proxy 地址。
2. 持久化 Data Protection keys；多实例必须共享同一 key ring。
3. 定期撤销闲置 PAT、SSH key、deploy key 和企业连接 secret。
4. 检查 repository audit、Webhook delivery 和 OpenTelemetry 告警。
5. 备份前记录应用版本、数据库 provider 和 migration 状态。
