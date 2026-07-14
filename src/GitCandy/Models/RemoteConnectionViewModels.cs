using System.ComponentModel.DataAnnotations;
using GitCandy.Remotes;

namespace GitCandy.Models;

public sealed class RemoteConnectionIndexViewModel
{
    public IReadOnlyList<RemoteProviderDescriptor> Providers { get; init; } = [];

    public IReadOnlyList<RemoteConnectionSummary> Connections { get; init; } = [];

    public RemoteConnectionFormViewModel Form { get; init; } = new();

    public long? SelectedConnectionId { get; init; }

    public RemoteRepositoryDiscoveryResult? Discovery { get; init; }

    public RemoteConnectionSummary? SelectedConnection => SelectedConnectionId is long connectionId
        ? Connections.SingleOrDefault(connection => connection.Id == connectionId)
        : null;
}

public sealed class RemoteConnectionFormViewModel
{
    [Required]
    public RemoteProviderKind Provider { get; set; } = RemoteProviderKind.GitHub;

    [Required]
    [Display(Name = "Authentication method")]
    public RemoteAuthenticationKind AuthenticationKind { get; set; } =
        RemoteAuthenticationKind.PersonalAccessToken;

    [Required, StringLength(16384)]
    [DataType(DataType.Password)]
    public string Secret { get; set; } = string.Empty;

    [Required, StringLength(2048)]
    [Display(Name = "Granted scopes")]
    public string GrantedScopes { get; set; } = "repo";
}
