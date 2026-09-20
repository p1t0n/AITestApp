using ExpertToJob.Application.Compliance;
using FluentAssertions;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// Art. 15(1)(c) has to name the chat provider this deployment actually sends career data to
/// (EXP-21). It is the only compliance text in this repo a data subject reads, and the only one
/// that can ship a false statement, so the provider is a parameter rather than a literal.
///
/// <para>Embeddings stay on Google whatever chat does (<c>manuals/adr-chat-provider-seam.md</c>),
/// which is why the Azure deployment discloses <em>two</em> recipients rather than swapping one
/// name for another.</para>
/// </summary>
public class Art15RecipientDisclosureTests
{
    [Fact]
    public void Under_gemini_one_entry_names_google_for_everything()
    {
        var recipients = Art15Disclosure.RecipientsFor(DisclosedChatProvider.Gemini);

        recipients.Should().ContainSingle(r => r.Recipient.Contains("Google"));
        recipients.Should().NotContain(r => r.Recipient.Contains("Microsoft"));
        recipients.Single(r => r.Recipient.Contains("Google")).Why.Should().Contain("outside this company");
    }

    [Fact]
    public void Under_azure_the_two_recipients_are_named_separately()
    {
        var recipients = Art15Disclosure.RecipientsFor(DisclosedChatProvider.AzureFoundry);

        var google = recipients.Single(r => r.Recipient.Contains("Google"));
        var microsoft = recipients.Single(r => r.Recipient.Contains("Microsoft"));

        google.Recipient.Should().Contain("embeddings provider");
        microsoft.Recipient.Should().Contain("AI model provider");
        google.Why.Should().Contain("outside this company");
        microsoft.Why.Should().Contain("outside this company");
    }

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
        var disclosure = ChatProviderDisclosure.From(configured);

        disclosure.Provider.Should().BeNull();
        var recipients = Art15Disclosure.RecipientsFor(disclosure.Provider);
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
}
