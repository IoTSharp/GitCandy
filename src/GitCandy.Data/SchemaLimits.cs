namespace GitCandy.Data;

internal static class SchemaLimits
{
    public const int IdentityKey = 450;
    public const int IdentityStoreKey = 128;
    public const int UserDisplayName = 128;
    public const int UserDescription = 512;
    public const int RepositoryName = 50;
    public const int RepositoryStorageName = 100;
    public const int RepositoryDescription = 500;
    public const int TeamName = 20;
    public const int NamespaceSlug = 50;
    public const int RenameReason = 500;
    public const int TeamDescription = 500;
    public const int SshKeyType = 20;
    public const int SshFingerprint = 47;
    public const int SshPublicKey = 600;
    public const int IssueTitle = 256;
    public const int IssueBody = 65536;
    public const int IssueDetail = 1000;
    public const int IssueLabelName = 50;
    public const int IssueLabelColor = 6;
    public const int IssueLabelDescription = 200;
    public const int IssueMilestoneTitle = 100;
    public const int IssueMilestoneDescription = 2000;
    public const int CommitSha = 64;
}
