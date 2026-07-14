---
layout: default
title: Issue、Pull Request 与发现
description: GitCandy 协作、review、通知、Release、搜索和公开发现指南。
permalink: /current/users/collaboration/index.html
help_root: ../../../
section: 用户指南
owner: product
audience: users
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/current/users/collaboration.md
---

# Issue、Pull Request 与发现

Issue 和 Pull Request 共享仓库内的工作项编号，并在每次读取时重新校验仓库权限。私有仓库的标题、引用、通知和搜索结果不会向无权用户降级泄漏。

## Issue 工作流

读者可创建 Issue 和评论；作者、assignee、仓库 owner 与管理员按角色管理编辑和状态。仓库 owner 可配置 label、milestone、assignee、relation、subscription 和讨论锁定。

正文使用受限 CommonMark，支持 fenced code、task list、mention、工作项和 commit 链接。Raw HTML 被禁用，最终 HTML 经过 allow-list 清洗。模板位于 `.gitcandy/ISSUE_TEMPLATE/{name}.md`，没有指定名称时使用 `default.md`。

成功 push 到默认分支后，`fixes #N`、`closes #N` 和 `resolves #N` 会幂等关闭对应 Issue。

## Pull Request 与 review

Pull Request 保存 base/head 的不可变 SHA 快照，支持同仓库与跨 fork 比较、draft/ready、Conversation、Commits、Files changed、行内 thread、approve/request changes、required review、merge 与 squash。

Review 锚点无法唯一映射到新 diff 时会标记 outdated，而不是静默移动到错误行。提交新 head 后，是否使旧 approval 失效由分支规则决定。

## Checks、Webhook 与 Release

CI 可使用 PAT 调用 commit check API。受保护分支可要求指定 context 成功后才能 push 或 merge。仓库 owner 可创建 Webhook 并查看 delivery、分类错误和 replay。Release 使用已有 Git tag，资产受单文件、总大小和数量限制。

## 搜索与发现

仓库搜索会在查询时过滤权限。`/explore` 和 dashboard 推荐只读取公开候选快照；没有 SPDX/许可证证据时页面只称“公开仓库”，不会把 public 自动描述成 open source。
