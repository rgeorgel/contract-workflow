using System.Collections.Concurrent;
using System.Net;

namespace Contratos.Tests.Infrastructure;

/// <summary>Faz o papel do servidor do cliente: guarda cada webhook recebido e responde o status configurado por URL.</summary>
public sealed class FakeWebhookReceiver : HttpMessageHandler
{
    public sealed record Received(string Url, string Body, IReadOnlyDictionary<string, string> Headers)
    {
        public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : "";
    }

    private readonly ConcurrentQueue<Received> _received = new();
    private readonly ConcurrentDictionary<string, HttpStatusCode> _status = new(StringComparer.Ordinal);

    public void RespondWith(string url, HttpStatusCode status) => _status[url] = status;

    public IReadOnlyList<Received> To(string url) => _received.Where(r => r.Url == url).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        _received.Enqueue(new Received(url, await request.Content!.ReadAsStringAsync(ct), headers));
        var status = _status.GetValueOrDefault(url, HttpStatusCode.OK);
        return new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.OK ? "{\"ok\":true}" : "erro do servidor do cliente") };
    }
}
