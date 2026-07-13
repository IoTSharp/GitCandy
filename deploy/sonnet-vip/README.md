# gitcandy.com on sonnet.vip

本 profile 将 GitCandy 部署到 `192.220.46.211`，复用服务器现有 Caddy 和 SonnetDB，不启动第二套反向代理或数据库。

感谢 [sonnet.vip](https://sonnet.vip/) 为 GitCandy 本次部署提供服务器资源。

## 拓扑

```text
gitcandy.com:443 -> existing Caddy -> GitCandy:8080
gitcandy.com:2222                 -> GitCandy:2222
GitCandy                         -> existing SonnetDB:5080/gitcandy
```

GitCandy 与 Caddy 通过外部网络 `sonnet-gitcandy-edge` 通信；GitCandy 同时加入现有内部网络 `sonnet-vip-sub2api_backend`。SonnetDB 仍不映射宿主机端口。

## 前置条件

1. 保持 `gitcandy.com` 指向 `sonnet.vip` 的 CNAME；当前已解析到 `192.220.46.211`。部署前再次确认解析结果，且不要再添加冲突的 A/AAAA 记录。
2. 在云防火墙和主机防火墙开放 TCP `2222`；80/443 已由现有 Caddy 使用。
3. 使用服务器侧 secret 配置 `GITCANDY_SONNETDB_TOKEN`，不得提交 token 或 SonnetDB 配置文件中的密钥。
4. 构建仓库前执行 `git submodule update --init --recursive`，GitCandy 当前直接引用 `external/SonnetDB` 中已修复的 EF provider。

该主机没有 swap，部署准备时可用内存约 560 MiB。Compose 默认把 GitCandy 限制为 384 MiB、保留 128 MiB；上线前后都要检查 `docker stats --no-stream`，不要在主机上并行构建 GitCandy 与 SonnetDB 镜像。

## SonnetDB 兼容升级

生产环境当前使用 `/opt/sub2api/docker-compose.yml` 中的 `iotsharp/sonnetdb:latest`，数据目录为 `/opt/sub2api/data/sonnetdb`。GitCandy 依赖本子模块中的数据库引擎与 EF provider 修复，不能只升级 GitCandy 客户端而继续使用旧 SonnetDB Server 镜像。

首次部署顺序：

1. 从递归初始化的 `external/SonnetDB` 构建带固定版本标签的 Server 镜像，不覆盖 `latest`。
2. 暂停所有 SonnetDB 写入方并停止 SonnetDB，备份 `/opt/sub2api/data/sonnetdb` 与 `/opt/sub2api/config/sonnetdb/appsettings.json`，记录当前镜像 ID。
3. 将 `/opt/sub2api/docker-compose.yml` 的 `sonnetdb.image` 改为固定补丁标签，启动 SonnetDB 并先验证现有 sonnet.vip 业务。
4. SonnetDB 健康且旧业务验证通过后，再启动 GitCandy 让它创建 `gitcandy` 数据库和应用 migration。

若 SonnetDB 升级验证失败，停止写入、恢复原镜像 ID 和数据备份，再恢复现有业务；不要在失败的数据库版本上继续启动 GitCandy。

## 与现有 Caddy 结合

首次创建固定边缘网络：

```bash
docker network create --driver bridge --subnet 172.31.0.0/24 sonnet-gitcandy-edge
```

现有 `/opt/sub2api/docker-compose.yml` 的 `caddy` 服务需要追加：

```yaml
services:
  caddy:
    networks:
      frontend:
      gitcandy-edge:
        ipv4_address: 172.31.0.2

networks:
  gitcandy-edge:
    external: true
    name: sonnet-gitcandy-edge
```

把本目录 `Caddyfile` 中的站点块追加到现有 `/opt/sub2api/Caddyfile`。固定地址 `172.31.0.2` 必须与 GitCandy 的 `KnownProxies` 一致，不能改成信任任意代理。

## 部署

服务器目录使用 `/opt/gitcandy`，持久数据使用 bind mount `/opt/gitcandy/data`。容器以 ASP.NET 基础镜像的非 root 用户运行，首次部署先设置目录所有者：

```bash
install -d -o 1654 -g 1654 /opt/gitcandy/data
cd /opt/gitcandy
cp .env.example .env
docker compose config --quiet
docker compose up -d
```

启动时 EF Core 会通过 SonnetDB 远程 provider 创建 `gitcandy` 数据库并应用独立 migration；失败时 GitCandy 不会开始监听 Web/SSH。

## 验证

```bash
curl --fail http://127.0.0.1:18080/health/ready
curl --fail https://gitcandy.com/health/ready
docker compose ps
docker compose logs --tail 200 gitcandy
```

DNS 和 TLS 生效后，至少验证 Web 注册/登录、私有仓库权限，以及真实 `git clone`、`git fetch`、`git push`。SSH URL 使用 `ssh://git@gitcandy.com:2222/{namespace}/{repository}.git`。

HTTP `http://gitcandy.com` 只作为到 HTTPS 的重定向入口；Identity Secure Cookie 和 Git 凭据不应在明文 HTTP 上使用。

## 生产验收记录

2026-07-13 已完成 DNS/TLS、Web 登录、私有权限、HTTP/SSH clone/fetch/push、Git LFS、端口边界、资源限制、一致备份恢复和镜像回滚。镜像摘要、commit/OID、维护窗口和已知边界见 [M12.6 生产部署验收](../../docs/migration/m12-6-sonnetdb-production-acceptance.md)。
