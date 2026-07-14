using System.Security.Claims;
using GitCandy.Authentication;
using GitCandy.Controllers;
using GitCandy.Models;
using GitCandy.Remotes;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Tests;

[TestClass]
public sealed class RemoteConnectionControllerTests
{
    [TestMethod]
    public async Task Connect_WithRejectedCredential_ClearsSecretFromViewModelAndModelState()
    {
        const string secret = "credential-that-must-not-be-rendered";
        var controller = new RemoteConnectionController(
            new RejectingRemoteConnectionService(),
            new FixtureCurrentUser());
        controller.ModelState.SetModelValue("Form.Secret", secret, secret);

        var result = await controller.Connect(
            new RemoteConnectionFormViewModel
            {
                Provider = RemoteProviderKind.GitHub,
                AuthenticationKind = RemoteAuthenticationKind.PersonalAccessToken,
                Secret = secret,
                GrantedScopes = "repo"
            },
            CancellationToken.None);

        var view = result as ViewResult;
        Assert.IsNotNull(view);
        var model = view.Model as RemoteConnectionIndexViewModel;
        Assert.IsNotNull(model);
        Assert.AreEqual(string.Empty, model.Form.Secret);
        Assert.IsFalse(controller.ModelState.ContainsKey("Form.Secret"));
        Assert.IsFalse(model.ToString()?.Contains(secret, StringComparison.Ordinal) == true);
    }

    private sealed class RejectingRemoteConnectionService : IRemoteConnectionService
    {
        public IReadOnlyList<RemoteProviderDescriptor> AvailableProviders { get; } =
        [
            new(
                RemoteProviderKind.GitHub,
                "GitHub",
                new Uri("https://github.com/"),
                RemoteProviderCapabilities.AccountConnection | RemoteProviderCapabilities.RepositoryDiscovery,
                new HashSet<RemoteAuthenticationKind>([RemoteAuthenticationKind.PersonalAccessToken]),
                new HashSet<string>(["repo"], StringComparer.Ordinal))
        ];

        public Task<IReadOnlyList<RemoteConnectionSummary>> GetForUserAsync(
            string userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteConnectionSummary>>([]);

        public Task<RemoteConnectionResult> ConnectUserAsync(
            string userId,
            RemoteUserConnectionRequest request,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual("credential-that-must-not-be-rendered", request.Secret.Value);
            return Task.FromResult(new RemoteConnectionResult(
                null,
                new RemoteProviderDiagnostic(false, "credential_rejected", "The credential was rejected.")));
        }

        public Task<RemoteProviderDiagnostic?> TestUserConnectionAsync(
            string userId,
            long connectionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RemoteProviderDiagnostic?>(null);

        public Task<RemoteRepositoryDiscoveryResult?> DiscoverRepositoriesAsync(
            string userId,
            long connectionId,
            string? cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RemoteRepositoryDiscoveryResult?>(null);

        public Task<bool> DisconnectUserAsync(
            string userId,
            long connectionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FixtureCurrentUser : ICurrentUser
    {
        public ClaimsPrincipal Principal { get; } = new(new ClaimsIdentity());
        public bool IsAuthenticated => true;
        public string? UserId => "user-1";
        public string? UserName => "remote-user";
        public bool IsAdministrator => false;
        public CancellationToken RequestAborted => CancellationToken.None;
    }
}
