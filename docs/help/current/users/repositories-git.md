---
layout: default
title: 仓库、Git 与 LFS
description: 创建仓库、克隆、推送、SSH、分支与 Git LFS 操作指南。
permalink: /current/users/repositories-git/index.html
help_root: ../../../
section: 用户指南
owner: git
audience: users
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/current/users/repositories-git.md
---

# 仓库、Git 与 LFS

仓库属于用户或团队 namespace。名称按大小写不敏感规则保持唯一，物理存储名与公开名称分离，因此改名不会移动仓库目录。

## 创建和克隆

创建仓库时选择 owner、名称、描述和可见性。Canonical URL 为：

```text
Web:  https://git.example.com/{namespace}/{repository}
HTTP: https://git.example.com/{namespace}/{repository}.git
SSH:  ssh://git@git.example.com:2222/{namespace}/{repository}.git
```

HTTP 私有仓库可使用用户名和密码，或带 `git:read`/`git:write` scope 的 PAT。SSH 只接受已登记的公钥，不提供交互 shell、SFTP、端口转发或密码登录。

## 常用命令

```bash
git clone https://git.example.com/acme/demo.git
git -C demo fetch --prune
git -C demo push origin main
```

Push 前会重新检查仓库写权限、分支规则和受保护 ref。大 pack 请求和响应保持流式传输；反向代理不得缓存或完整缓冲 pack。

## 分支、标签和归档

仓库页提供 Branches、Tags、Contributors、commit、tree、blob、raw、diff、blame、compare 和 archive。删除分支或标签需要写权限、POST 与 antiforgery 校验；默认分支和受保护分支会再次在服务端拒绝。

## Git LFS

先安装 Git LFS，再为需要的大文件类型启用 tracking：

```bash
git lfs install
git lfs track "*.psd"
git add .gitattributes design.psd
git commit -m "Add design source"
git push origin main
```

LFS 使用同一仓库 URL 和权限。当前支持 basic batch、上传、下载和 verify；LFS locking 不属于当前发布能力。备份必须同时包含数据库、bare repositories 和 LFS 对象目录。
