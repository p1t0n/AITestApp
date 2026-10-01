using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpertToJob.Agents.Configuration;
using ExpertToJob.Agents.Mcp;
using ExpertToJob.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// A Development host with <b>no credential for its active chat provider</b> (EXP-93): neither the
/// provider's environment variable nor its <c>Ai:&lt;Provider&gt;:ApiKey</c> path carries a key.
/// That is the documented local condition — "without it the agents degrade" (CLAUDE.md) — and it
/// used to end in <c>ArgumentException: Value cannot be an empty string. (Parameter 'key')</c>
/// escaping a singleton factory during endpoint parameter binding, i.e. a 500 with a stack trace.
///
/// <para><b>The chat client here is the real one.</b> Every other endpoint test in this project
/// substitutes an <c>IChatClient</c>, which is exactly what makes them blind to this: the seam's
/// own client is never built. What is substituted instead is <see cref="ChatCredential"/> — the
/// one value the construction branch resolves from the environment and the configuration — so the
/// host builds its own client, on its own branch, over a credential that is honestly absent. The
/// alternative, clearing <c>GEMINI_API_KEY</c> for the duration, would mutate process-global state
/// that the live smokes in this same assembly read; <see cref="ChatProviderStartupGuard"/> takes
/// its environment reader as a parameter for the same reason, and
/// <see cref="MissingChatCredentialResolutionTests"/> covers the resolution itself.</para>
///
/// <para>MCP tool sources are faked because they are a different dependency with a different
/// failure (an unreachable server is a 502, and it would arrive first); the credential is the
/// subject.</para>
/// </summary>
public class MissingChatCredentialTests
{
    /// <summary>The provider the test host runs on. Azure is the shipped default (EXP-41), so this
    /// is the branch a developer without a key actually hits.</summary>
    private const ChatProvider Provider = ChatProvider.AzureFoundry;

    private static WebApplicationFactory<Program> CredentiallessHost() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                // Last registration wins: the host's own resolved credential is replaced by one
                // that found nothing in either place.
                s.AddSingleton(new ChatCredential(Provider, key: null));
                foreach (var agent in new[]
                         {
                             "roster-qa", "cv-tailoring", "match", "shortlist",
                             "interview-kit", "bench-report", "resume-ingestion", "roster-scan",
                         })
                {
                    s.AddKeyedSingleton<IMcpToolSource>(agent, (_, _) => new FakeToolSource());
                }

