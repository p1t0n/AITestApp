using CvManager.Agents.Configuration;
using FluentAssertions;
using System.Text.Json.Nodes;

namespace CvManager.Agents.Tests;

/// <summary>Gemini reports nonstandard finish_reason values the OpenAI SDK's enum parser throws
/// on; the compat handler rewrites unknown values to "stop" and leaves standard ones alone.</summary>
public class GeminiCompatHandlerTests
{
    [Fact]
    public void Rewrites_a_nonstandard_finish_reason_to_stop()
    {
        var body = """{"choices":[{"finish_reason":"function_call_filter: MALFORMED_FUNCTION_CALL","index":0,"message":{"content":""}}]}""";

        var normalized = GeminiCompatHandler.NormalizeFinishReasons(body);

        normalized.Should().NotBeNull();
        JsonNode.Parse(normalized!)!["choices"]![0]!["finish_reason"]!.GetValue<string>().Should().Be("stop");
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("length")]
    [InlineData("tool_calls")]
    [InlineData("content_filter")]
    public void Leaves_standard_finish_reasons_untouched(string reason)
    {
        var body = $$"""{"choices":[{"finish_reason":"{{reason}}","index":0}]}""";
        GeminiCompatHandler.NormalizeFinishReasons(body).Should().BeNull();
    }

    [Fact]
    public void Ignores_bodies_that_are_not_chat_completions()
    {
        GeminiCompatHandler.NormalizeFinishReasons("""{"data":[1,2,3]}""").Should().BeNull();
        GeminiCompatHandler.NormalizeFinishReasons("not json at all").Should().BeNull();
    }
}
