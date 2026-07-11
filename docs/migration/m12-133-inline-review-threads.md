# M12 #133 inline review threads

## Scope

This slice adds inline Pull Request review threads to Files changed. An authenticated repository reader can create a single-line or range thread on the old or new side of a merge-base diff, reply to existing threads, and resolve or reopen a thread they created. Repository owners may also resolve or reopen threads.

Ordinary Pull Request conversation and inline review threads remain separate models. Reviewer assignment and approve/request-changes decisions remain in M12 #134.

## Anchor integrity

GitCandy validates path, side, and line range against the server-side immutable base/head diff. It stores original base/head SHA, path, side, range, and a bounded JSON hunk-context signature. Client-supplied text is never accepted as anchor context.

When the source or target branch tip changes, GitCandy refreshes the current SHA snapshot and searches the new merge-base diff for one unique context match. A missing or ambiguous match clears the current path/range and marks the thread `Outdated`; it never silently attaches the thread to the same numeric line.

## Database impact

SQLite and SQL Server receive `PullRequestReviewThreads` and `PullRequestReviewComments`. Threads cascade with their Pull Request, replies cascade with their thread, and Identity authors remain protected by restrictive foreign keys. Status and chronological indexes support Files changed rendering without scanning unrelated Pull Requests.

Existing Pull Requests need no backfill. They start with no review threads.

## Upgrade and rollback

Apply the normal EF Core migration before serving review POST routes. Rolling back drops only the two review tables and permanently removes their thread/reply history; it does not modify Git refs or objects. Back up the database before rollback when review history must be retained.

## Verification

- SQLite migration-backed thread, reply, sanitized Markdown, resolve, and outdated-state tests.
- Real bare-repository line capture, hunk-context remap, and invalid-range tests.
- SQL Server migration generation and pending-model checks.
- ASP.NET Core MVC authorization, antiforgery, public-read, and private-repository boundaries remain enforced by the existing PR controller integration path.
