using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.JSInterop;

namespace Dashboard.Client.Services;

public class AuthStateService(HttpClient http, IJSRuntime js)
{
    private const string TokenKey = "auth_token";

    public bool IsAuthenticated { get; private set; }
    public string Username { get; private set; } = "";
    public bool Initialized { get; private set; }

    public event Action? StateChanged;

    public async Task InitializeAsync()
    {
        if (Initialized) return;
        try
        {
            var token = await js.InvokeAsync<string?>("localStorage.getItem", TokenKey);
            if (!string.IsNullOrEmpty(token))
            {
                SetAuthHeader(token);
                var resp = await http.GetAsync("api/auth/me");
                if (resp.IsSuccessStatusCode)
                {
                    var data = await resp.Content.ReadFromJsonAsync<MeResponse>();
                    IsAuthenticated = true;
                    Username = data?.Username ?? "";
                }
                else
                {
                    await ClearTokenAsync();
                }
            }
        }
        catch { /* JS not available during pre-render */ }
        finally
        {
            Initialized = true;
            StateChanged?.Invoke();
        }
    }

    public async Task<(bool ok, string error)> LoginAsync(string username, string password)
    {
        var resp = await http.PostAsJsonAsync("api/auth/login", new { username, password });
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
            return (false, err?.Error ?? "Помилка входу");
        }
        var data = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        if (data?.Token is null) return (false, "Помилка входу");

        await js.InvokeVoidAsync("localStorage.setItem", TokenKey, data.Token);
        SetAuthHeader(data.Token);
        IsAuthenticated = true;
        Username = data.Username ?? username.Trim();
        StateChanged?.Invoke();
        return (true, "");
    }

    public async Task<(bool ok, string error)> RegisterAsync(string username, string password)
    {
        var resp = await http.PostAsJsonAsync("api/auth/register", new { username, password });
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
            return (false, err?.Error ?? "Помилка реєстрації");
        }
        return (true, "");
    }

    public async Task LogoutAsync()
    {
        try { await http.PostAsync("api/auth/logout", null); } catch { }
        await ClearTokenAsync();
        IsAuthenticated = false;
        Username = "";
        StateChanged?.Invoke();
    }

    private void SetAuthHeader(string token) =>
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task ClearTokenAsync()
    {
        http.DefaultRequestHeaders.Authorization = null;
        try { await js.InvokeVoidAsync("localStorage.removeItem", TokenKey); } catch { }
    }

    private record MeResponse(string Username);
    private record LoginResponse(string Token, string Username);
    private record ErrorResponse(string Error);
}
