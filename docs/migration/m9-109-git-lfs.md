# M9 #109 Git LFS v2

## 变更点

- 新增 Git LFS v2 basic transfer：batch、upload、download、HEAD existence 和 verify。
- LFS 使用独立 Git Basic authentication，复用 repository read/write policy；私有仓库匿名 batch 返回 401 Basic challenge。
- 对象写入 `CachePath/lfs/{normalizedRepository}/{oid-prefix}/{oid}`；上传先进入同卷 `.tmp`，流式计算 SHA-256 和 size 后原子 rename。
- 支持单对象上限、repository quota、operation timeout、流缓冲配置和 HTTP range download。
- locking API 不在本切片内。

## 对应 ROADMAP

- Milestone 9 / #109，独立于 #101 UI 收尾。

## 测试说明

- `GitLfsObjectStoreTests`：SHA-256/size、临时文件清理、原子提交、quota、OID 与 repository path 边界。
- `GitLfsIntegrationTests`：真实 `git-lfs 3.7.1` push、fetch、clone；另验证 batch 鉴权、HEAD、range download 和错误 verify。

## 配置与部署

- `GitCandy:Lfs:Enabled` 默认 `true`。
- `MaxObjectBytes` 默认 4 GiB，`RepositoryQuotaBytes=0` 表示无额外仓库总量限制。
- reverse proxy 必须允许 `/git/*.git/info/lfs/*` 流式 PUT/GET，body/timeout 不得低于 GitCandy 配置。
- LFS 位于 cache 根，但它是不可重建的持久对象数据；备份、恢复和磁盘容量告警必须包含 `CachePath/lfs`。

## 回滚

- 设置 `GitCandy:Lfs:Enabled=false` 可立即关闭新 endpoint，不影响普通 Git clone/fetch/push。
- 回滚应用前保留 `CachePath/lfs`；旧版本会忽略这些对象，重新升级后可继续使用。
- 确认没有 LFS pointer 依赖后才能人工删除对象目录；仅删除 LFS cache 会使现有 pointer 无法 checkout。
