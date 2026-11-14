using System.Net.Http.Json;
using System.Text.Json;

namespace Contratos.Tests.Infrastructure;

/// <summary>HttpClient que busca o token antiforgery antes de cada escrita, como o api.js.</summary>
public sealed class ApiClient(HttpClient http)
{
    public HttpClient Http => http;

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body ?? new { });
    public Task<HttpResponseMessage> PutAsync(string path, object? body = null, string? ifMatch = null) =>
        SendAsync(HttpMethod.Put, path, body ?? new { }, ifMatch);
    public Task<HttpResponseMessage> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? ifMatch = null)
    {
        var csrf = await http.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var req = new HttpRequestMessage(method, path);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        req.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        if (ifMatch is not null)
            req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await http.SendAsync(req);
    }

    public Task<HttpResponseMessage> GetAsync(string path) => http.GetAsync(path);
}

public static class HttpResponseExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage res) =>
        await res.Content.ReadFromJsonAsync<JsonElement>();
}
