using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Serialization;
using CvManager.Agents.Configuration;
using FluentAssertions;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CvManager.Agents.Tests;

/// <summary>
/// Live probes of the Gemini OpenAI-compatibility endpoint (P1T-115): which structured-output
/// and tool-forcing knobs does the compat wire actually honor? The compat docs document
/// <c>response_format</c> json_schema but only <c>tool_choice: "auto"</c>; these probes pin the
/// observed behavior so a silent capability change on Google's side turns a test red before it
/// corrupts an agent. Results + the resulting method decisions are recorded in
/// <c>manuals/anthropic-gemini-ichatclient-mapping.md</c>.
///
/// Excluded from the default run; execute with <c>dotnet test --filter "Category=live"</c> and
/// <c>GEMINI_API_KEY</c> set. No MCP server or database needed — these talk straight to the model.
/// </summary>
[Trait("Category", "live")]
public class CompatEndpointProbeTests
{
    /// <summary>Deliberately exercises the schema features extraction will lean on:
    /// a string enum, a nullable int, and a required array.</summary>
    private sealed record ProbeExtraction(
        [property: JsonPropertyName("seniority")] Seniority Seniority,
        [property: JsonPropertyName("minYears")] int? MinYears,
        [property: JsonPropertyName("requirements")] IReadOnlyList<string> Requirements);

    [JsonConverter(typeof(JsonStringEnumConverter<Seniority>))]
    private enum Seniority
    {
        Junior,
        Mid,
        Senior,
        Unspecified,
    }

    private const string Jd =
        "Job description: 'We need a senior backend engineer, 5+ years of experience, " +
        "for event streaming and cloud infrastructure work.' " +
        "Extract the seniority, minimum years, and the capability requirements.";

    [SkippableFact]
    public async Task Native_json_schema_response_format_is_honored()
    {
        var client = BuildChatClient();

        var response = await client.GetResponseAsync<ProbeExtraction>(
            Jd, useJsonSchemaResponseFormat: true);

        response.TryGetResult(out var result).Should().BeTrue(
            "the compat endpoint documents response_format json_schema, so the native path must parse");
        result!.Requirements.Should().NotBeEmpty();
        result.Seniority.Should().Be(Seniority.Senior);
        result.MinYears.Should().Be(5);
    }

    [SkippableFact]
    public async Task Schema_in_prompt_fallback_is_honored()
    {
        var client = BuildChatClient();

        var response = await client.GetResponseAsync<ProbeExtraction>(
            Jd, useJsonSchemaResponseFormat: false);

        response.TryGetResult(out var result).Should().BeTrue(
            "the documented fallback (JSON mode + schema injected into the prompt) must parse");
        result!.Requirements.Should().NotBeEmpty();
        result.Seniority.Should().Be(Seniority.Senior);
    }

    [SkippableFact]
    public async Task RequireAny_tool_mode_forces_a_function_call()
    {
        var client = BuildChatClient();
        var options = new ChatOptions
        {
            Tools = [LookupTool()],
            ToolMode = ChatToolMode.RequireAny,
        };

        // Neutral prompt: nothing about it invites a tool call — only forcing produces one.
        var response = await client.GetResponseAsync("Say hello.", options);

        FunctionCalls(response).Should().NotBeEmpty(
            "tool_choice forcing is undocumented on the compat endpoint; if this turns red the wire stopped honoring 'required'");
    }

    [SkippableFact]
    public async Task RequireSpecific_tool_mode_forces_that_function()
    {
        var client = BuildChatClient();
        var options = new ChatOptions
        {
            Tools = [LookupTool()],
            ToolMode = ChatToolMode.RequireSpecific("lookup_office_city"),
        };

        var response = await client.GetResponseAsync("Say hello.", options);

        var calls = FunctionCalls(response);
        calls.Should().NotBeEmpty(
            "named-function forcing is undocumented on the compat endpoint; if this turns red the wire stopped honoring it");
        calls.Should().OnlyContain(c => c.Name == "lookup_office_city");
    }

    [SkippableFact]
    public async Task Structured_output_combined_with_tools_works_in_one_request()
    {
        // Gemini documents structured-output + function-calling combined for the 3 series only;
        // the compat wire's support is undocumented. P1T-118 converts agents that end a TOOL run
        // with structured JSON (Match, ResumeIngestion), so this combo is the load-bearing probe.
        var client = BuildChatClient();
        var options = new ChatOptions
        {
            Tools = [LookupTool()],
            ResponseFormat = ChatResponseFormat.ForJsonSchema<ProbeExtraction>(),
        };

        var first = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User,
                "Look up the office city for employee 'Alex Doe' with the tool, then report: " +
                "seniority Senior, minYears 5, and one requirement naming that city.")],
            options);

        var calls = FunctionCalls(first);
        calls.Should().NotBeEmpty("the model must still be able to call tools under a response format");

        // Manual second turn (no FunctionInvokingChatClient here): return the tool result and ask
        // for the final structured answer.
        var followUp = new List<ChatMessage>
        {
            new(ChatRole.User,
                "Look up the office city for employee 'Alex Doe' with the tool, then report: " +
                "seniority Senior, minYears 5, and one requirement naming that city."),
        };
        followUp.AddRange(first.Messages);
        followUp.Add(new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent(calls[0].CallId, "Amsterdam")]));

        var final = await client.GetResponseAsync(followUp, options);
        var result = System.Text.Json.JsonSerializer.Deserialize<ProbeExtraction>(
            final.Text, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        result.Should().NotBeNull("the final message after the tool round-trip must be schema-valid JSON");
        result!.Requirements.Should().Contain(r => r.Contains("Amsterdam", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The raw production wiring (endpoint, compat handler, thought-signature policy,
    /// pinned model) without metering/OTel — probes measure the wire, not our decorators.</summary>
    private static IChatClient BuildChatClient()
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        Skip.If(string.IsNullOrWhiteSpace(apiKey), "Live probe needs GEMINI_API_KEY.");

        var cfg = new GeminiOptions();
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(cfg.Endpoint),
            Transport = new HttpClientPipelineTransport(
                new HttpClient(new GeminiCompatHandler(new HttpClientHandler()))),
        };
        options.AddPolicy(new GeminiThoughtSignaturePolicy(), PipelinePosition.PerCall);
        return new OpenAIClient(new ApiKeyCredential(apiKey!), options)
            .GetChatClient(cfg.Model)
            .AsIChatClient();
    }

    private static AIFunction LookupTool() => AIFunctionFactory.Create(
        (string employeeName) => "Amsterdam",
        "lookup_office_city",
        "Returns the office city for the named employee.");

    private static List<FunctionCallContent> FunctionCalls(ChatResponse response) =>
        response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
}
