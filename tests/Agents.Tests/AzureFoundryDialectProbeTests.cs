using System.ClientModel;
using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using OpenAI;
using Xunit.Abstractions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// EXP-13's <b>dialect probe</b>: is an Azure OpenAI deployment really the same OpenAI dialect the
/// Gemini path speaks, or does it need shims of its own? The research says no quirk analog exists
/// on the Azure side (no <see cref="GeminiThoughtSignaturePolicy"/>, no <see cref="GeminiCompatHandler"/>),
/// and since nothing is *known* to be needed, this probe is the only thing that can falsify that
/// before the ADR commits the seam to it.
///
/// <para>Committed rather than thrown away, for the same reason
/// <see cref="CloudflareWorkersAiGateTests"/> is: a measurement that needs a credential the default
/// run does not have would otherwise be written, run once and lost. It skips without
/// <c>AZURE_FOUNDRY_API_KEY</c>, runs with one command, and doubles as the Azure live smoke test.</para>
///
/// <para><b>Route B</b>, as the seam ticket decided: the plain <see cref="OpenAIClient"/> from the
/// <c>OpenAI</c> package pointed at the resource's <c>openai/v1/</c> endpoint, with the deployment
/// name passed where a model id goes — no <c>Azure.AI.OpenAI</c>, and deliberately <b>no transport
/// handler and no per-call policy</b>. Both Gemini shims are omitted on purpose: attaching them
/// here would measure our shims instead of Azure, and the whole question is whether the bare client
/// suffices.</para>
///
/// <para>Run it:
/// <c>AZURE_FOUNDRY_API_KEY=$(az cognitiveservices account keys list -n experttojob-openai-swc -g rg-experttojob-foundry --query key1 -o tsv) dotnet test tests/Agents.Tests --filter "Category=live"</c>.
/// The endpoint and deployment default to the provisioned resource (EXP-9) and are overridable so a
/// re-run can point at another deployment without a rebuild.</para>
///
/// <para>Background: <c>manuals/wayfinder-map-azure-chat-provider.md</c>.</para>
/// </summary>
[Trait("Category", "live")]
public class AzureFoundryDialectProbeTests(ITestOutputHelper output)
{
    /// <summary>The resource provisioned by EXP-9. The hostname is <c>*.openai.azure.com</c>, not
    /// <c>*.services.ai.azure.com</c> — a bare Azure OpenAI resource, no Foundry project — and the
    /// seam appends <c>openai/v1/</c>, which is the endpoint Route B talks to.</summary>
    private static string Endpoint =>
        Environment.GetEnvironmentVariable("AZURE_FOUNDRY_ENDPOINT") is { Length: > 0 } e
            ? e
            : "https://experttojob-openai-swc.openai.azure.com/openai/v1/";

    /// <summary>The <b>deployment</b> name, which is what Azure wants where OpenAI wants a model id.
    /// Behind it sits <c>gpt-4.1-mini</c> 2025-04-14 on <c>GlobalStandard</c>.</summary>
    private static string Deployment =>
        Environment.GetEnvironmentVariable("AZURE_FOUNDRY_DEPLOYMENT") is { Length: > 0 } d
            ? d
            : "gpt-4-1-mini";

    /// <summary>The model actually behind the deployment. The smoke test in EXP-9 already showed the
    /// response's <c>model</c> field carries this and not the deployment name, which is what lets
    /// <c>MeteringChatClient</c> record real model identity under Azure with no change.</summary>
    private const string ModelBehindTheDeployment = "gpt-4.1-mini";

    // GlobalStandard gpt-4.1-mini, the rates the cost research priced the map against
    // (manuals/wayfinder-map-azure-chat-provider.md §2.1: ~$1.60 per 1,000 calls at 2k in / 500 out).
    private const double UsdPerMillionInput = 0.40;
    private const double UsdPerMillionOutput = 1.60;

