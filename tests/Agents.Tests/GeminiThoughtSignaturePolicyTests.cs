using CvManager.Agents.Configuration;
using FluentAssertions;
using System.Text.Json.Nodes;

namespace CvManager.Agents.Tests;

/// <summary>
/// The pure JSON transform behind <see cref="GeminiThoughtSignaturePolicy"/>: Gemini 3 rejects
/// replayed assistant tool calls without a thought signature, so the policy injects the
/// documented bypass sentinel — and must leave everything else untouched.
/// </summary>
public class GeminiThoughtSignaturePolicyTests
{
    [Fact]
    public void Injects_sentinel_into_assistant_tool_calls_without_signature()
    {
        var json = """
        {"model":"m","messages":[
          {"role":"user","content":"hi"},
          {"role":"assistant","tool_calls":[{"id":"1","type":"function","function":{"name":"cv_get","arguments":"{}"}}]},
          {"role":"tool","tool_call_id":"1","content":"cv"}
        ]}
        """;

        var result = GeminiThoughtSignaturePolicy.InjectSignatures(json);

        result.Should().NotBeNull();
        var toolCall = JsonNode.Parse(result!)!["messages"]![1]!["tool_calls"]![0]!;
        toolCall["extra_content"]!["google"]!["thought_signature"]!.GetValue<string>()
            .Should().Be("skip_thought_signature_validator");
        toolCall["function"]!["name"]!.GetValue<string>().Should().Be("cv_get");
    }

    [Fact]
    public void Preserves_an_existing_signature()
    {
        var json = """
        {"messages":[{"role":"assistant","tool_calls":[
          {"id":"1","extra_content":{"google":{"thought_signature":"real-sig"}}}
        ]}]}
        """;

        GeminiThoughtSignaturePolicy.InjectSignatures(json).Should().BeNull();
    }

    [Fact]
    public void Leaves_requests_without_tool_calls_alone()
    {
        GeminiThoughtSignaturePolicy.InjectSignatures(
            """{"messages":[{"role":"user","content":"hi"}]}""").Should().BeNull();
    }

    [Fact]
    public void Ignores_tool_calls_on_non_assistant_messages()
    {
        GeminiThoughtSignaturePolicy.InjectSignatures(
            """{"messages":[{"role":"user","tool_calls":[{"id":"1"}]}]}""").Should().BeNull();
    }
}
