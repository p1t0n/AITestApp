using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The Art. 15 access view assembled end to end through the Application layer (EXP-66), for the two
/// sentences on it that name a company: the recipient list and the search-index note.
///
/// <para>Both were literals until this ticket. The audit in
/// <c>manuals/provider-naming-compliance-audit.md</c> found them as sites #1 and #2 and said the
/// second would "read as a contradiction next to a changed #1" — a page naming Microsoft as the
/// embeddings recipient while its own derived-data paragraph credits Google. That contradiction is
/// what this asserts against.</para>
/// </summary>
public class AccessAndExportServiceTests
{
    [Theory]
    [InlineData(DisclosedEmbeddingsProvider.Gemini, "Google's Gemini models")]
    [InlineData(DisclosedEmbeddingsProvider.AzureFoundry, "Microsoft's Azure OpenAI service")]
    public async Task Search_index_note_names_the_active_embeddings_provider(
        DisclosedEmbeddingsProvider provider, string expected)
    {
        await using var world = World.Under(embeddings: provider);
        var expertId = await world.GivenAPersonAsync();

        var view = await world.Access.AccessAsync(expertId);

        view.Derived.SearchIndexNote.Should().Contain(expected);
        view.Derived.SearchIndexNote.Should().Contain("numeric representations");
    }

    /// <summary>
    /// The contradiction itself: whichever company the note credits has to be the company the
    /// recipient list says receives the narrative. One string moving without the other is the
    /// failure mode, and it is invisible to a test that only reads one of them.
    /// </summary>
    [Theory]
    [InlineData(DisclosedEmbeddingsProvider.Gemini, "Google", "Microsoft")]
    [InlineData(DisclosedEmbeddingsProvider.AzureFoundry, "Microsoft", "Google")]
    public async Task The_note_and_the_recipient_list_name_the_same_embeddings_company(
        DisclosedEmbeddingsProvider provider, string named, string other)
    {
        await using var world = World.Under(embeddings: provider);
        var expertId = await world.GivenAPersonAsync();

        var view = await world.Access.AccessAsync(expertId);

        view.Derived.SearchIndexNote.Should().Contain(named).And.NotContain(other);

        // Found by what the entry says it does rather than by its title: one provider doing both
        // jobs is a single "AI model provider" entry that covers embeddings too, and looking for
        // the word in the title would silently find nothing there.
        view.Recipients.Single(r => r.Why.Contains("turned into search embeddings"))
            .Recipient.Should().Contain(named);
    }

    /// <summary>
    /// The former-recipient entry, at the surface that serves it rather than only at the pure
    /// function: a person who joined while the deployment was on Gemini is owed the fact that
    /// Google already has their narrative, however the page reads in the present tense.
    /// </summary>
    [Fact]
    public async Task A_person_who_predates_the_switch_is_told_google_already_had_their_narrative()
    {
        await using var world = World.Under(embeddings: DisclosedEmbeddingsProvider.AzureFoundry);
        var expertId = await world.GivenAPersonAsync();
        await world.SwitchedFromGeminiAtAsync(DateTimeOffset.UtcNow.AddDays(1));

        var view = await world.Access.AccessAsync(expertId);

        var former = view.Recipients.Single(r => r.Recipient.Contains("formerly"));
        former.Recipient.Should().Be("Google (Gemini), formerly our embeddings provider");
        view.Recipients.Should().Contain(r =>
            r.Recipient == "Microsoft (Azure OpenAI), as our embeddings provider"
            || r.Recipient == "Microsoft (Azure OpenAI), as our AI model provider");
    }

    /// <summary>
    /// A deployment that never switched writes no ended period, so the page carries no past tense
    /// at all — the present-tense Google entry is the whole truth and a second one would confuse it.
    /// </summary>
    [Fact]
    public async Task A_deployment_that_never_switched_shows_no_former_recipient()
    {
        await using var world = World.Under(embeddings: DisclosedEmbeddingsProvider.Gemini);
        var expertId = await world.GivenAPersonAsync();

        var view = await world.Access.AccessAsync(expertId);

        view.Recipients.Should().NotContain(r => r.Recipient.Contains("formerly"));
    }

    /// <summary>The Application layer over an isolated in-memory store, with the two provider
    /// disclosures a host would supply.</summary>
    private sealed class World(ServiceProvider provider) : IAsyncDisposable
    {
        public static World Under(DisclosedEmbeddingsProvider embeddings)
        {
            var services = new ServiceCollection();
            services.AddSingleton(new ChatProviderDisclosure(DisclosedChatProvider.AzureFoundry));
            services.AddSingleton(new EmbeddingsProviderDisclosure(embeddings));
            services.AddApplication();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase($"access-{Guid.NewGuid()}"));
            services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
            services.AddSingleton<IOwnershipScopeProvider, UnrestrictedOwnershipScopeProvider>();
            return new World(services.BuildServiceProvider());
        }

        public IAccessAndExportService Access =>
            provider.GetRequiredService<IAccessAndExportService>();

        public async Task<Guid> GivenAPersonAsync()
        {
            var experts = provider.GetRequiredService<IExpertService>();
            var created = await experts.CreateAsync(new SaveExpertDto(
                "Ada", "Lovelace", "Engineer", $"ada-{Guid.NewGuid():N}@example.com",
                null, null, null, null));

            return created.Id;
        }

        /// <summary>The history the MCP host would have written on the day of the switch.</summary>
        public async Task SwitchedFromGeminiAtAsync(DateTimeOffset at)
        {
            var db = provider.GetRequiredService<AppDbContext>();
            db.EmbeddingsProviderPeriods.AddRange(
                new EmbeddingsProviderPeriod
                {
                    Id = Guid.NewGuid(), Provider = "Gemini", StartedAt = null, EndedAt = at,
                },
                new EmbeddingsProviderPeriod
                {
                    Id = Guid.NewGuid(), Provider = "AzureFoundry", StartedAt = at, EndedAt = null,
                });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync() => await provider.DisposeAsync();
    }
}
