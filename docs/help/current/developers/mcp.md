---
layout: default
title: MCP 能力矩阵
description: GitCandy 当前与规划 MCP、文档搜索和 Agent Memory 能力边界。
permalink: /current/developers/mcp/index.html
help_root: ../../../
section: 开发者文档
owner: code-intelligence
audience: developers
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/current/developers/mcp.md
---

# MCP 能力矩阵

当前发布**没有启用 MCP host、文档向量检索或 Agent Memory API**。帮助中心是构建期静态站点，不会因未配置 SonnetDB 而降级；不要把 `/help` 搜索解释成知识库检索。

## 当前矩阵

| 能力 | 当前状态 | 计划边界 |
| --- | --- | --- |
| `/help` 静态搜索 | 可用 | 只搜公开、同版本本地 metadata |
| Code Memory ingest | 不可用 | 增量、可取消、不进入 Git 热路径 |
| `docs_search/get/topics` | 不可用 | 结果引用稳定 `/help` 资源 |
| code search/symbol/impact MCP | 不可用 | 查询时重新校验 repository 权限 |
| 只读业务 MCP tools | 不可用 | Streamable HTTP、PAT/Bearer、分页、审计 |
| 写 MCP tools | 不可用 | 最小 scope、幂等、并发确认、可禁用 |

## 未来契约要求

MCP 不能通过 endpoint 反射自动暴露业务操作。每个 tool 必须登记输入 schema、scope、风险、分页、审计、版本和排除原因；结果必须追溯到稳定资源或同版本帮助页。

私有仓库内容在索引、检索、缓存和最终返回四个阶段都要重新校验权限。Prompt injection、资源枚举、token 撤销、低置信度、超大结果和并发限制必须进入验收矩阵。

未配置 SonnetDB 时，Web、Git HTTP/SSH、帮助中心和普通搜索继续工作；知识能力应明确报告 unavailable，而不是让主进程启动失败。
