# M0 #001 测试数据与样例仓库

记录日期：2026-07-09

## 验收结论

- 已新增可重复运行的 fixture 生成脚本：`tools/migration/m0-001/New-M0SampleData.ps1`。
- 生成物默认写入 `artifacts/migration/m0-001/`，该目录由 `.gitignore` 的 `artifacts/` 规则排除，不提交 bare repository 对象、数据库实例或本机状态。
- fixture 包含新库样例数据规格、管理员、普通用户、团队、公有仓库、私有仓库和两个 bare git repository。
- 样例数据不保存明文密码；后续 Identity seed 代码必须从本机 secret 或环境变量读取密码。

## 生成命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\migration\m0-001\New-M0SampleData.ps1 -Verify
```

生成后关键文件：

- `artifacts/migration/m0-001/seed-data.json`
- `artifacts/migration/m0-001/manifest.json`
- `artifacts/migration/m0-001/repositories/public-demo.git`
- `artifacts/migration/m0-001/repositories/private-demo.git`

`-Verify` 会对两个 bare repository 执行本地 `git clone` 和 `git fetch --all --tags` smoke test。若需要查看临时 worktree，可追加 `-KeepWorktrees`。

已验证：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\migration\m0-001\New-M0SampleData.ps1 -Verify`：通过。
- `public-demo.git`：bare repository，可 clone/fetch，包含 `main`、`docs` 和 `v0.1.0`。
- `private-demo.git`：bare repository，可 clone/fetch，包含 `main`、`release/v1` 和 `internal-v0.1.0`。

## 样例用户

| 用户 | 邮箱 | 角色语义 |
| --- | --- | --- |
| `admin` | `admin@gitcandy.local` | 系统管理员，可读写所有仓库 |
| `alice` | `alice@gitcandy.local` | 普通用户，`core` 团队管理员，两个样例仓库 owner |
| `bob` | `bob@gitcandy.local` | 普通用户，`core` 团队成员，通过团队获得私有仓库读写权限 |
| `carol` | `carol@gitcandy.local` | 普通用户，无私有仓库权限，用于权限不足场景 |

密码不会写入 fixture。后续需要实际登录 seed 时使用以下本机环境变量或等价 user-secrets：

- `GITCANDY_M0_ADMIN_PASSWORD`
- `GITCANDY_M0_ALICE_PASSWORD`
- `GITCANDY_M0_BOB_PASSWORD`
- `GITCANDY_M0_CAROL_PASSWORD`

## 样例团队

| 团队 | 成员 | 语义 |
| --- | --- | --- |
| `core` | `alice` 管理员，`bob` 成员 | 用于验证团队管理员、团队成员和团队仓库授权 |

## 样例仓库

| 仓库 | 可见性 | 匿名读 | 匿名写 | 主要 refs |
| --- | --- | --- | --- | --- |
| `public-demo` | public | 是 | 否 | `main`、`docs`、`v0.1.0` |
| `private-demo` | private | 否 | 否 | `main`、`release/v1`、`internal-v0.1.0` |

兼容路由样例：

- `git/public-demo.git/{*verb}`
- `git/public-demo/{*verb}`
- `git/private-demo.git/{*verb}`
- `git/private-demo/{*verb}`

## 权限验收矩阵

| Actor | Repository | Read | Write | 来源 |
| --- | --- | --- | --- | --- |
| anonymous | `public-demo` | 是 | 否 | `AllowAnonymousRead=true` |
| anonymous | `private-demo` | 否 | 否 | private repository |
| `admin` | `private-demo` | 是 | 是 | system administrator |
| `alice` | `public-demo` | 是 | 是 | repository owner |
| `alice` | `private-demo` | 是 | 是 | repository owner |
| `bob` | `private-demo` | 是 | 是 | `core` team role |
| `carol` | `private-demo` | 否 | 否 | no repository or team role |

## 本地 Git 验证

生成 fixture 后可手动检查：

```powershell
git -C .\artifacts\migration\m0-001\repositories\public-demo.git rev-parse --is-bare-repository
git -C .\artifacts\migration\m0-001\repositories\private-demo.git for-each-ref --format="%(refname:short)"
git clone .\artifacts\migration\m0-001\repositories\public-demo.git .\artifacts\migration\m0-001\manual-public-clone
git -C .\artifacts\migration\m0-001\manual-public-clone fetch --all --tags
```

这些命令只验证本地 bare repository 是否可用；HTTP/SSH clone、fetch、push 行为会在 M0 #003、#004 和 #008 继续补齐。

## 兼容性影响

本任务只新增 fixture 生成脚本和迁移文档，不改变：

- 公开路由和 Git URL
- 数据库 schema、索引、默认值或应用 seed 行为
- Identity cookie、Basic Auth 或权限语义
- 配置键、环境变量或部署方式
- Git HTTP/SSH 协议行为和响应 header
- repository、cache、App_Data、host keys、logs 等文件系统布局
