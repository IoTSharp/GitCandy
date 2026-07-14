using System.Text;
using System.Text.Json;

namespace GitCandy.Web.Enterprise;

internal static class EnterpriseProviderHttp
{
    private const int MaxResponseBytes = 1024 * 1024;

    public static async Task<JsonDocument> SendJsonAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                throw new HttpRequestException("The provider response exceeded the configured limit.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                if (output.Length + read > MaxResponseBytes)
                {
                    throw new HttpRequestException("The provider response exceeded the configured limit.");
                }

                output.Write(buffer, 0, read);
            }

            return JsonDocument.Parse(output.ToArray());
        }
    }

    public static HttpRequestMessage JsonPost(Uri uri, object value, string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(bearer))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        }

        return request;
    }

    public static string? GetString(JsonElement element, params string[] path)
    {
        foreach (var segment in path)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(segment, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }

    public static bool IsSuccess(JsonElement root) =>
        !root.TryGetProperty("errcode", out var errorCode)
        || errorCode.ValueKind == JsonValueKind.Number && errorCode.GetInt32() == 0;
}