                s.AddInMemoryAppDb("missing-credential");
            }));

    /// <summary>Both spellings of the missing setting, in the title, so the fix is one edit away
    /// from the response body alone.</summary>
    private static async Task ShouldNameTheMissingSetting(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(
            HttpStatusCode.ServiceUnavailable,
            "a credential nobody set is a configuration fault, not an upstream one, and not a 500");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var title = body.GetProperty("title").GetString();
        title.Should().Contain("AZURE_FOUNDRY_API_KEY").And.Contain("Ai:AzureFoundry:ApiKey");
        body.GetProperty("status").GetInt32().Should().Be(503);
    }

    [Fact]
    public async Task Shortlist_reports_503_naming_the_missing_key()
    {
        using var factory = CredentiallessHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/shortlist", new { jobDescription = "Platform engineer: Kafka, Kubernetes." });

        await ShouldNameTheMissingSetting(response);
    }

    [Fact]
    public async Task RosterQa_reports_503_naming_the_missing_key()
    {
        using var factory = CredentiallessHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/roster-qa", new { question = "Who knows React?" });

        await ShouldNameTheMissingSetting(response);
    }

    [Fact]
    public async Task InterviewKit_reports_503_naming_the_missing_key()
    {
        using var factory = CredentiallessHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/agents/interview-kit",
            new { expertId = Guid.NewGuid(), jobDescription = "Platform engineer." });

        await ShouldNameTheMissingSetting(response);
    }

    [Fact]
    public async Task BenchReport_reports_503_rather_than_a_narrative_nobody_wrote()
    {
        using var factory = CredentiallessHost();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/agents/bench-report", new { });

        // This endpoint degrades a failed model call to its deterministic summary on purpose, and
        // that is right for a model that faulted. A host with no key is not a faulting model: every
        // call will fail the same way, and a report that reads as normal is how the misconfiguration
        // stays invisible.
        await ShouldNameTheMissingSetting(response);
    }

    /// <summary>
    /// Staffing answers over SSE, so there is no status code to carry the fault once the stream is
    /// open — the contract's terminal <c>error</c> event is where it goes, and the stream closes
    /// after it like any other terminal event.
    /// </summary>
    [Fact]
    public async Task Staffing_emits_the_same_error_as_a_terminal_sse_event()
    {
        using var factory = CredentiallessHost();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostSseAsync(
            "/agents/staffing", new { jobDescription = "Platform engineer: Kafka, Kubernetes." });

        response.EnsureSuccessStatusCode();
        var frames = await response.ReadAllSseFramesAsync();

        var terminal = frames[^1];
        terminal.Event.Should().Be("error", "one terminal event closes the stream");
        terminal.Json.GetProperty("title").GetString()
            .Should().Contain("AZURE_FOUNDRY_API_KEY").And.Contain("Ai:AzureFoundry:ApiKey",
                "the stream's one failure channel carries the same headline the 503s do");
        terminal.Json.GetProperty("detail").GetString().Should().Contain("Ai:Chat:Provider");
        frames.Should().NotContain(f => f.Event == "report", "no run reached a report");
        frames.Should().NotContain(f => f.Event == "stepFailed",
            "a failed shortlist promises no continuation, so the terminal error is its only signal");
    }

    /// <summary>The two read-only surfaces the dock polls do not call a model, and they keep
    /// working without a key exactly as they do today — the degraded host still renders.</summary>
    [Fact]
    public async Task Models_and_usage_keep_answering_without_a_key()
    {
        using var factory = CredentiallessHost();
        using var client = factory.CreateAuthenticatedClient();

        var models = await client.GetAsync("/agents/models");
        models.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await models.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("provider").GetString().Should().Be(Provider.ToString());

        var usage = await client.GetAsync("/agents/usage");
        usage.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>
/// The other half of "no credential": the resolution itself, with both places a provider's key can
/// come from genuinely empty. Process environment variables are global, so the reader is a
/// parameter here — the same shape (and the same reason) as
/// <see cref="ChatProviderStartupGuard.RequireActiveProviderCredential"/>'s.
/// </summary>
public class MissingChatCredentialResolutionTests
{
    [Theory]
    [InlineData(ChatProvider.Gemini)]
    [InlineData(ChatProvider.AzureFoundry)]
    public void An_unset_variable_and_an_empty_config_key_is_a_missing_credential(ChatProvider provider)
    {
        var credential = ChatCredential.Resolve(
            provider, new ChatProviderOptions { ApiKey = "" }, readEnvironmentVariable: _ => null);

        credential.IsMissing.Should().BeTrue();
        credential.Provider.Should().Be(provider);

        var act = () => credential.Key;
        act.Should().Throw<ChatCredentialMissingException>()
            .Which.Title.Should()
            .Contain(ChatProviderOptions.ApiKeyVariableFor(provider),
                "the title has to name the variable an operator would export")
            .And.Contain($"{ChatProviderOptions.SectionFor(provider)}:ApiKey",
                "and the configuration path, which is the other place the branch reads");
    }

    [Fact]
    public void The_environment_variable_wins_over_the_config_key()
    {
        var credential = ChatCredential.Resolve(
            ChatProvider.Gemini,
            new ChatProviderOptions { ApiKey = "from-config" },
            readEnvironmentVariable: name => name == "GEMINI_API_KEY" ? "from-environment" : null);

        credential.IsMissing.Should().BeFalse();
        credential.Key.Should().Be("from-environment");
    }

    [Fact]
    public void The_config_key_is_the_fallback_when_the_variable_is_unset()
    {
        var credential = ChatCredential.Resolve(
            ChatProvider.AzureFoundry,
            new ChatProviderOptions { ApiKey = "from-config" },
            readEnvironmentVariable: _ => "");

        credential.Key.Should().Be("from-config");
    }

    /// <summary>A provider never reads another provider's variable — EXP-53's rule, asserted on the
    /// type that now does the reading.</summary>
    [Fact]
    public void A_provider_never_reads_another_providers_variable()
    {
        var credential = ChatCredential.Resolve(
            ChatProvider.AzureFoundry,
            new ChatProviderOptions { ApiKey = "" },
            readEnvironmentVariable: name => name == "GEMINI_API_KEY" ? "google-key" : null);

        credential.IsMissing.Should().BeTrue("an Azure-configured host must not carry the Google key");
    }
}
