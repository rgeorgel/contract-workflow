using System.Net.Http.Headers;
using System.Text;
using Contratos.Application.Storage;

namespace Contratos.Infrastructure.Storage;

public sealed class GotenbergOptions
{
    public const string Section = "Gotenberg";
    public string Url { get; set; } = "http://localhost:3005";
}

/// <summary>HTML → PDF pelo Chromium do Gotenberg. Margens e tamanho vêm do @page do HTML.</summary>
internal sealed class GotenbergPdfRenderer(HttpClient http) : IPdfRenderer
{
    public async Task<byte[]> RenderAsync(string html, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(html));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
        form.Add(file, "files", "index.html");
        form.Add(new StringContent("true"), "preferCssPageSize");
        form.Add(new StringContent("true"), "printBackground");

        using var res = await http.PostAsync("/forms/chromium/convert/html", form, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Gotenberg respondeu {(int)res.StatusCode}: {body[..Math.Min(body.Length, 300)]}");
        }
        return await res.Content.ReadAsByteArrayAsync(ct);
    }
}
