using System.ClientModel;
using System.ClientModel.Primitives;
using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Configuration;
using ExpertToJob.Agents.Usage;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Content filtering, normalized into one typed failure in the shared decorator stack (EXP-20,
/// <c>manuals/adr-chat-provider-seam.md</c> §2 decision 8).
///
/// <para>A provider can refuse a call in two places, and they arrive on the wire as two different
/// shapes: a filtered <b>prompt</b> is an HTTP 400 whose body carries <c>content_filter</c>, and a
/// filtered <b>completion</b> is a perfectly ordinary 200 whose <c>finish_reason</c> is
/// <c>content_filter</c> and whose content may be empty. Both shapes are recorded by the live
/// dialect probe (<c>AzureFoundryDialectProbeTests</c>) rather than hunted for, and Gemini's own
/// safety block arrives as the second of them — <c>content_filter</c> is already a known finish
/// reason in <see cref="GeminiCompatHandler"/> and passes through that shim untouched.</para>
///
/// <para>Orchestration degrades a failed stage rather than failing the call, and it can only keep
/// doing that while it knows nothing about providers. So the two shapes become one exception
/// <em>below</em> it. Every test here drives a fake inner client: nothing in this file reaches a
/// model, and no prompt here is written to provoke a real filter.</para>
/// </summary>
public class ContentFilterNormalizationTests
{
    private const string FilteredPromptBody =
        """{"error":{"code":"content_filter","message":"The response was filtered due to the prompt triggering the content management policy."}}""";

    private static readonly ChatMessage[] Anything = [new(ChatRole.User, "a perfectly ordinary question")];

    /// <summary>The 400 shape: the request never reached the model.</summary>
    private static CountingChat FilteredPrompt() => new(
        () => throw new ClientResultException(new FaultResponse(400, FilteredPromptBody)));

    /// <summary>The 200 shape: the model answered and the answer was withheld. It still cost its
    /// input tokens, which is why the response travels on the exception.</summary>
    private static CountingChat FilteredCompletion() => new(
        () => new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))
        {
            FinishReason = ChatFinishReason.ContentFilter,
            ModelId = "gpt-4.1-mini-2025-04-14",
            Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 0, TotalTokenCount = 120 },
        });

    [Fact]
    public async Task FilteredPrompt_Becomes_ChatContentFilteredException()
    {
        var client = new ContentFilterChatClient(FilteredPrompt(), ChatProvider.AzureFoundry);

        var thrown = await client.Invoking(c => c.GetResponseAsync(Anything))
            .Should().ThrowAsync<ChatContentFilteredException>();

        thrown.Which.Side.Should().Be(ContentFilterSide.Prompt,
            "an HTTP 400 carrying content_filter means the prompt never reached the model");
        thrown.Which.Provider.Should().Be(ChatProvider.AzureFoundry,
            "the run's provider is on the exception so a diagnosis does not have to re-read config");
        thrown.Which.Response.Should().BeNull("there is no response to carry when the request was refused");
        thrown.Which.InnerException.Should().BeOfType<ClientResultException>(
            "the wire fault is kept, not swallowed — the body is the evidence");
    }

    [Fact]
    public async Task FilteredCompletion_Becomes_ChatContentFilteredException()
    {
        var client = new ContentFilterChatClient(FilteredCompletion(), ChatProvider.AzureFoundry);

        var thrown = await client.Invoking(c => c.GetResponseAsync(Anything))
            .Should().ThrowAsync<ChatContentFilteredException>();

        thrown.Which.Side.Should().Be(ContentFilterSide.Completion,
            "a 200 whose finish reason is content_filter means the ANSWER was withheld");
        thrown.Which.Response!.Usage!.InputTokenCount.Should().Be(120,
            "the withheld response travels with the failure because those tokens were spent");
        thrown.Which.Response.ModelId.Should().Be("gpt-4.1-mini-2025-04-14");
    }

    /// <summary>
    /// The property the whole decorator exists for: orchestration catches <b>one</b> type and stays
    /// provider-blind. Asserted across both providers and both shapes together, because "the same
    /// type" is a claim about the cross-product, not about any one cell of it.
    /// </summary>
    [Theory]
    [InlineData(ChatProvider.Gemini)]
    [InlineData(ChatProvider.AzureFoundry)]
    public async Task BothProviders_ProduceTheSameExceptionType(ChatProvider provider)
    {
        foreach (var (shape, inner) in new (string, CountingChat)[]
                 {
                     ("a filtered prompt", FilteredPrompt()),
                     ("a filtered completion", FilteredCompletion()),
                 })
        {
            var client = new ContentFilterChatClient(inner, provider);

            var thrown = await client.Invoking(c => c.GetResponseAsync(Anything))
                .Should().ThrowAsync<ChatContentFilteredException>(
                    $"{shape} on {provider} is the same failure to every caller above the seam");

            thrown.Which.Provider.Should().Be(provider,
                "which side and which provider are DATA on one type, never two types to catch");
        }
    }

    /// <summary>
    /// The decorator ordering, pinned: content filtering is normalized <b>inside</b> metering, so a
    /// filtered call still lands in the run's ledger. A filtered run costs tokens, and dropping
    /// them loses cost data exactly where a provider migration most needs it. Were the order
    /// reversed the exception would escape past <see cref="MeteringChatClient"/>, the scope would
    /// be empty, and the row below would record a run that apparently never called a model.
    /// </summary>
    [Fact]
    public async Task AFilteredCallStillWritesItsUsageRow()
    {
        // The stack the seam composes, minus the OpenTelemetry layer between them (it neither
        // reads nor rewrites the failure): metering outside, normalization in.
        var client = new MeteringChatClient(
            new ContentFilterChatClient(FilteredCompletion(), ChatProvider.AzureFoundry));

        MeteringSnapshot run;
        using (var metering = MeteringScope.Begin())
        {
            await client.Invoking(c => c.GetResponseAsync(Anything))
                .Should().ThrowAsync<ChatContentFilteredException>();
            run = metering.Snapshot();
        }

        run.Iterations.Should().Be(1, "the filtered call was a model call and the ledger has to see it");
        run.ModelId.Should().Be("gpt-4.1-mini-2025-04-14", "the REAL model that filtered, not config");

        // And the row that capture exists to write, through the real meter.
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"content-filter-{Guid.NewGuid()}").Options);
        var userId = Guid.NewGuid();

        await new UsageMeter(db, ChatProvider.AzureFoundry, TimeProvider.System, NullLogger<UsageMeter>.Instance)
            .RecordAsync(userId, "staffing", new AgentReply("", 120, 0, 120, run.ModelId, run.LatencyMs, run.Iterations), "narrative");

        var row = await db.AgentUsages.SingleAsync();
        row.InputTokens.Should().Be(120, "the tokens the filtered call spent");
        row.Model.Should().Be("gpt-4.1-mini-2025-04-14");
        row.Provider.Should().Be("AzureFoundry", "cost attribution needs to know who refused (EXP-19)");
    }

    /// <summary>
    /// No retry, on either shape. A prompt filtered once is filtered twice: retrying spends the
    /// budget again for the same refusal. The decorator adds none of its own, and the failure it
    /// raises is deliberately not 429-shaped, so the staffing rate-limit retry does not adopt it
    /// either.
    /// </summary>
    [Fact]
    public async Task NothingIsRetried()
    {
        foreach (var inner in new[] { FilteredPrompt(), FilteredCompletion() })
        {
            var client = new ContentFilterChatClient(inner, ChatProvider.AzureFoundry);

            var thrown = await client.Invoking(c => c.GetResponseAsync(Anything))
                .Should().ThrowAsync<ChatContentFilteredException>();

            inner.Calls.Should().Be(1, "the filter decision is final; a second call buys the same answer");
            Staffing.StaffingRetryPolicy.IsRateLimit(thrown.Which).Should().BeFalse(
                "a content filter is not a rate limit, and the staffing retry must not treat it as one");
        }
    }

    /// <summary>
    /// The decorator's other half: it must not adopt faults that merely share a status code. A 400
    /// is the everyday shape of a malformed request, and turning one into a content-filter failure
    /// would hide a bug in this repo's own prompt assembly behind a provider's refusal.
    /// </summary>
    [Fact]
    public async Task A_400_that_is_not_a_filter_passes_through_untouched()
    {
        var client = new ContentFilterChatClient(
            new CountingChat(() => throw new ClientResultException(
                new FaultResponse(400, """{"error":{"code":"invalid_request_error","message":"bad tool schema"}}"""))),
            ChatProvider.AzureFoundry);

        await client.Invoking(c => c.GetResponseAsync(Anything))
            .Should().ThrowAsync<ClientResultException>();
    }

    /// <summary>An ordinary answer is handed back untouched — the decorator is a filter translator,
    /// not a gate.</summary>
    [Fact]
    public async Task An_unfiltered_response_is_passed_through()
    {
        var inner = new CountingChat(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            FinishReason = ChatFinishReason.Stop,
        });

        var response = await new ContentFilterChatClient(inner, ChatProvider.Gemini).GetResponseAsync(Anything);

        response.Text.Should().Be("ok");
        inner.Calls.Should().Be(1);
    }

    /// <summary>A fake inner client that answers (or throws) on script and counts its calls.</summary>
    private sealed class CountingChat(Func<ChatResponse> respond) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(respond());
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("These tests use the non-streaming path.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    /// <summary>Just enough <see cref="PipelineResponse"/> to build the SDK's own fault type without
    /// a live call — the 400 shape is body-carried, so the body is what matters here.</summary>
    private sealed class FaultResponse(int status, string body) : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => "";
        public override BinaryData Content => BinaryData.FromString(body);
        public override Stream? ContentStream { get => null; set => throw new NotSupportedException(); }
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override BinaryData BufferContent(CancellationToken ct = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(Content);
        public override void Dispose() { }
    }
}
