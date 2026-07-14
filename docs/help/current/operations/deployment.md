---
layout: default
title: 部署、反向代理与 TLS
description: Docker Compose、Linux systemd、Windows Service 和 TLS 部署指南。
permalink: /current/operations/deployment/index.html
help_root: ../../../
section: 运维指南
owner: operations
audience: operators
public: true
archived: false
version: current
updated: 2026-07-14
canonical: docs/help/current/operations/deployment.md
---

# 部署、反向代理与 TLS

正式支持 Docker Compose、Linux x64 systemd 服务和 Windows x64 Windows Service。不支持 IIS in-process/out-of-process 或旧 MVC5 发布流程。

## Docker Compose

```bash
cp .env.example .env
docker compose pull
docker compose up -d
docker compose ps
curl --fail http://127.0.0.1:8080/health/ready
```

生产环境把 `GITCANDY_IMAGE` 固定到明确版本。持久状态位于 `gitcandy-data` volume，默认公开 HTTP `8080` 和 SSH `2222`。自定义 `appsettings.Production.json` 应以只读 volume 注入，secret 使用环境或 secret store。

## Linux systemd

Release 的 Linux 包包含自包含应用、`install.sh`、systemd unit 和生产配置样例。以 root 执行安装器后检查：

```bash
curl --fail http://127.0.0.1:8080/health/ready
sudo systemctl status gitcandy
sudo journalctl -u gitcandy -n 200 --no-pager
```

应用安装在 `/opt/gitcandy`，状态默认在 `/var/lib/gitcandy`。服务用户只需要状态目录的必要权限。

## Windows Service

以管理员 PowerShell 执行包内 `Install-GitCandyService.ps1`。默认应用目录为 `%ProgramFiles%\GitCandy`，数据目录为 `%ProgramData%\GitCandy`。脚本拒绝把安装或数据目录设为文件系统根。

## TLS 与 PathBase

Identity cookie 始终要求 Secure，公网必须在 GitCandy 前放置 TLS reverse proxy。代理要转发 scheme、host 和客户端地址，并为 Git pack 保持长请求、流式 body/response 和足够 timeout，禁止响应缓冲。

`GitCandy:Proxy` 只信任明确的 `KnownProxies`/`KnownNetworks` 和 `ForwardLimit`。帮助中心使用相对资源 URL并支持 `Request.PathBase`；代理若剥离 `/gitcandy` 前缀，应同时传递正确 PathBase。

## 发布完整性

Docker、framework-dependent publish、Linux 和 Windows 包都必须包含 `wwwroot/help/index.html`、`help-manifest.json`、搜索索引和本地资产。缺少 JekyllNet、坏链接或帮助生成失败必须阻止发布。
