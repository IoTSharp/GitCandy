---
layout: default
title: 账号与私人工作台
description: 注册、登录、安全设置、Todo、通知与公开个人页指南。
permalink: /current/users/account-workspace/index.html
help_root: ../../../
section: 用户指南
owner: product
audience: users
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/current/users/account-workspace.md
---

# 账号与私人工作台

登录后的 `/me` 是仅当前用户可见的工作入口；`/{username}` 才是公开个人页。两者有意分离，避免把私人 Todo、通知或团队上下文暴露给访客。

## 创建和保护账号

当管理员允许注册时，从登录页创建账号并使用唯一邮箱。账号安全页可修改密码、启用 TOTP 两步验证、生成恢复码、管理外部登录和 Personal Access Token。

恢复码只在生成时显示，应保存在密码管理器中。重置 authenticator 会使旧密钥失效。PAT 也只在创建成功页显示一次；不要把它写入 URL、仓库文件、CI 日志或 shell history。

## 工作台、Todo 与通知

`/me` 汇总需要关注的仓库、近期活动、Todo、通知、团队和公开推荐。模块失败时其他模块仍可用，页面会显示相应降级状态。

- Todo 表示仍需你处理的工作，可完成、恢复或 snooze。
- 通知表示已经发生并投递的事件，可标记已读；读取通知不会自动完成 Todo。
- Feed 是关注、参与和团队上下文的时间线，不参与未读计数，也不替代审计记录。

## 公开身份

公开页只允许 `repositories`、`stars`、`packages` 和 `teams` tab。私有仓库不会因为你已登录而进入公共推荐快照；匿名访问和登录访问都必须通过相同的公开候选边界。

## SSH key 与 PAT

用户 SSH key 用于内置 SSH Git transport，PAT 用于 API 或 Git HTTP。PAT scope 为 `api:read`、`api:write`、`git:read`、`git:write`；写 scope 自动包含对应读 scope。撤销凭据后，后续认证立即失败。