    [SkippableFact]
    public async Task Azure_calls_a_tool_and_returns_its_result()
    {
        var chat = BuildAzureChatClient();

        List<ChatMessage> history =
        [
            new(ChatRole.System, "You are a roster assistant. Use the tools you are given."),
            new(ChatRole.User, "How many experts sit in the Berlin office?"),
        ];

        var first = await SendAsync(chat, history, ToolOptions(), "turn 1 (tool call)");

        // The whole point of the first turn: the model must ASK for the tool rather than answer.
        var call = first.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .Should().ContainSingle("a tool-calling round-trip starts with exactly one tool call")
            .Subject;
        call.Name.Should().Be(HeadcountToolName);
        first.FinishReason.Should().Be(
            ChatFinishReason.ToolCalls,
            "an Azure finish_reason must land on a value the OpenAI SDK already knows — a value it " +
            "cannot parse is what GeminiCompatHandler exists to normalize");

        history.AddMessages(first);
        history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, BerlinHeadcount)]));

        var second = await SendAsync(chat, history, ToolOptions(), "turn 2 (after tool result)");

        second.Text.Should().Contain(BerlinHeadcount, "the model must carry the tool's result into its answer");
        second.FinishReason.Should().Be(ChatFinishReason.Stop);
    }

    [SkippableFact]
    public async Task Azure_accepts_a_replayed_tool_call_history_without_a_policy_analog()
    {
        // THE load-bearing measurement. Gemini 3 rejects a replayed assistant tool call unless its
        // thought signature is echoed back, which is the entire reason GeminiThoughtSignaturePolicy
        // exists. Every multi-turn agent in this repo replays exactly this shape, so if Azure needs
        // an analog, the seam grows a per-provider policy and the ADR's "no shims" claim is false.
        var chat = BuildAzureChatClient();

        List<ChatMessage> history =
        [
            new(ChatRole.System, "You are a roster assistant. Use the tools you are given."),
            new(ChatRole.User, "How many experts sit in the Berlin office?"),
        ];

        var first = await SendAsync(chat, history, ToolOptions(), "turn 1 (tool call)");
        var call = first.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().First();
        history.AddMessages(first);
        history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, BerlinHeadcount)]));

        var second = await SendAsync(chat, history, ToolOptions(), "turn 2 (after tool result)");
        history.AddMessages(second);

        // Turn 3 replays the whole thing — assistant tool call, tool result and all — with the tools
        // still declared. This is the request Gemini 400s without the policy.
        history.Add(new ChatMessage(ChatRole.User, "And how many sit in Munich? Use the tool again."));
        var third = await SendAsync(chat, history, ToolOptions(), "turn 3 (replayed history)");

        third.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>()
            .Should().NotBeEmpty(
                "a replayed tool-call history must survive intact — the model asking for the tool a " +
                "second time is the proof the earlier call and result were accepted, not dropped");
    }

    [SkippableFact]
    public async Task Azure_honours_a_strict_json_schema()
    {
        // Structured output is how five of the eight agents parse a reply at all. json_schema with
        // strict:true is the contract; a provider that treats it as a hint breaks them silently.
        var chat = BuildAzureChatClient();

        var schema = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "type": "object",
              "properties": {
                "headline": { "type": "string" },
                "years": { "type": "integer" },
                "band": { "type": "string", "enum": ["junior", "mid", "senior"] }
              },
              "required": ["headline", "years", "band"],
              "additionalProperties": false
            }
            """);

        var response = await SendAsync(
            chat,
            [
                new(ChatRole.System, "You summarize an expert. Answer only with the schema."),
                new(ChatRole.User,
                    "Ada has twelve years of backend engineering on .NET and Kafka, and leads a team of six."),
            ],
            new ChatOptions
            {
                Temperature = 0,
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    schema, "expert_summary", "One-line summary of an expert."),
            },
            "json schema");

        var parsed = JsonSerializer.Deserialize<JsonElement>(response.Text);
        parsed.GetProperty("headline").GetString().Should().NotBeNullOrWhiteSpace();
        parsed.GetProperty("years").GetInt32().Should().Be(12);
        parsed.GetProperty("band").GetString().Should().Be("senior");
        parsed.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["headline", "years", "band"],
            "strict mode means additionalProperties:false is enforced, not suggested");
    }

    private const string HeadcountToolName = "roster_headcount";
    private const string BerlinHeadcount = "47";

    /// <summary>A trivial declared function — the probe is about the wire, not the tool. Nothing
    /// auto-invokes it: <c>FunctionInvokingChatClient</c> is deliberately absent so the tool call
    /// and the replayed history are visible as messages rather than hidden inside a helper.</summary>
    private static ChatOptions ToolOptions() => new()
    {
        Temperature = 0,
        ToolMode = ChatToolMode.Auto,
        Tools =
        [
            AIFunctionFactory.Create(
                (string office) => BerlinHeadcount,
                HeadcountToolName,
                "How many experts sit in a given office."),
        ],
    };

    /// <summary>One call, with the two numbers the ticket asks for (tokens, cost) printed per turn,
    /// and the two failure shapes content filtering can take recorded rather than hunted for: a
    /// filtered prompt is an HTTP 400 carrying <c>content_filter</c>, a filtered completion is a 200
    /// whose finish reason is <c>content_filter</c> with possibly empty content.</summary>
    private async Task<ChatResponse> SendAsync(
        IChatClient chat, IList<ChatMessage> messages, ChatOptions options, string label)
    {
        ChatResponse response;
        try
        {
            response = await chat.GetResponseAsync(messages, options);
        }
        catch (ClientResultException ex) when (ex.Status == 400 && ex.Message.Contains("content_filter"))
        {
            output.WriteLine($"{label}: FILTERED PROMPT — HTTP 400 content_filter: {ex.Message}");
            throw;
        }

        var usage = response.Usage;
        var inTokens = usage?.InputTokenCount ?? 0;
        var outTokens = usage?.OutputTokenCount ?? 0;
        var usd = inTokens / 1_000_000d * UsdPerMillionInput + outTokens / 1_000_000d * UsdPerMillionOutput;

        // Invariant on purpose: this line is the ticket's evidence, and a machine running under a
        // comma-decimal culture would otherwise print "$0,000053" into the resolution.
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: finish={response.FinishReason} · model={response.ModelId} · " +
            $"{inTokens} in / {outTokens} out · ${usd:F6}"));

        if (response.FinishReason == ChatFinishReason.ContentFilter)
        {
            output.WriteLine(
                $"{label}: FILTERED COMPLETION — HTTP 200, finish_reason=content_filter, " +
                $"content length {response.Text.Length}");
        }

        // The deployment name goes up; the real model comes back. Pinned because the metering
        // conclusion (provider identity on a usage row) rests on it holding.
        response.ModelId.Should().StartWith(
            ModelBehindTheDeployment,
            "the response's model field carries the real model, not the deployment name");

        return response;
    }

    /// <summary>Route B, bare: no <see cref="System.ClientModel.Primitives.HttpClientPipelineTransport"/>
    /// override, no per-call policy. If a shim turns out to be needed, it shows up here as a red
    /// test rather than as a surprise in the build.</summary>
    private static IChatClient BuildAzureChatClient()
    {
        var key = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_API_KEY");
        Skip.If(
            string.IsNullOrWhiteSpace(key),
            "EXP-13's dialect probe needs AZURE_FOUNDRY_API_KEY (az cognitiveservices account keys " +
            "list -n experttojob-openai-swc -g rg-experttojob-foundry --query key1 -o tsv).");

        return new OpenAIClient(
                new ApiKeyCredential(key!),
                new OpenAIClientOptions { Endpoint = new Uri(Endpoint) })
            .GetChatClient(Deployment)
            .AsIChatClient();
    }
}
