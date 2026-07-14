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

## 远程 Git Provider

登录用户可从工作台设置进入 `/me/remotes`，连接 GitHub、GitLab 或 Gitee，测试凭据并分页查看该凭据可访问的仓库。当前连接方式接受 PAT 或已有 OAuth access token；交互式 OAuth consent 和完整 GitHub App 安装流程仍在后续切片。

配置位于 `GitCandy:Remotes`：

| 键 | 默认值 | 说明 |
| --- | --- | --- |
| `RequestTimeout` | `00:00:20` | 单次 Provider API 请求 timeout，允许 1 秒到 2 分钟 |
| `OperationTimeout` | `00:30:00` | 远程 Git fetch/push timeout，允许 1 秒到 24 小时 |
| `StreamBufferSize` | `81920` | Git 子进程 stdout/stderr 流式排空缓冲区，允许 4 KiB 到 1 MiB |
| `MaxDiagnosticCharacters` | `8192` | 仅用于错误分类的 stderr 尾部上限，允许 1024 到 65536 字符 |
| `{Provider}:Enabled` | `true` | 是否允许用户选择该 Provider |
| `{Provider}:ServerUrl` | 官方站点 | stable identity 所属站点；自托管实例由管理员固定 |
| `{Provider}:ApiBaseUrl` | 官方 API | 出站 API origin；只允许 HTTPS，loopback HTTP 仅供测试 |

用户不能提交自定义 endpoint。authenticated API 请求不跟随 redirect，token 只进入授权 header。连接 token 使用 Data Protection key ring 加密到其 `remote-credentials` 子目录，EF Core 只保存 opaque reference；设置页和日志均不回显 token。

单向 Pull/Push mirror 执行引擎已经内置。Pull 使用隔离 staging refs 执行初始/周期 fetch，并在启用期间把本地仓库设为只读；Push 在本地 receive-pack 成功后只合并并持久化 ref 事件，再由 Quartz 异步推送，不等待远端网络。默认 divergence policy 是停止；`KeepDivergent` 跳过分叉 ref，`OverwriteTarget` 必须显式配置并写审计。删除传播默认关闭。

当前 Web 设置仍只提供账号连接和仓库发现；mirror 配置、暂停、重试、取消、立即同步和分类诊断运维视图属于后续 M15 job/operations 切片。当前 pending ref 表只保护 post-receive 事件不因进程重启丢失；跨实例 lease、指数退避、最大重试和 crash recovery 尚未发布。仓库 mirror 第一阶段只处理 branches/tags 和 Git objects，不包含 LFS、Issues、PR/MR、Wiki、Releases、CI 或 Packages。

## 变更流程

切换 Provider 前先停止写流量、完成一致备份、生成目标 schema、迁移数据并在隔离环境验证 Identity、权限和 clone/fetch/push。回滚必须同时恢复数据库与仓库快照，不能只回退二进制。
