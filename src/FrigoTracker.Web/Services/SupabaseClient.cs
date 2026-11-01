using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace FrigoTracker.Web.Services;

/// <summary>Supabase project settings, read from wwwroot/appsettings.json.</summary>
public sealed class SupabaseOptions
{
    public string Url { get; set; } = "";

    /// <summary>The public "anon" / "publishable" key. Never put the service_role / secret key here.</summary>
    public string AnonKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(AnonKey);
}

/// <summary>Signed-in session as returned by Supabase Auth.</summary>
public sealed class SupabaseSession
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
    [JsonPropertyName("user")] public SupabaseUser User { get; set; } = new();

    public bool IsExpiringSoon =>
        DateTimeOffset.FromUnixTimeSeconds(ExpiresAt) < DateTimeOffset.UtcNow.AddMinutes(1);
}

public sealed class SupabaseUser
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("email")] public string? Email { get; set; }
}

/// <summary>Raised for any Supabase failure, with a message that can be shown to the user.</summary>
public sealed class SupabaseException(string message) : Exception(message);

/// <summary>
/// Minimal Supabase client over the REST APIs (Auth + PostgREST), so no extra NuGet package is needed.
/// Accounts are created in the Supabase dashboard (sign-ups disabled) and sign in with email + password.
/// </summary>
public sealed class SupabaseClient(SupabaseOptions options, IJSRuntime js)
{
    private const string SessionKey = "frigo-tracker:session";

    private readonly HttpClient _http = new();
    private SupabaseSession? _session;
    private bool _sessionLoaded;

    public bool IsConfigured => options.IsConfigured;

    public string? Email => _session?.User.Email;

    public async Task<bool> IsSignedInAsync()
    {
        await EnsureSessionLoadedAsync();
        return _session is not null;
    }

    public async Task SignInWithPasswordAsync(string email, string password)
    {
        var response = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=password",
            new { email = email.Trim(), password }, authorized: false);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // Supabase answers 400 "Invalid login credentials" for a wrong email or password.
            throw new SupabaseException("Courriel ou mot de passe invalide.");
        }
        await EnsureSuccessAsync(response, "Connexion impossible");
        await StoreSessionAsync(await ReadSessionAsync(response));
    }

    public async Task SignOutAsync()
    {
        _session = null;
        await js.InvokeVoidAsync("localStorage.removeItem", SessionKey);
    }

    // ---------- Data API ----------

    /// <summary>GET /rest/v1/{table}?{query}; RLS limits the rows to the user's household.</summary>
    public async Task<JsonArray> SelectAsync(string table, string query)
    {
        var response = await SendAsync(HttpMethod.Get, $"rest/v1/{table}?{query}", null, authorized: true);
        await EnsureSuccessAsync(response, "Lecture du nuage impossible");
        return await response.Content.ReadFromJsonAsync<JsonArray>() ?? [];
    }

    /// <summary>Inserts or replaces one row, matched on its "id" column.</summary>
    public async Task UpsertAsync(string table, JsonObject row)
    {
        var response = await SendAsync(HttpMethod.Post, $"rest/v1/{table}?on_conflict=id", new JsonArray(row),
            authorized: true, prefer: "resolution=merge-duplicates,return=minimal");
        await EnsureSuccessAsync(response, "Envoi vers le nuage impossible");
    }

    /// <summary>Deletes one row by id. Deleting a row that no longer exists is not an error.</summary>
    public async Task DeleteAsync(string table, Guid id)
    {
        var response = await SendAsync(HttpMethod.Delete, $"rest/v1/{table}?id=eq.{id}", null, authorized: true,
            prefer: "return=minimal");
        await EnsureSuccessAsync(response, "Suppression dans le nuage impossible");
    }

    /// <summary>Calls a Postgres function (POST /rest/v1/rpc/{name}) and returns its JSON result.</summary>
    public async Task<JsonNode?> RpcAsync(string function, object args)
    {
        var response = await SendAsync(HttpMethod.Post, $"rest/v1/rpc/{function}", args, authorized: true);
        await EnsureSuccessAsync(response, "Opération impossible");
        return await response.Content.ReadFromJsonAsync<JsonNode>();
    }

    // ---------- Plumbing ----------

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, bool authorized, string? prefer = null)
    {
        if (!IsConfigured)
        {
            throw new SupabaseException("Supabase n'est pas configuré (appsettings.json).");
        }
        if (authorized)
        {
            await EnsureFreshSessionAsync();
        }

        var request = new HttpRequestMessage(method, $"{options.Url.TrimEnd('/')}/{path}");
        request.Headers.Add("apikey", options.AnonKey);
        if (authorized && _session is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
        }
        if (prefer is not null)
        {
            request.Headers.Add("Prefer", prefer);
        }
        if (body is not null)
        {
            // Explicit length (no chunked encoding) for maximum proxy compatibility.
            string json = body is JsonNode node ? node.ToJsonString() : JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        try
        {
            return await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new SupabaseException("Pas de connexion Internet.");
        }
    }

    private async Task EnsureSessionLoadedAsync()
    {
        if (_sessionLoaded)
        {
            return;
        }
        _sessionLoaded = true;
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", SessionKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                _session = JsonSerializer.Deserialize<SupabaseSession>(json);
            }
        }
        catch (JsonException)
        {
            _session = null;
        }
    }

    private async Task EnsureFreshSessionAsync()
    {
        await EnsureSessionLoadedAsync();
        if (_session is null)
        {
            throw new SupabaseException("Non connecté.");
        }
        if (!_session.IsExpiringSoon)
        {
            return;
        }

        var response = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token",
            new { refresh_token = _session.RefreshToken }, authorized: false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            // Refresh token revoked or expired: the user has to sign in again.
            await SignOutAsync();
            throw new SupabaseException("Session expirée, reconnecte-toi.");
        }
        await EnsureSuccessAsync(response, "Renouvellement de la session impossible");
        await StoreSessionAsync(await ReadSessionAsync(response));
    }

    private static async Task<SupabaseSession> ReadSessionAsync(HttpResponseMessage response)
    {
        var session = await response.Content.ReadFromJsonAsync<SupabaseSession>()
                      ?? throw new SupabaseException("Réponse de connexion vide.");
        if (session.ExpiresAt == 0)
        {
            // Some responses only carry expires_in; assume the default one-hour lifetime.
            session.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        }
        return session;
    }

    private async Task StoreSessionAsync(SupabaseSession session)
    {
        _session = session;
        _sessionLoaded = true;
        await js.InvokeVoidAsync("localStorage.setItem", SessionKey, JsonSerializer.Serialize(session));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string context)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        string detail = await response.Content.ReadAsStringAsync();
        throw new SupabaseException($"{context} ({(int)response.StatusCode}). {Shorten(ExtractMessage(detail))}".Trim());
    }

    // Supabase errors come as {"msg": ...}, {"message": ...} or {"error_description": ...}.
    private static string ExtractMessage(string body)
    {
        try
        {
            var node = JsonNode.Parse(body) as JsonObject;
            return node?["msg"]?.ToString() ?? node?["message"]?.ToString()
                   ?? node?["error_description"]?.ToString() ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static string Shorten(string text) => text.Length > 160 ? text[..160] + "…" : text;
}
