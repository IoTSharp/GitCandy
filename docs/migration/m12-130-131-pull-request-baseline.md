# M12 #130-#131 Pull Request baseline

## Scope

This vertical slice starts Milestone 12 with same-repository Pull Requests. It adds:

- canonical `/{namespace}/{repository}/pulls` routes plus the `/Repository/Pulls/{name}` redirect;
- shared repository work-item numbers for Issues and Pull Requests;
- draft/ready, edit, close, and reopen state transitions;
- original/current base and head SHA snapshots and optimistic concurrency;
- server-maintained `refs/pull/{number}/head` refs.

Reviewers, mergeability, merge/squash, Issue closing, and cross-fork Pull Requests remain in M12 `#134-#139`. Conversation/Commits/Files changed and inline review threads are covered by the subsequent #132 and #133 slices.

## Database impact

SQLite and SQL Server receive the `PullRequests` and `PullRequestTimelineEvents` tables. The migrations add repository, Identity user, and timeline foreign keys plus unique indexes for repository work-item numbers and active source/target pairs.

Existing repositories and Issues are not rewritten. Existing `WorkItemSequences.NextNumber` values continue unchanged, so the first Pull Request receives the next number after any existing Issue. No legacy Pull Request data is imported.

## Git repository impact

Creating a Pull Request writes `refs/pull/{number}/head` to the bare repository. The ref preserves the proposed head commit if the source branch is later deleted and remains visible to `upload-pack` fetches.

GitCandy adds `receive.hideRefs = refs/pull/` as a local multivalue configuration entry. Existing `receive.hideRefs` values are preserved. This prevents Git clients from creating or updating GitCandy-owned PR refs.

## Upgrade and rollback

Apply the normal EF Core migration before serving the new routes. The application startup migration path handles this for supported deployments.

The migration `Down` operation drops only the two new tables and their indexes. It does not delete Git objects or refs. Before rollback, back up the database and repositories. After rollback, `refs/pull/*` and the matching `receive.hideRefs` entry are harmless but may be removed with an explicitly reviewed repository maintenance operation. Do not recursively delete repository paths.

Reapplying the migration does not recreate Pull Request rows from remaining refs; database restore is required when Pull Request history must be retained.

## Verification

- SQLite migration-backed create/read/update tests.
- SQL Server idempotent migration SQL generation.
- Real bare-repository branch comparison and internal ref tests.
- Kestrel login/create/detail/private-repository denial test.
