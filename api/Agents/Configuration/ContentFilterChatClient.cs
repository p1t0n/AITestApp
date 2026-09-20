using System.ClientModel;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Configuration;

/// <summary>Which side of a chat call a provider's safety filter stopped.</summary>
public enum ContentFilterSide
{
    /// <summary>The request was refused before the model saw it, so nothing was generated.</summary>
    Prompt,

    /// <summary>The model generated an answer and the provider withheld it. The call still cost its
    /// input tokens, which is why the withheld response travels on the exception.</summary>
    Completion,
}

/// <summary>
/// One typed failure for content filtering, whoever refused and wherever they refused
/// (<c>manuals/adr-chat-provider-seam.md</c> §2 decision 8, EXP-20). Raised by
/// <see cref="ContentFilterChatClient"/>; caught by orchestration, which degrades the stage.
///
/// <para>Which side and which provider are <b>data on one type</b>, never a second type to catch:
/// the moment a caller has to know that Azure refuses differently from Gemini, provider knowledge
/// has arrived in the layer the seam exists to keep free of it.</para>
/// </summary>
public sealed class ChatContentFilteredException : Exception
{
    public ChatContentFilteredException(
        ContentFilterSide side,
        ChatProvider provider,
        ChatResponse? response = null,
        Exception? innerException = null)
        : base(
            $"The chat {(side == ContentFilterSide.Prompt ? "prompt" : "completion")} was blocked by "
            + $"{provider}'s content filter.",
            innerException)
    {
        Side = side;
        Provider = provider;
        Response = response;
    }

    /// <summary>Whether the prompt was refused or the answer was withheld.</summary>
    public ContentFilterSide Side { get; }

    /// <summary>The provider that served — and refused — the run, so a diagnosis does not have to
    /// re-read configuration to learn who said no.</summary>
    public ChatProvider Provider { get; }

    /// <summary>The withheld response on <see cref="ContentFilterSide.Completion"/>, carrying the
    /// usage those tokens were spent on; null on <see cref="ContentFilterSide.Prompt"/>, where
    /// there is no response to carry.</summary>
    public ChatResponse? Response { get; }
}

/// <summary>
/// Normalizes the two shapes content filtering takes on the wire into
/// <see cref="ChatContentFilteredException"/>. Registered on the shared decorator stack for
/// <b>both</b> providers — the point is one shape, not an Azure special case.
///
/// <list type="bullet">
/// <item>A filtered <b>prompt</b> is an HTTP <c>400</c> whose body carries <c>content_filter</c>,
/// surfacing through the SDK as <see cref="ClientResultException"/>.</item>
/// <item>A filtered <b>completion</b> is an ordinary <c>200</c> whose
/// <see cref="ChatResponse.FinishReason"/> is <see cref="ChatFinishReason.ContentFilter"/>, with
/// possibly empty content — a shape that is trivially mistaken for a model that answered with
/// nothing to say.</item>
/// </list>
///
/// <para>Both were recorded by the live dialect probe rather than assumed (ADR §3,
/// <c>tests/Agents.Tests/AzureFoundryDialectProbeTests.cs</c>), and Gemini's safety block arrives
/// as the second of them: <c>content_filter</c> is already a known finish reason in
/// <see cref="GeminiCompatHandler"/> and passes that shim through untouched.</para>
///
/// <para><b>Placement.</b> Inside <see cref="Usage.MeteringChatClient"/>, so a filtered call still
/// lands in the run's ledger — a filtered run costs tokens, and losing them loses cost data exactly
/// where a provider migration most needs it.</para>
///
/// <para><b>No retry, and none invited.</b> This decorator adds none, and the failure it raises is
/// not 429-shaped, so <see cref="Staffing.StaffingRetryPolicy"/> does not adopt it either. A prompt
/// filtered once is filtered twice; a retry spends the budget again for the same refusal.</para>
///
/// <para><b>The streaming path is deliberately left as pass-through.</b> Nothing in this repo
/// streams chat, so the shapes filtering takes there have never been measured — and this repo's
/// rule for this endpoint is that a shim follows evidence (ADR §3). A guess written here would be
/// untested code on a path no caller walks.</para>
/// </summary>
public sealed class ContentFilterChatClient(IChatClient inner, ChatProvider provider)
    : DelegatingChatClient(inner)
{
    /// <summary>The code the 400 body carries.</summary>
    private const string FilterCode = "content_filter";

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (ClientResultException ex) when (IsFilteredPrompt(ex))
        {
            throw new ChatContentFilteredException(
                ContentFilterSide.Prompt, provider, response: null, innerException: ex);
        }

        return response.FinishReason == ChatFinishReason.ContentFilter
            ? throw new ChatContentFilteredException(ContentFilterSide.Completion, provider, response)
            : response;
    }

    /// <summary>
    /// A 400 is the everyday shape of a malformed request, so the code has to be present before
    /// this decorator claims the fault. Adopting every 400 would hide a bug in this repo's own
    /// prompt assembly behind a provider's refusal — and would do it silently, because a degraded
    /// stage reads like a policy decision rather than a defect.
    /// </summary>
    private static bool IsFilteredPrompt(ClientResultException ex) =>
        ex.Status == 400 && (Names(RawBodyOf(ex)) || Names(ex.Message));

    private static bool Names(string? text) =>
        text is not null && text.Contains(FilterCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The response body, when it can be read. The <b>body</b> is where the code lives; the
    /// message is checked as well because the SDK folds a buffered body into it, and the probe's
    /// own recording clause reads it there — but that path has never actually fired (the ADR §3
    /// run did not trip a filter), so it is the fallback and not the test.
    ///
    /// <para>Reading <see cref="System.ClientModel.Primitives.PipelineResponse.Content"/> throws
    /// when the response was never buffered. That must not turn a recognisable fault into an
    /// unrecognisable one — the original exception is the thing worth preserving here — so an
    /// unreadable body is simply "not a filter" and the message gets the next word.</para>
    /// </summary>
    private static string? RawBodyOf(ClientResultException ex)
    {
        try
        {
            return ex.GetRawResponse()?.Content?.ToString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
