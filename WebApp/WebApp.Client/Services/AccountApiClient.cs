using System.Net;
using System.Net.Http.Json;
using WebApp.Client.Models;

namespace WebApp.Client.Services;

/// <summary>Typed access to the account API. Antiforgery tokens are added by <see cref="AntiforgeryDelegatingHandler"/>.</summary>
public sealed class AccountApiClient(HttpClient http)
{
    public async Task<IReadOnlyList<AccountUserDto>> GetUsersAsync() =>
        await http.GetFromJsonAsync<List<AccountUserDto>>("api/account/users") ?? [];

    /// <summary>The signed-in user's display name once known; shown by the shell user menu.</summary>
    public string? DisplayName { get; private set; }
    public event Action? DisplayNameChanged;

    public async Task<AccountMeDto> GetMeAsync()
    {
        var me = await http.GetFromJsonAsync<AccountMeDto>("api/account/me")
            ?? throw new InvalidOperationException("Unable to load your account.");
        SetDisplayName(me.DisplayName);
        return me;
    }

    public void SetDisplayName(string displayName)
    {
        if (DisplayName == displayName) return;
        DisplayName = displayName;
        DisplayNameChanged?.Invoke();
    }

    public async Task PostAsync<T>(string path, T payload)
    {
        using var response = await http.PostAsJsonAsync(path, payload);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest payload)
    {
        using var response = await http.PostAsJsonAsync(path, payload);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }

    /// <summary>Creates a member and returns the server's validation messages instead of throwing on 400/409.</summary>
    public async Task<(AccountUserDto? User, IReadOnlyList<string> Errors)> CreateUserAsync(CreateAccountDto payload)
    {
        using var response = await http.PostAsJsonAsync("api/account/users", payload);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<AccountUserDto>(), []);
        return (null, await ReadErrorsAsync(response, "The member could not be created."));
    }

    /// <summary>Runs an Admin lifecycle action; the server's validation messages are returned instead of thrown.</summary>
    public async Task<IReadOnlyList<string>> ManageUserAsync(string userName, string action)
    {
        var path = $"api/account/users/{Uri.EscapeDataString(userName)}";
        using var response = action == "delete"
            ? await http.DeleteAsync(path)
            : await http.PostAsync($"{path}/{action}", content: null);
        return response.IsSuccessStatusCode ? [] : await ReadErrorsAsync(response, "The action could not be completed.");
    }

    /// <summary>Resets a member's password and returns the one-time temporary password.</summary>
    public async Task<(string? TemporaryPassword, IReadOnlyList<string> Errors)> ResetPasswordAsync(string userName)
    {
        using var response = await http.PostAsync($"api/account/users/{Uri.EscapeDataString(userName)}/reset-password", content: null);
        if (response.IsSuccessStatusCode)
            return ((await response.Content.ReadFromJsonAsync<TemporaryPasswordDto>())?.TemporaryPassword, []);
        return (null, await ReadErrorsAsync(response, "The password could not be reset."));
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, string fallback)
    {
        if (response.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.NotFound))
            throw new HttpRequestException($"Unexpected status {(int)response.StatusCode}.");
        try
        {
            var errors = await response.Content.ReadFromJsonAsync<string[]>() ?? [];
            return errors.Length > 0 ? errors : [fallback];
        }
        catch (System.Text.Json.JsonException) { return [fallback]; }
    }
}
