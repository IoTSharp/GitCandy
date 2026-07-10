using GitCandy.Configuration;
using GitCandy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitCandy.Controllers;

[Authorize(Policy = AuthorizationPolicies.Administrator)]
public sealed class SettingController(IOptions<GitCandyApplicationOptions> options) : CandyControllerBase
{
    private readonly GitCandyApplicationOptions _options = options.Value;

    [HttpGet]
    public IActionResult Edit()
    {
        return View(new SettingViewModel
        {
            IsPublicServer = _options.IsPublicServer,
            ForceSsl = _options.ForceSsl,
            SslPort = _options.SslPort,
            AllowRegisterUser = _options.AllowRegisterUser,
            AllowRepositoryCreation = _options.AllowRepositoryCreation,
            RepositoryPath = _options.RepositoryPath,
            CachePath = _options.CachePath,
            GitCorePath = _options.GitCorePath,
            NumberOfCommitsPerPage = _options.NumberOfCommitsPerPage,
            NumberOfItemsPerList = _options.NumberOfItemsPerList,
            NumberOfRepositoryContributors = _options.NumberOfRepositoryContributors,
            SshPort = _options.SshPort,
            EnableSsh = _options.EnableSsh
        });
    }
}
