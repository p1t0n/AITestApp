using ExpertToJob.Application.Compliance;
using ExpertToJob.Domain.Entities;
using FluentAssertions;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// Art. 15(1)(c) has to name the companies this deployment actually sends career data to (EXP-21,
/// EXP-66). It is the only compliance text in this repo a data subject reads, and the only one that
/// can ship a false statement, so both providers are parameters rather than literals.
///
/// <para>Chat and embeddings move independently (<c>manuals/adr-embeddings-provider-seam.md</c> §2
/// decision 16), which is four present-tense combinations rather than two — and a fifth statement
/// for somebody whose narrative went to a provider this deployment has since left.</para>
/// </summary>
public class Art15RecipientDisclosureTests
{
    /// <summary>
    /// The four combinations, from the ADR's own table. One provider doing both jobs is one entry;
    /// two providers are two, and collapsing them would understate the transfer to whichever one
    /// went unnamed.
    /// </summary>
    [Theory]
    [InlineData(DisclosedChatProvider.Gemini, DisclosedEmbeddingsProvider.Gemini,
        "Google (Gemini), as our AI model provider")]
    [InlineData(DisclosedChatProvider.AzureFoundry, DisclosedEmbeddingsProvider.AzureFoundry,
        "Microsoft (Azure OpenAI), as our AI model provider")]
    [InlineData(DisclosedChatProvider.AzureFoundry, DisclosedEmbeddingsProvider.Gemini,
        "Google (Gemini), as our embeddings provider|Microsoft (Azure OpenAI), as our AI model provider")]
    [InlineData(DisclosedChatProvider.Gemini, DisclosedEmbeddingsProvider.AzureFoundry,
        "Microsoft (Azure OpenAI), as our embeddings provider|Google (Gemini), as our AI model provider")]
    public void Recipients_follow_both_providers(
        DisclosedChatProvider chat, DisclosedEmbeddingsProvider embeddings, string expected)
    {
        var recipients = Art15Disclosure.RecipientsFor(chat, embeddings);

        var providerEntries = recipients
            .Where(r => r.Recipient.Contains("Google") || r.Recipient.Contains("Microsoft"))
            .Select(r => r.Recipient)
            .ToList();

        providerEntries.Should().Equal(expected.Split('|'));
        providerEntries.Should().HaveCount(expected.Split('|').Length);
        recipients.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Why));
        recipients.Where(r => providerEntries.Contains(r.Recipient))
            .Should().OnlyContain(r => r.Why.Contains("outside this company"));
    }

    /// <summary>
    /// The one fact that makes the Azure transfer different in kind rather than only in name: the
    /// deployment is EU-confined (ADR §7), and a recipient disclosure that omitted it would leave
    /// the person unable to tell the two providers apart on the question they care about.
    /// </summary>
    [Theory]
    [InlineData(DisclosedChatProvider.Gemini)]
    [InlineData(DisclosedChatProvider.AzureFoundry)]
    public void Azure_embeddings_entry_says_processed_within_the_EU(DisclosedChatProvider chat)
    {
        var recipients = Art15Disclosure.RecipientsFor(chat, DisclosedEmbeddingsProvider.AzureFoundry);

        recipients.Single(r => r.Recipient.Contains("Microsoft"))
            .Why.Should().Contain("within the EU");
    }

    /// <summary>Google never claimed a region, so its entry must not borrow Azure's sentence.</summary>
    [Fact]
    public void The_gemini_embeddings_entry_claims_no_region()
    {
        var recipients = Art15Disclosure.RecipientsFor(
            DisclosedChatProvider.AzureFoundry, DisclosedEmbeddingsProvider.Gemini);

        recipients.Single(r => r.Recipient.Contains("Google"))
            .Why.Should().NotContain("within the EU");
    }

    // ---- The former recipient (EXP-62, ADR §2 decision 18) ----------------------------------

    private static readonly DateTimeOffset SwitchedAt = new(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);

    /// <summary>A Gemini period this deployment has closed, and an open Azure one after it.</summary>
    private static IReadOnlyList<EmbeddingsProviderPeriod> AfterTheSwitch() =>
    [
        new() { Id = Guid.NewGuid(), Provider = "Gemini", StartedAt = null, EndedAt = SwitchedAt },
        new() { Id = Guid.NewGuid(), Provider = "AzureFoundry", StartedAt = SwitchedAt, EndedAt = null },
    ];

    [Fact]
    public void Expert_created_during_a_Gemini_period_sees_Google_as_former_recipient()
    {
        var former = Art15Disclosure.FormerEmbeddingsRecipientFor(
            recordCreatedAt: SwitchedAt.AddDays(-30), periods: AfterTheSwitch());

        former.Should().NotBeNull();
        former!.Recipient.Should().Be("Google (Gemini), formerly our embeddings provider");
        former.Why.Should().Contain("2026-10-01");
        former.Why.Should().Contain("receives nothing new from us");

        // It says the data was sent, and until when. It says nothing about what Google kept —
        // that is not a fact this service can verify, and Art. 5(1)(a) forbids implying it can.
        former.Why.Should().NotContain("delete");
        former.Why.Should().NotContain("retain");

        var recipients = Art15Disclosure.RecipientsFor(
            DisclosedChatProvider.AzureFoundry, DisclosedEmbeddingsProvider.AzureFoundry, former);
        recipients.Should().Contain(former);
        recipients[^1].Recipient.Should().Contain("Clients",
            "the former recipient belongs with the other third parties, not after the clients row");
    }

    [Fact]
    public void Expert_created_after_the_switch_does_not_see_Google()
    {
        Art15Disclosure.FormerEmbeddingsRecipientFor(
            recordCreatedAt: SwitchedAt.AddMinutes(1), periods: AfterTheSwitch())
            .Should().BeNull();
    }

    /// <summary>
    /// A deployment that never left Gemini has an open period and no end date, so there is nothing
    /// to say in the past tense — the present-tense entry already names Google.
    /// </summary>
    [Fact]
    public void An_open_gemini_period_produces_no_former_recipient()
    {
        IReadOnlyList<EmbeddingsProviderPeriod> seededOnly =
        [
            new() { Id = Guid.NewGuid(), Provider = "Gemini", StartedAt = null, EndedAt = null },
        ];

        Art15Disclosure.FormerEmbeddingsRecipientFor(DateTimeOffset.UtcNow, seededOnly)
            .Should().BeNull();
    }

    /// <summary>
    /// Switched away and back again: the sentence has to name the <em>latest</em> end date, because
    /// an earlier one would tell somebody Google stopped receiving their data years before it did.
    /// </summary>
    [Fact]
    public void The_former_recipient_names_the_most_recent_gemini_period_that_ended()
    {
        var first = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        IReadOnlyList<EmbeddingsProviderPeriod> thereAndBack =
        [
            new() { Id = Guid.NewGuid(), Provider = "Gemini", StartedAt = null, EndedAt = first },
            new() { Id = Guid.NewGuid(), Provider = "AzureFoundry", StartedAt = first, EndedAt = second },
            new() { Id = Guid.NewGuid(), Provider = "Gemini", StartedAt = second, EndedAt = null },
        ];

        Art15Disclosure.FormerEmbeddingsRecipientFor(first.AddDays(-1), thereAndBack)!
            .Why.Should().Contain("2026-03-01");
    }

    // ---- Parsing the two configuration keys --------------------------------------------------

    /// <summary>
    /// The disclosure path may not be the thing that breaks somebody's access request, so an
    /// unrecognised provider names every recipient it might be rather than throwing. Over-telling
    /// a data subject is survivable; telling them the wrong recipient, or nothing at all, is not.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("OpenAI")]
    [InlineData("1")]
    [InlineData("Gemini,AzureFoundry")]
    public void An_unrecognised_provider_names_both(string? configured)
    {
        var chat = ChatProviderDisclosure.From(configured);
        var embeddings = EmbeddingsProviderDisclosure.From(configured);

        chat.Provider.Should().BeNull();
        embeddings.Provider.Should().BeNull();

        var recipients = Art15Disclosure.RecipientsFor(chat.Provider, embeddings.Provider);
        recipients.Should().Contain(r => r.Recipient.Contains("Google"));
        recipients.Should().Contain(r => r.Recipient.Contains("Microsoft"));
    }

    /// <summary>One half unreadable is still one half known, and the known half must stay true.</summary>
    [Fact]
    public void An_unrecognised_embeddings_provider_still_names_the_known_chat_provider()
    {
        var recipients = Art15Disclosure.RecipientsFor(DisclosedChatProvider.AzureFoundry, null);

        recipients.Should().Contain(r => r.Recipient.Contains("Google"));
        recipients.Should().Contain(r => r.Recipient.Contains("Microsoft"));
    }

    [Theory]
    [InlineData("Gemini", DisclosedChatProvider.Gemini)]
    [InlineData("gemini", DisclosedChatProvider.Gemini)]
    [InlineData(" AzureFoundry ", DisclosedChatProvider.AzureFoundry)]
    public void A_recognised_provider_parses_off_the_same_key_the_seam_binds(
        string configured, DisclosedChatProvider expected)
    {
        ChatProviderDisclosure.ConfigurationKey.Should().Be("Ai:Chat:Provider");
        ChatProviderDisclosure.From(configured).Provider.Should().Be(expected);
    }

    [Theory]
    [InlineData("Gemini", DisclosedEmbeddingsProvider.Gemini)]
    [InlineData("azurefoundry", DisclosedEmbeddingsProvider.AzureFoundry)]
    [InlineData(" AzureFoundry ", DisclosedEmbeddingsProvider.AzureFoundry)]
    public void The_embeddings_provider_parses_off_the_seams_own_key(
        string configured, DisclosedEmbeddingsProvider expected)
    {
        EmbeddingsProviderDisclosure.ConfigurationKey.Should().Be("Ai:Embeddings:Provider");
        EmbeddingsProviderDisclosure.From(configured).Provider.Should().Be(expected);
    }
}
