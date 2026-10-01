using System.Net.Http.Json;
using WebApp.Client.Models;

namespace WebApp.Client.Services;

public sealed class AccountApiClient(HttpClient http)
{
    private string? _token;

    public async Task<IReadOnlyList<AccountUserDto>> GetUsersAsync() =>
        await http.GetFromJsonAsync<List<AccountUserDto>>("api/account/users") ?? [];

    public async Task<AccountMeDto> GetMeAsync() =>
        await http.GetFromJsonAsync<AccountMeDto>("api/account/me")
            ?? throw new InvalidOperationException("Unable to load your account.");

    public async Task PostAsync<T>(string path, T payload)
    {
        _token ??= (await http.GetFromJsonAsync<AntiforgeryTokenDto>("api/antiforgery"))?.RequestToken
            ?? throw new InvalidOperationException("Unable to start a protected account request.");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", _token);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest payload)
    {
        _token ??= (await http.GetFromJsonAsync<AntiforgeryTokenDto>("api/antiforgery"))?.RequestToken
            ?? throw new InvalidOperationException("Unable to start a protected account request.");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", _token);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }

    /// <summary>Creates a member and returns the server's validation messages instead of throwing on 400/409.</summary>
    public async Task<(AccountUserDto? User, IReadOnlyList<string> Errors)> CreateUserAsync(CreateAccountDto payload)
    {
        _token ??= (await http.GetFromJsonAsync<AntiforgeryTokenDto>("api/antiforgery"))?.RequestToken
            ?? throw new InvalidOperationException("Unable to start a protected account request.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/account/users") { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", _token);
        using var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<AccountUserDto>(), []);
        if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Conflict)
        {
            var errors = await response.Content.ReadFromJsonAsync<string[]>() ?? [];
            return (null, errors.Length > 0 ? errors : ["The member could not be created."]);
        }
        throw new HttpRequestException($"Unexpected status {(int)response.StatusCode}.");
    }
}
