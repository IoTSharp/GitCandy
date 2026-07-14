using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using GitCandy.Authentication;
using GitCandy.Configuration;
using GitCandy.Enterprise;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GitCandy.Controllers;

[ApiController]
[Route("scim/v2/{connectionId:long}")]
[Authorize(AuthenticationSchemes = GitCandyAuthenticationSchemes.ScimBearer)]
[EnableRateLimiting(ApiRateLimitPolicies.Write)]
[Produces("application/scim+json")]
public sealed partial class ScimController(IScimProvisioningService provisioningService) : ControllerBase
{
    private const string UserSchema = "urn:ietf:params:scim:schemas:core:2.0:User";
    private const string GroupSchema = "urn:ietf:params:scim:schemas:core:2.0:Group";
    private const string ListSchema = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
    private const string PatchSchema = "urn:ietf:params:scim:api:messages:2.0:PatchOp";
    private const string ErrorSchema = "urn:ietf:params:scim:api:messages:2.0:Error";
    private readonly IScimProvisioningService _provisioningService = provisioningService;

    [HttpGet("ServiceProviderConfig")]
    public IActionResult ServiceProviderConfig(long connectionId)
    {
        if (!MatchesConnection(connectionId)) return Forbid();
        return ScimOk(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig" },
            patch = new { supported = true },
            bulk = new { supported = false, maxOperations = 0, maxPayloadSize = 0 },
            filter = new { supported = true, maxResults = 100 },
            changePassword = new { supported = false },
            sort = new { supported = false },
            etag = new { supported = false },
            authenticationSchemes = new[] { new { type = "oauthbearertoken", name = "Bearer", primary = true } }
        });
    }

    [HttpGet("Users")]
    public async Task<IActionResult> GetUsers(
        long connectionId,
        int startIndex = 1,
        int count = 100,
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (!MatchesConnection(connectionId)) return Forbid();
        if (!TryParseFilter(filter, ["externalId", "userName"], out var attribute, out var value))
        {
            return ScimError(400, "invalidFilter", "The SCIM filter is not supported.");
        }

        var page = await _provisioningService.GetUsersAsync(
            connectionId,
            startIndex,
            count,
            attribute,
            value,
            cancellationToken);
        return ScimOk(new
        {
            schemas = new[] { ListSchema },
            totalResults = page.TotalResults,
            startIndex = page.StartIndex,
            itemsPerPage = page.ItemsPerPage,
            Resources = page.Resources.Select(item => ToUserResponse(connectionId, item))
        });
    }

    [HttpGet("Users/{id:long}", Name = "ScimGetUser")]
    public async Task<IActionResult> GetUser(
        long connectionId,
        long id,
        CancellationToken cancellationToken = default)
    {
        if (!MatchesConnection(connectionId)) return Forbid();
        var user = await _provisioningService.GetUserAsync(connectionId, id, cancellationToken);
        return user is null
            ? ScimError(404, null, "The SCIM user was not found.")
            : ScimOk(ToUserResponse(connectionId, user));
    }

    [HttpPost("Users")]
    public async Task<IActionResult> CreateUser(
        long connectionId,
        ScimUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!MatchesConnection(connectionId)) return Forbid();
        var result = await _provisioningService.UpsertUserAsync(
            connectionId,
            request.ToData(),
            cancellationToken);
        if (!result.Succeeded || result.Resource is null)
        {
            return WriteError(result.ErrorCode);
        }

        return ScimResult(
            result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK,
            ToUserResponse(connectionId, result.Resource),
            Url.RouteUrl("ScimGetUser", new { connectionId, id = result.Resource.Id }));
    }

    [HttpPatch("Users/{id:long}")]
    public async Task<IActionResult> PatchUser(
        long connectionId,
        long id,
        ScimPatchRequest patch,
        CancellationToken cancellationToken = default)
    {
        if (!MatchesConnection(connectionId)) return Forbid();
        if (!patch.Schemas.Contains(PatchSchema, StringComparer.Ordinal))
        {
            return ScimError(400, "invalidSyntax", "The SCIM PATCH schema is required.");
        }

        var current = await _provisioningService.GetUserAsync(connectionId, id, cancellationToken);
        if (current is null)
        {
            return ScimError(404, null, "The SCIM user was not found.");
        }

        var data = new ScimUserData(
            current.ExternalId,
            current.UserName,
            current.Email,
            current.DisplayName,
            current.Active);
        if (!TryApplyUserPatch(data, patch.Operations, out data))
        {
            return ScimError(400, "invalidValue", "The SCIM user patch is not supported.");
        }

        var result = await _provisioningService.PatchUserAsync(connectionId, id, data, cancellationToken);
        return !result.Succeeded || result.Resource is null
            ? WriteError(result.ErrorCode)
            : ScimOk(ToUserResponse(connectionId, result.Resource));
    }

    [HttpGet("Groups")]
    public async Task<IActionResult> GetGroups(
        long connectionId,
        int startIndex = 1,
        int count = 100,
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (!MatchesConnection(connectionId)) return Forbid();
        if (!TryParseFilter(filter, ["externalId", "displayName"], out var attribute, out var value))
        {
            return ScimError(400, "invalidFilter", "The SCIM filter is not supported.");
        }

        var page = await _provisioningService.GetGroupsAsync(
            connectionId,
            startIndex,
            count,
            attribute,
            value,
            cancellationToken);
        return ScimOk(new
        {
            schemas = new[] { ListSchema },
            totalResults = page.TotalResults,
            startIndex = page.StartIndex,
            itemsPerPage = page.ItemsPerPage,
            Resources = page.Resources.Select(item => ToGroupResponse(connectionId, item))
        });
    }

    [HttpGet("Groups/{id:long}", Name = "ScimGetGroup")]
    public async Task<IActionResult> GetGroup(
        long connectionId,
        long id,
        CancellationToken cancellationToken = default)
    {
        if (!MatchesConnection(connectionId)) return Forbid();
        var group = await _provisioningService.GetGroupAsync(connectionId, id, cancellationToken);
        return group is null
            ? ScimError(404, null, "The SCIM group was not found.")
            : ScimOk(ToGroupResponse(connectionId, group));
    }

    [HttpPost("Groups")]
    public async Task<IActionResult> CreateGroup(
        long connectionId,
        ScimGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!MatchesConnection(connectionId)) return Forbid();
        if (!request.TryToData(out var data))
        {
            return ScimError(400, "invalidValue", "One or more SCIM group members are invalid.");
        }

        var result = await _provisioningService.UpsertGroupAsync(connectionId, data, cancellationToken);
        if (!result.Succeeded || result.Resource is null)
        {
            return WriteError(result.ErrorCode);
        }

        return ScimResult(
            result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK,
            ToGroupResponse(connectionId, result.Resource),
            Url.RouteUrl("ScimGetGroup", new { connectionId, id = result.Resource.Id }));
    }

    [HttpPatch("Groups/{id:long}")]
    public async Task<IActionResult> PatchGroup(
        long connectionId,
        long id,
        ScimPatchRequest patch,
        CancellationToken cancellationToken = default)
    {
        if (!MatchesConnection(connectionId)) return Forbid();
        var current = await _provisioningService.GetGroupAsync(connectionId, id, cancellationToken);
        if (current is null)
        {
            return ScimError(404, null, "The SCIM group was not found.");
        }

        var data = new ScimGroupData(
            current.ExternalId,
            current.DisplayName,
            current.MemberIds);
        if (!TryApplyGroupPatch(data, patch.Operations, out data))
        {
            return ScimError(400, "invalidValue", "The SCIM group patch is not supported.");
        }

        var result = await _provisioningService.PatchGroupAsync(connectionId, id, data, cancellationToken);
        return !result.Succeeded || result.Resource is null
            ? WriteError(result.ErrorCode)
            : ScimOk(ToGroupResponse(connectionId, result.Resource));
    }

    private bool MatchesConnection(long connectionId) => long.TryParse(
        User.FindFirstValue(ScimBearerAuthenticationHandler.ConnectionIdClaim),
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var authenticatedConnectionId)
        && authenticatedConnectionId == connectionId;

    private IActionResult WriteError(string? errorCode) => errorCode switch
    {
        "notFound" => ScimError(404, null, "The SCIM resource or connection was not found."),
        "uniqueness" => ScimError(409, "uniqueness", "The SCIM identity conflicts with an existing account."),
        "lastTeamOwner" => ScimError(409, "mutability", "The last TeamOwner cannot be deactivated."),
        _ => ScimError(400, errorCode ?? "invalidValue", "The SCIM resource could not be written.")
    };

    private IActionResult ScimOk(object value) => ScimResult(StatusCodes.Status200OK, value);

    private IActionResult ScimError(int status, string? scimType, string detail) => ScimResult(
        status,
        new
        {
            schemas = new[] { ErrorSchema },
            status = status.ToString(CultureInfo.InvariantCulture),
            scimType,
            detail
        });

    private IActionResult ScimResult(int status, object value, string? location = null)
    {
        if (!string.IsNullOrWhiteSpace(location))
        {
            Response.Headers.Location = location;
        }

        return new ObjectResult(value)
        {
            StatusCode = status,
            ContentTypes = { "application/scim+json" }
        };
    }

    private object ToUserResponse(long connectionId, ScimUserResource user) => new
    {
        schemas = new[] { UserSchema },
        id = user.Id.ToString(CultureInfo.InvariantCulture),
        externalId = user.ExternalId,
        userName = user.UserName,
        displayName = user.DisplayName,
        active = user.Active,
        emails = string.IsNullOrWhiteSpace(user.Email)
            ? Array.Empty<object>()
            : new object[] { new { value = user.Email, primary = true } },
        meta = new
        {
            resourceType = "User",
            created = user.CreatedAt,
            lastModified = user.LastModifiedAt,
            location = Url.RouteUrl("ScimGetUser", new { connectionId, id = user.Id })
        }
    };

    private object ToGroupResponse(long connectionId, ScimGroupResource group) => new
    {
        schemas = new[] { GroupSchema },
        id = group.Id.ToString(CultureInfo.InvariantCulture),
        externalId = group.ExternalId,
        displayName = group.DisplayName,
        members = group.MemberIds.Select(id => new
        {
            value = id.ToString(CultureInfo.InvariantCulture),
            type = "User",
            @ref = Url.RouteUrl("ScimGetUser", new { connectionId, id })
        }),
        meta = new
        {
            resourceType = "Group",
            created = group.CreatedAt,
            lastModified = group.LastModifiedAt,
            location = Url.RouteUrl("ScimGetGroup", new { connectionId, id = group.Id })
        }
    };

    private static bool TryParseFilter(
        string? filter,
        IReadOnlyCollection<string> allowedAttributes,
        out string? attribute,
        out string? value)
    {
        attribute = null;
        value = null;
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var match = EqualityFilterRegex().Match(filter);
        if (!match.Success || !allowedAttributes.Contains(match.Groups[1].Value, StringComparer.Ordinal))
        {
            return false;
        }

        attribute = allowedAttributes.First(item => string.Equals(
            item,
            match.Groups[1].Value,
            StringComparison.OrdinalIgnoreCase));
        value = Regex.Unescape(match.Groups[2].Value);
        return true;
    }

    private static bool TryApplyUserPatch(
        ScimUserData current,
        IReadOnlyList<ScimPatchOperation> operations,
        out ScimUserData result)
    {
        result = current;
        foreach (var operation in operations)
        {
            var op = operation.Op.ToUpperInvariant();
            var path = operation.Path?.ToLowerInvariant();
            if (op is not ("ADD" or "REPLACE" or "REMOVE")) return false;
            if (path == "active" && TryReadBoolean(operation.Value, out var active))
            {
                result = result with { Active = op == "REMOVE" || active };
            }
            else if (path == "username" && TryReadString(operation.Value, out var userName))
            {
                result = result with { UserName = userName };
            }
            else if (path == "displayname")
            {
                result = result with { DisplayName = op == "REMOVE" ? null : ReadOptionalString(operation.Value) };
            }
            else if (path is not null && path.StartsWith("emails", StringComparison.Ordinal))
            {
                result = result with { Email = op == "REMOVE" ? null : ReadEmail(operation.Value) };
            }
            else if (path is null && operation.Value.ValueKind == JsonValueKind.Object)
            {
                var request = operation.Value.Deserialize<ScimUserRequest>();
                if (request is null) return false;
                result = request.ToData() with { ExternalId = result.ExternalId };
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryApplyGroupPatch(
        ScimGroupData current,
        IReadOnlyList<ScimPatchOperation> operations,
        out ScimGroupData result)
    {
        result = current;
        foreach (var operation in operations)
        {
            var op = operation.Op.ToUpperInvariant();
            var path = operation.Path?.ToLowerInvariant();
            if (op is not ("ADD" or "REPLACE" or "REMOVE")) return false;
            if (path == "displayname" && TryReadString(operation.Value, out var displayName))
            {
                result = result with { DisplayName = displayName };
            }
            else if (path == "members" && TryReadMemberIds(operation.Value, out var memberIds))
            {
                result = result with
                {
                    MemberIds = op switch
                    {
                        "ADD" => result.MemberIds.Concat(memberIds).Distinct().ToArray(),
                        "REMOVE" => result.MemberIds.Except(memberIds).ToArray(),
                        _ => memberIds
                    }
                };
            }
            else if (op == "REMOVE" && TryParseMemberPath(operation.Path, out var memberId))
            {
                result = result with { MemberIds = result.MemberIds.Where(id => id != memberId).ToArray() };
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadBoolean(JsonElement value, out bool result)
    {
        result = false;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result = value.GetBoolean();
            return true;
        }

        return value.ValueKind == JsonValueKind.String
            && bool.TryParse(value.GetString(), out result);
    }

    private static bool TryReadString(JsonElement value, out string result)
    {
        result = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
        return !string.IsNullOrWhiteSpace(result);
    }

    private static string? ReadOptionalString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ReadEmail(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Select(item => item.TryGetProperty("value", out var email) ? email.GetString() : null)
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
        }

        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("value", out var property)
                ? property.GetString()
                : null;
    }

    private static bool TryReadMemberIds(JsonElement value, out IReadOnlyList<long> memberIds)
    {
        var values = value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [value];
        var result = new List<long>(values.Length);
        foreach (var item in values)
        {
            string? text;
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("value", out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                text = property.GetString();
            }
            else if (item.ValueKind == JsonValueKind.String)
            {
                text = item.GetString();
            }
            else
            {
                memberIds = [];
                return false;
            }
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                memberIds = [];
                return false;
            }

            result.Add(id);
        }

        memberIds = result;
        return true;
    }

    private static bool TryParseMemberPath(string? path, out long memberId)
    {
        memberId = 0;
        var match = MemberPathRegex().Match(path ?? string.Empty);
        return match.Success
            && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out memberId);
    }

    [GeneratedRegex("^\\s*(externalId|userName|displayName)\\s+eq\\s+\"([^\"]*)\"\\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex EqualityFilterRegex();

    [GeneratedRegex("^members\\[value\\s+eq\\s+\"([0-9]+)\"\\]$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex MemberPathRegex();
}

public sealed class ScimUserRequest
{
    public IReadOnlyList<string> Schemas { get; set; } = [];
    public string ExternalId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool Active { get; set; } = true;
    public IReadOnlyList<ScimEmail> Emails { get; set; } = [];

    public ScimUserData ToData() => new(
        ExternalId,
        UserName,
        Emails.FirstOrDefault(email => email.Primary)?.Value ?? Emails.FirstOrDefault()?.Value,
        DisplayName,
        Active);
}

public sealed class ScimEmail
{
    public string Value { get; set; } = string.Empty;
    public bool Primary { get; set; }
}

public sealed class ScimGroupRequest
{
    public IReadOnlyList<string> Schemas { get; set; } = [];
    public string ExternalId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public IReadOnlyList<ScimMember> Members { get; set; } = [];

    public bool TryToData(out ScimGroupData data)
    {
        var ids = new List<long>(Members.Count);
        foreach (var member in Members)
        {
            if (!long.TryParse(member.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                data = new ScimGroupData(string.Empty, string.Empty, []);
                return false;
            }

            ids.Add(id);
        }

        data = new ScimGroupData(ExternalId, DisplayName, ids);
        return true;
    }
}

public sealed class ScimMember
{
    public string Value { get; set; } = string.Empty;
}

public sealed class ScimPatchRequest
{
    public IReadOnlyList<string> Schemas { get; set; } = [];
    public IReadOnlyList<ScimPatchOperation> Operations { get; set; } = [];
}

public sealed class ScimPatchOperation
{
    public string Op { get; set; } = string.Empty;
    public string? Path { get; set; }
    public JsonElement Value { get; set; }
}
