using CvManager.Agents.Auth;
using CvManager.Agents.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace CvManager.Agents.Mcp;

/// <summary>
/// Connects to the MCP server over Streamable HTTP (with a bearer token injected per request)
/// and lists its tools once, caching the result for the app lifetime. Because the agent's token
/// carries only <c>mcp:read</c>, the server advertises read tools only — the agent never sees a
/// write or destructive tool.
/// </summary>
public sealed class McpToolSource : IMcpToolSource, IAsyncDisposable
{
    private readonly McpServerOptions _server;
    private readonly IAccessTokenProvider _tokens;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HttpClient? _httpClient;
    private McpClient? _client;
    private IReadOnlyList<AITool>? _tools;

    public McpToolSource(
        IOptions<McpServerOptions> server,
        IAccessTokenProvider tokens,
        ILoggerFactory loggerFactory)
    {
        _server = server.Value;
        _tokens = tokens;
        _loggerFactory = loggerFactory;
    }

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default)
    {
        if (_tools is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_tools is { } stillCached)
            {
                return stillCached;
            }

            var httpClient = new HttpClient(new BearerTokenHandler(_tokens) { InnerHandler = new SocketsHttpHandler() });
            try
            {
                var transport = new HttpClientTransport(
                    new HttpClientTransportOptions
                    {
                        Endpoint = new Uri(_server.BaseUrl),
                        TransportMode = HttpTransportMode.StreamableHttp,
                    },
                    httpClient,
                    _loggerFactory,
                    ownsHttpClient: false);

                _client = await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory, cancellationToken: ct);

                var tools = await _client.ListToolsAsync(cancellationToken: ct);
                _httpClient = httpClient;
                _tools = tools.Cast<AITool>().ToList();
                return _tools;
            }
            catch
            {
                // Failed connect (MCP down, auth rejected): release resources so the next call retries clean.
                if (_client is not null)
                {
                    await _client.DisposeAsync();
                    _client = null;
                }

                httpClient.Dispose();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        _httpClient?.Dispose();
    }
}
