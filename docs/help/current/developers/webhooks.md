---
layout: default
title: Webhook 集成
description: 仓库 Webhook 事件、签名、delivery、重试与安全约定。
permalink: /current/developers/webhooks/index.html
help_root: ../../../
section: 开发者文档
owner: integrations
audience: developers
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/current/developers/webhooks.md
---

# Webhook 集成

仓库 owner 在 Settings / Webhooks 创建订阅，选择事件并保存一次性显示的 secret。Delivery 持久化后由后台 job 发送，失败会按策略重试，owner 可查看状态并 replay。

## 事件

当前事件类型为：

- `push`
- `pull_request.merged`
- `check.updated`
- `release.published`

Payload 使用版本化 event envelope，并限制 ref、描述和集合大小。具体业务字段随 event type 变化，消费者应忽略未知字段，不应依赖 JSON 属性顺序。

## 请求头与签名

每次 delivery 包含版本头 `X-GitCandy-Webhook-Version: 1`、稳定 event/delivery 标识和基于订阅 secret 的签名。接收方应对原始 request body 验签，使用常量时间比较，并以 event ID 去重。

Secret 只在创建时显示，不会在管理页、delivery 详情或日志回显。轮换时创建新订阅、并行验证后再停用旧订阅。

## Delivery 状态

状态为 `Pending`、`InProgress`、`Succeeded` 或 `Failed`。诊断摘要包含 attempt count、下次尝试时间、HTTP status 或分类 error code，不包含响应正文和 secret。

Replay 创建新的 delivery 并记录原 delivery ID；它不会重用已完成记录，也不能绕过订阅是否有效或当前仓库权限。

## 目标安全

只允许受支持的 HTTP(S) 目标。GitCandy 在连接前执行 DNS/地址策略以阻止 loopback、link-local、保留地址和不允许的内部目标；重定向、DNS rebinding、timeout 和响应大小也必须留在边界内。

接收方应快速返回 2xx，把耗时工作写入自己的队列。不要让 GitCandy 的重试成为接收方业务事务的唯一幂等机制。
