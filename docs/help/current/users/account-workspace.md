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
updated: 2026-08-24
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

## 远程账号

工作台设置中的 **Remote accounts** 打开 `/me/remotes`。你可以连接 GitHub、GitLab 或 Gitee，测试 credential，并查看当前账号有权访问的远程仓库。Token 只在提交时使用，之后不会显示；到期时间会显示在连接状态中，需要替换时使用连接上的轮换操作，再撤销远端旧 token。

使用最小 scope：GitHub 私有仓库通常需要 `repo`，GitLab 发现需要 `read_api` 且 Push mirror 还需要仓库写 scope，Gitee 需要 `user_info, projects` 以及与 mirror 方向匹配的写权限。GitHub App 连接绑定管理员在上游取得的短期 installation token，并且必须填写到期时间；GitLab/Gitee OAuth 连接绑定已有 access token。GitCandy 当前不发起交互式 OAuth consent，也不使用 GitHub App private key 自动换票。

远程仓库出现在发现列表中不表示已经同步。仓库 owner 可打开 `/{namespace}/{repository}/settings/mirrors`，使用发现结果中的 stable repository ID、owner、名称和 Web URL 注册一个单向 Pull 或 Push mirror。Pull mirror 启用时本地仓库只读；Push mirror 在本地 push 成功后异步发送，不会因远端失败回滚本地 push。设置页可暂停/恢复、立即同步、取消、重试和删除，并显示 durable job 与分类错误状态。
