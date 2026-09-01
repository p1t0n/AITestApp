using System.Net;

namespace ExpertToJob.Agents.Tests.Fakes;

/// <summary>Captures every outgoing token request and returns a canned Keycloak token JSON, so the
/// client-credentials flow can be tested without a live Keycloak.</summary>
internal sealed class CapturingHandler(string accessToken) : HttpMessageHandler
{
    private readonly List<Dictionary<string, string>> _requests = [];

    /// <summary>Posted form fields of every request, in order.</summary>
    public IReadOnlyList<Dictionary<string, string>> Requests => _requests;

    /// <summary>The most recent request's posted form fields (convenience for single-request tests).</summary>
    public Dictionary<string, string> Form => _requests[^1];

    public string? RequestUri { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUri = request.RequestUri?.ToString();
        var form = new Dictionary<string, string>();
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        foreach (var pair in body.Split('&'))
        {
            var kv = pair.Split('=', 2);
            form[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv.Length > 1 ? kv[1] : "");
        }

        _requests.Add(form);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"access_token":"{{accessToken}}","expires_in":300}"""),
        };
    }
}

/// <summary>An <see cref="IHttpClientFactory"/> that hands out clients over a single shared handler —
/// lets a test observe every request the token provider makes.</summary>
internal sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
