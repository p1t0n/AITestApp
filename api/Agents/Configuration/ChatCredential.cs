using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Configuration;

/// <summary>
/// The active chat provider's credential, as one resolved value (EXP-93): the provider's own
/// environment variable first, its <c>ApiKey</c> config path second, and nothing else. The
/// Production guard (<see cref="ChatProviderStartupGuard"/>) calls this rather than repeating the
/// two lookups (EXP-97), so exactly one rule decides whether a host has a key — and startup and
/// request time can no longer answer that differently.
///
/// <para><b>Why a type and not a string.</b> A missing credential is a normal development
/// condition, so the seam has to be able to ask whether one is present without having already
/// failed. Before this, the answer was discovered by <see cref="System.ClientModel.ApiKeyCredential"/>
/// throwing <see cref="ArgumentException"/> deep inside a singleton factory, which surfaced as a
/// 500 with a stack trace on the first agent request. <see cref="IsMissing"/> is that question
/// asked before the throw; <see cref="Key"/> is the answer only a configured host may have.</para>
///
/// <para>Registered as a singleton by the construction branch that resolved it, which is also what
/// lets a test say "no credential" honestly: process environment variables are global, so a test
/// that unset <c>GEMINI_API_KEY</c> would be mutating state another test running beside it reads
/// (the same reason <see cref="ChatProviderStartupGuard"/> takes its reader as a parameter).
/// Substituting this registration says the same thing and touches nothing outside the host.</para>
/// </summary>
public sealed class ChatCredential(ChatProvider provider, string? key)
{
    /// <summary>The provider this credential is for. A credential is never shared across providers
    /// — that is the whole point of <see cref="ChatProviderOptions.ApiKeyVariableFor"/>.</summary>
    public ChatProvider Provider { get; } = provider;

    /// <summary>True when neither lookup produced a key, so no call can reach the model.</summary>
    public bool IsMissing => string.IsNullOrWhiteSpace(key);

    /// <summary>The key, for a host that has one. Throws for a host that does not, rather than
    /// handing an empty string to the SDK, which throws a message naming no setting.</summary>
    public string Key => IsMissing ? throw new ChatCredentialMissingException(Provider) : key!;

    /// <summary>The credential as the two places it is read from, resolved through an injectable
    /// environment reader so a test can describe an unset variable without unsetting one.
    ///
    /// <para><b>Whitespace counts as unset</b> (EXP-97), by the same
    /// <see cref="string.IsNullOrWhiteSpace"/> test <see cref="IsMissing"/> applies — a variable
    /// exported empty is an operator who has not set it, not one who set it to a space. The
    /// stricter <c>{ Length: > 0 }</c> this used to match on let such a variable beat a perfectly
    /// good config key and resolve to missing, while the Production guard tested the same variable
    /// with <see cref="string.IsNullOrWhiteSpace"/> and called the setup configured: the host
    /// booted and then returned the EXP-93 503 on every agent call. One test, applied in one
    /// place, is what keeps the two from disagreeing.</para></summary>
    public static ChatCredential Resolve(
        ChatProvider provider,
        ChatProviderOptions cfg,
        Func<string, string?>? readEnvironmentVariable = null)
    {
        var read = readEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var fromEnvironment = read(ChatProviderOptions.ApiKeyVariableFor(provider));
        return new ChatCredential(
            provider,
            string.IsNullOrWhiteSpace(fromEnvironment) ? cfg.ApiKey : fromEnvironment);
    }
}

/// <summary>
/// The typed failure a chat call makes when the host has no credential for its active provider
/// (EXP-93). It carries the two settings that would fix it, so the endpoint shells can name them
/// in a 503 instead of returning a 500 with an SDK stack trace that names neither.
///
/// <para>This is a <em>configuration</em> fault, not an upstream one, which is why it is 503 and
/// not the 502 an unreachable MCP server or a faulting model endpoint gets: nothing upstream was
/// asked anything, and retrying changes nothing until someone sets a key.</para>
/// </summary>
public sealed class ChatCredentialMissingException(ChatProvider provider)
    : InvalidOperationException(DetailFor(provider))
{
    /// <summary>The provider whose credential is missing — the one <c>Ai:Chat:Provider</c> names.</summary>
    public ChatProvider Provider { get; } = provider;

    /// <summary>The environment variable this provider's credential is read from first.</summary>
    public string Variable => ChatProviderOptions.ApiKeyVariableFor(Provider);

    /// <summary>The configuration path it falls back to.</summary>
    public string ConfigPath => $"{ChatProviderOptions.SectionFor(Provider)}:ApiKey";

    /// <summary>The ProblemDetails title: it names the missing setting, both spellings, so the fix
    /// is readable from the response body alone.</summary>
    public string Title =>
        $"No API key for the configured chat provider (set {Variable} or '{ConfigPath}').";

    private static string DetailFor(ChatProvider provider) =>
        $"'{ChatProviderServiceCollectionExtensions.ProviderKey}' is '{provider}', and no credential "
        + $"is configured for it: set the {ChatProviderOptions.ApiKeyVariableFor(provider)} environment "
        + $"variable or the '{ChatProviderOptions.SectionFor(provider)}:ApiKey' configuration key. "
        + "Agents that call a model are unavailable until one of them carries a key. "
        + "See manuals/adr-chat-provider-seam.md.";
}

/// <summary>
/// The chat client a credential-less host gets: it builds, and it throws
/// <see cref="ChatCredentialMissingException"/> the moment anyone asks it for a completion.
///
/// <para><b>Constructing has to succeed</b> — that is the bug this type fixes. Every agent is a
/// container registration that resolves its chat client, so a client that threw while being built
/// threw during endpoint parameter binding, outside any handler's <c>try</c>, which is how a
/// missing key became a 500 with a stack trace rather than a mapped response. Failing at call time
/// instead puts the failure inside the shell that already knows how to turn one into
/// ProblemDetails.</para>
///
/// <para>It is deliberately <b>not</b> a client that returns empty text: an agent that "answers"
/// without a model would have its reply composed, metered and persisted like any other, and the
/// first sign of a misconfigured host would be a roster answer nobody can source.</para>
/// </summary>
internal sealed class UnavailableChatClient(ChatCredential credential, string modelId) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new ChatCredentialMissingException(credential.Provider);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new ChatCredentialMissingException(credential.Provider);

    /// <summary>The metadata still answers honestly: the model this client would have run on is a
    /// deployment decision, and <c>GET /agents/models</c>'s catalog reports it whether or not a key
    /// is set.</summary>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
        {
            return null;
        }

        return serviceType == typeof(ChatClientMetadata)
            ? new ChatClientMetadata(credential.Provider.ToString(), defaultModelId: modelId)
            : serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }
}
