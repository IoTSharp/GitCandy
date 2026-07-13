namespace GitCandy.Data.Domain;

public sealed class GitCandyBranchProtectionRule
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public int PushAccess { get; set; }
    public int MergeAccess { get; set; }
    public bool AllowForcePushes { get; set; }
    public bool AllowDeletions { get; set; }
    public bool AllowAdministratorBypass { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public GitCandyRepository? Repository { get; set; }
    public ICollection<GitCandyBranchProtectionRequiredCheck> RequiredChecks { get; } = [];
}

public sealed class GitCandyBranchProtectionRequiredCheck
{
    public long RuleId { get; set; }
    public string Context { get; set; } = string.Empty;
    public GitCandyBranchProtectionRule? Rule { get; set; }
}

public sealed class GitCandyGovernanceAuditEvent
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public string? ActorUserId { get; set; }
    public long? DeployKeyId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string ReferenceName { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}
