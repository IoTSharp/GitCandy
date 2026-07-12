# M10 stable namespace migration

## URL contract

- Web canonical: `/{namespace}/{repository}`.
- Git HTTP: `/{namespace}/{repository}.git/info/refs`, `git-upload-pack`, and `git-receive-pack`.
- SSH: `ssh://git@host:port/{namespace}/{repository}.git`.
- Legacy `/git/{project}[.git]`, `/Repository/...`, no-suffix Git remotes, and retained alias addresses are not routed and return not found.
- Slugs use ordinal, invariant uppercase normalization for uniqueness. Original casing is retained for display.
- A slug starts with an ASCII letter, contains only ASCII letters, digits, `.`, `_`, or `-`, has 2-50 characters, and cannot end in `.git`.
- Reserved root slugs are `account`, `api`, `assets`, `git`, `health`, `home`, `identity`, `legacy`, `repository`, `setting`, `signin-oidc`, `signout-callback-oidc`, and `team`.

## Data migration

`StableNamespaces` performs the following provider-specific backfill before creating final indexes and foreign keys:

1. Creates the reserved `legacy` namespace and system route claims.
2. Creates one stable namespace for every Identity user and team.
3. Copies existing team names into `DisplayName` and repository names into immutable `StorageName`.
4. Assigns each repository to the lexically first `IsOwner` user namespace. Ownerless metadata remains under `legacy`.
5. Creates current repository claims and explicit legacy mappings without moving repository directories.

The migration intentionally fails if a user name, team name, active claim, or reserved route conflicts case-insensitively. Back up the database and repository/cache roots first, then resolve conflicts before retrying. Do not edit the migration to auto-suffix public names.

## Runtime behavior

- `GitCandy:Namespaces:AliasRetentionDays` defaults to `365`.
- `RenameLimit` defaults to `3` successful user/team slug changes in `RenameWindowDays=7` rolling days.
- Failed, conflicting, and administrator override changes do not consume normal quota.
- Serializable transactions, a namespace version concurrency token, and unique claim keys protect concurrent changes.
- Aliases point directly to stable namespace/repository IDs. Cleanup removes expired claims idempotently; administrators can extend active aliases with an audit reason.
- Repository create/list/detail and code-browser navigation only generate canonical namespace URLs. Legacy and retained alias addresses return not found without redirecting.
- Git HTTP and LFS only accept the canonical `.git` endpoint. RPC bodies remain streamed.
- OpenSSH and the built-in listener only accept current namespace/repository paths; legacy and alias paths are rejected before authorization and transport startup.
- Deleting a repository converts its active claims to reserved tombstones before deleting metadata and storage, preventing immediate name reuse.

## Verification matrix

- SQLite migration and model: `GitCandy.Data.Tests`, including three-success/fourth-blocked quota, reserved/occupied failures, expiry idempotence, direct alias resolution, and concurrent final-quota requests.
- SQL Server: offline idempotent migration SQL contains all namespace/alias/claim/event tables, composite unique indexes, backfill SQL, and stable-ID foreign keys.
- Web: canonical rendering, canonical link, private-resource authorization, and 404 responses for legacy and alias routes.
- Git HTTP/LFS: real protocol v2 canonical `.git` clone/fetch/push/LFS, authenticated push, 24 MiB pack streaming, and 404 responses for legacy, alias, and no-suffix remotes.
- SSH: real current namespace/repository clone/fetch/push with the shared resolver, permission query, path resolver, and transport backend; legacy and alias commands are rejected before transport execution.

## Rollback

The migration does not rename physical repository directories, so repository data remains usable by the previous application. Database rollback is not an online compatibility operation: stop GitCandy, restore the pre-M10 database backup, deploy the previous application package, and verify Web login plus Git HTTP/SSH clone/fetch/push. Do not apply the generated `Down` migration to a live database after users have renamed namespaces because it discards alias and audit history and restores global repository-name uniqueness.
