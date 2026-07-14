---
layout: default
title: HTTP API 参考
description: 当前 GitCandy v1 commit check API 的认证、请求、响应和错误约定。
permalink: /current/developers/api/index.html
help_root: ../../../
section: 开发者文档
owner: web
audience: developers
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/current/developers/api.md
---

# HTTP API 参考

当前公开业务 API 以 commit status/check 为主。Web UI 路由、Git Smart HTTP、LFS、SCIM 和企业事件 callback 各有独立协议契约，不应被当作通用 JSON CRUD API。

## 认证与 scope

在账号安全页创建 PAT，并以 Bearer token 发送。读取需要 `api:read`，写入需要 `api:write`；写 scope 自动包含读 scope。PAT 只显示一次。

```http
Authorization: Bearer gcpat_...
Accept: application/json
Content-Type: application/json
```

## Endpoints

基础路径：`/api/v1/repositories/{namespace}/{repository}/commits/{sha}`。

| Method | Path | Scope | 说明 |
| --- | --- | --- | --- |
| `GET` | `/checks` | `api:read` | 返回当前 commit 的 status 与 check |
| `POST` | `/statuses` | `api:write` | 幂等更新一个 status context |
| `POST` | `/checks` | `api:write` | 幂等更新一个 check run |

Status state：`pending`、`success`、`failure`、`error`。Check state 还接受 `queued`、`in_progress`、`cancelled`、`neutral`、`skipped` 等归一化值。

```json
{
  "context": "ci/build",
  "state": "success",
  "description": "Build 184 passed",
  "targetUrl": "https://ci.example.test/builds/184",
  "externalId": "184"
}
```

## 响应与错误

成功写入返回归一化 check JSON；读取返回数组。错误使用明确 HTTP 状态，验证错误采用 Problem Details：

- `401`：缺少或无效 PAT。
- `403`：scope 或仓库权限不足；私有资源不会通过响应正文泄漏。
- `404`：namespace、仓库、alias 或 SHA 不可解析。
- `422`：state、context、描述或目标 URL 无效。
- `429`：写 API 固定窗口限流，当前每个 credential/IP 每分钟 120 次。

当前 API 没有通用列表分页 endpoint。未来列表接口必须使用稳定排序、显式 page/pageSize 上限和可追踪错误格式，不能把 Web HTML 分页约定冒充 API contract。
