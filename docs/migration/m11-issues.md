# M11 Issues migration record

## Scope

Milestone 11 adds repository Issues, comments, timeline events, edit history, labels, milestones, assignees, subscriptions, notifications, references, relations, templates, discussion governance, and Razor MVC pages. It does not add Pull Requests, boards, email delivery, or arbitrary custom fields.

Public Issue pages use `/{namespace}/{repository}/issues` and `/{namespace}/{repository}/issues/{number}`. Existing Web, Smart HTTP, SSH, Identity cookie, and repository storage paths are unchanged.

## Database migration

SQLite and SQL Server receive the `IssuesAndDiscussions` migration. It creates:

- `WorkItemSequences`, `Issues`, `IssueComments`, `IssueEdits`, and `IssueTimelineEvents`
- `IssueLabels`, `IssueLabelLinks`, and `IssueMilestones`
- `IssueSubscriptions`, `IssueNotifications`, `IssueReferences`, and `IssueRelations`

The migration inserts a `WorkItemSequences` row with `NextNumber = 1` for every existing repository. New repositories create this row in the same EF aggregate. The `(RepositoryId, Number)` unique index and serializable sequence update protect repository-scoped numbering. Issue and comment `Version` fields are EF concurrency tokens.

Metadata is archived instead of physically removed so existing Issue history remains readable. Notification delivery and inbox reads both recheck repository permission. Repository deletion cascades owned Issue data while SQL Server `NO ACTION` edges avoid multiple cascade paths.

## Markdown dependencies

M11 adds Markdig for maintained CommonMark/fenced-code/task-list parsing and HtmlSanitizer for a final allow-list pass. Raw HTML is disabled before rendering; scripts, event handlers, images, and unsafe URL schemes are not allowed. This avoids maintaining a security-sensitive custom parser and sanitizer. Both packages are centrally pinned in `Directory.Packages.props`.

## Templates and automatic closing

Templates are read only through the managed Git repository browser from `.gitcandy/ISSUE_TEMPLATE`. Template names accept a bounded filename segment and cannot select paths outside that directory. Missing, binary, oversized, or invalid templates degrade to an empty form.

After a successful Smart HTTP or built-in SSH `receive-pack`, GitCandy reads the latest default-branch commit message and applies `fixes`, `closes`, or `resolves #N`. Closing is idempotent and recorded in the Issue timeline. Failure in this post-push collaboration step is logged and does not turn an already successful Git push into a protocol failure.

## Upgrade and rollback

1. Back up the database before deploying M11.
2. Deploy the new application; startup migration creates and backfills the Issue schema.
3. Verify the repository Issue list, authenticated create/comment flow, notification inbox, and a private-repository denial.
4. For rollback, stop GitCandy, restore the pre-M11 database backup, and deploy the previous application version.

Down-migrating drops all M11 Issue data. Do not rely on a down migration as a data-preserving rollback.
