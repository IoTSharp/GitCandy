using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GitCandy.Models;

public sealed class GitLfsBatchRequest
{
    [Required]
    [JsonPropertyName("operation")]
    public string Operation { get; init; } = string.Empty;

    [JsonPropertyName("transfers")]
    public IReadOnlyList<string> Transfers { get; init; } = [];

    [Required]
    [JsonPropertyName("objects")]
    public IReadOnlyList<GitLfsObjectRequest> Objects { get; init; } = [];

    [JsonPropertyName("hash_algo")]
    public string? HashAlgorithm { get; init; }
}

public sealed class GitLfsObjectRequest
{
    [Required]
    [JsonPropertyName("oid")]
    public string Oid { get; init; } = string.Empty;

    [Range(0, long.MaxValue)]
    [JsonPropertyName("size")]
    public long Size { get; init; }
}

public sealed record GitLfsBatchResponse(
    [property: JsonPropertyName("transfer")] string Transfer,
    [property: JsonPropertyName("objects")] IReadOnlyList<GitLfsObjectResponse> Objects,
    [property: JsonPropertyName("hash_algo")] string HashAlgorithm = "sha256");

public sealed record GitLfsObjectResponse(
    [property: JsonPropertyName("oid")] string Oid,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("actions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, GitLfsAction>? Actions = null,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GitLfsError? Error = null);

public sealed record GitLfsAction(
    [property: JsonPropertyName("href")] string Href,
    [property: JsonPropertyName("header")] IReadOnlyDictionary<string, string> Header,
    [property: JsonPropertyName("expires_in")] int ExpiresIn = 1800);

public sealed record GitLfsError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message);

public sealed class GitLfsVerifyRequest
{
    [Required]
    [JsonPropertyName("oid")]
    public string Oid { get; init; } = string.Empty;

    [Range(0, long.MaxValue)]
    [JsonPropertyName("size")]
    public long Size { get; init; }
}
