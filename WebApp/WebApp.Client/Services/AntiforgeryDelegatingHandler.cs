using System.Net;
using System.Net.Http.Json;
using WebApp.Client.Models;

namespace WebApp.Client.Services;

/// <summary>
/// Adds the antiforgery request token to every unsafe API request. The token comes from the authenticated,
/// no-store <c>GET /api/antiforgery</c> endpoint and is cached until the server rejects it (for example after
/// sign-in/out). A rejected buffered request is retried exactly once with a fresh token; multipart and
/// streamed bodies are never replayed because their content cannot be re-sent safely.
/// </summary>
public sealed class AntiforgeryDelegatingHandler : DelegatingHandler
{
    public const string HeaderName = "X-CSRF-TOKEN";
    public const string FailureHeader = "X-Antiforgery-Failure";
    public const string TokenPath = "/api/antiforgery";

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsUnsafe(request.Method) || request.RequestUri is null || IsTokenRequest(request.RequestUri))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        request.Headers.Remove(HeaderName);
        request.Headers.Add(HeaderName, await GetTokenAsync(request.RequestUri, refresh: false, cancellationToken));
        var response = await base.SendAsync(request, cancellationToken);
        if (!IsAntiforgeryRejection(response) || !CanReplay(request.Content))
        {
            return response;
        }

        response.Dispose();
        request.Headers.Remove(HeaderName);
        request.Headers.Add(HeaderName, await GetTokenAsync(request.RequestUri, refresh: true, cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }

    public static bool IsUnsafe(HttpMethod method) =>
        method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Options && method != HttpMethod.Trace;

    public static bool CanReplay(HttpContent? content) =>
        content is null or StringContent or ByteArrayContent or JsonContent;

    private static bool IsTokenRequest(Uri uri) =>
        string.Equals(uri.AbsolutePath, TokenPath, StringComparison.OrdinalIgnoreCase);

    private static bool IsAntiforgeryRejection(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.BadRequest && response.Headers.Contains(FailureHeader);

    private async Task<string> GetTokenAsync(Uri requestUri, bool refresh, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _token is not null) return _token;
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(requestUri, TokenPath));
            using var tokenResponse = await base.SendAsync(tokenRequest, cancellationToken);
            tokenResponse.EnsureSuccessStatusCode();
            var dto = await tokenResponse.Content.ReadFromJsonAsync<AntiforgeryTokenDto>(cancellationToken);
            return _token = dto?.RequestToken ?? throw new HttpRequestException("The server did not provide a request token.");
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
