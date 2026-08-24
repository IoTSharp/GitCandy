---
layout: default
title: 备份、恢复、观测与排障
description: GitCandy 一致性备份、恢复、回滚、健康检查和故障分类指南。
permalink: /current/operations/recovery-troubleshooting/index.html
help_root: ../../../
section: 运维指南
owner: operations
audience: operators
public: true
archived: false
version: current
updated: 2026-08-24
canonical: docs/help/current/operations/recovery-troubleshooting.md
---

# 备份、恢复、观测与排障

GitCandy 状态跨数据库、bare repositories、LFS、Data Protection keys、SSH host key 和配置。可靠恢复要求这些内容来自同一个受控时间点。

## 一致性备份

1. 记录应用版本、database provider、最后 migration 和配置摘要。
2. 停止写流量或停止服务，等待 Git transport 与后台 job graceful shutdown；未完成的 mirror lease 会释放并在恢复后重新认领。
3. 备份数据库、repositories、LFS/cache 中不可重建对象、Data Protection keys、SSH host key 和生产配置。
4. 对备份生成校验和并保存在独立故障域。
5. 定期在隔离目录运行 `tools/operations/Invoke-RecoveryRehearsal.ps1` 或等价流程。

Cache 中可重建内容可以排除，但必须明确区分 LFS/Release 等持久对象。不要把数据库和仓库从不同时间点拼成一次恢复。

## 恢复与回滚

先部署与备份版本相同的应用，恢复所有状态并执行 migration-only/readiness 检查，再开放 Web、HTTP Git 和 SSH。至少验证登录、公私有权限、clone、fetch、push、LFS 和后台队列。对 mirror 还要核对数据库中的 pending generation 与仓库当前 refs：只恢复数据库可能重放已不存在的 ref，只恢复仓库会丢失尚未发送的 Push 事件。

Schema 变更后只回退二进制通常不安全。回滚应恢复变更前的完整快照；不能通过删除 migration history 伪造旧 schema。

## 健康与观测

- `/health/live`：进程存活。
- `/health/ready`：数据库和关键启动条件已就绪。
- OpenTelemetry：ASP.NET Core、runtime、trace、metric 和可选 OTLP export。
- systemd journal、Windows Event/服务状态或容器日志：只记录分类信息，不能记录凭据。

## 常见故障

| 症状 | 首要检查 |
| --- | --- |
| 登录后循环跳转或 cookie 不保存 | 外部 scheme 是否为 HTTPS、Forwarded Headers 是否在 auth 前 |
| clone/fetch 中断 | 代理 timeout、buffering、body size、Git helper 与磁盘空间 |
| SSH 无法连接 | 2222 映射、host key 权限、`EnableSsh`、公钥登记 |
| readiness 失败 | 数据库连接、pending migration、状态目录权限 |
| `/help` 404 | 发布包是否包含 `wwwroot/help`，生成步骤是否成功 |
| Webhook 重试 | delivery 分类错误、DNS/SSRF policy、目标 TLS 与响应码 |
| Mirror job 一直 Pending | scheduler 是否运行、`Jobs:MaxConcurrentJobs`、`AvailableAtUtc` 和远端 rate-limit 时间 |
| Mirror job 一直 Leased | Git operation timeout、进程是否仍存活；超过 lease 后下一次唤醒会恢复，不要直接改数据库 |
| Mirror permanent failure | owner 设置页的分类错误；修复 credential、权限、remote delete 或 divergence 后再 Retry |
| Provider callback 401/404/503 | 原生签名头、连接 provider、secret reference 是否存在并能由运行账户解析 |
