using System.Net;
using System.Net.Http.Json;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Web.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The registration gate and the notify-don't-gate rule (P1T-183), over the real host.
///
/// <para>The passkey ceremony itself needs a browser, so the acknowledgment is checked where it is
/// enforced: <c>signup/begin</c>, before any account exists. That is deliberate placement rather
/// than a testing convenience — refusing there is what stops a "shown but not acknowledged"
/// half-state from being representable at all.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class TransparencyNoticeTests(WebApiFactory factory)
{
    // ---- Readable before you have an account ---------------------------------------------------

    [Fact]
    public async Task The_notice_is_readable_without_signing_in()
    {
        var response = await factory.CreateClient().GetAsync("/api/notice");

        var notice = await response.ReadOkAsync<TransparencyNoticeDto>();
        notice.Version.Should().Be(TransparencyNotice.CurrentVersion);
        notice.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Any_published_version_can_be_fetched_back_by_its_version_string()
    {
        var response = await factory.CreateClient()
            .GetAsync($"/api/notice/{TransparencyNotice.CurrentVersion}");

        (await response.ReadOkAsync<TransparencyNoticeDto>()).Text
            .Should().Be(TransparencyNotice.Current.Text,
                "a recorded acknowledgment proves nothing if the words cannot be recovered");
    }

    [Fact]
    public async Task A_version_that_was_never_published_is_a_404()
    {
        var response = await factory.CreateClient().GetAsync("/api/notice/1999-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Declining blocks registration ---------------------------------------------------------

    [Fact]
    public async Task Registration_without_an_acknowledgment_is_refused()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/signup/begin",
            new { email = ApiClientExtensions.UniqueEmail("declines"), controlWord = "hunter2" },
            WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("transparency notice");
    }

    [Fact]
    public async Task Registration_acknowledging_a_version_nobody_published_is_refused()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/signup/begin",
            new SignupBeginRequest(ApiClientExtensions.UniqueEmail("invented"), "hunter2", "1999-01-01"),
            WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Registration_acknowledging_the_current_notice_proceeds()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/signup/begin",
            new SignupBeginRequest(
                ApiClientExtensions.UniqueEmail("acknowledges"), "hunter2", TransparencyNotice.CurrentVersion),
            WebApiFactory.Json);

        (await response.ReadOkAsync<SignupBeginResponse>()).CeremonyId.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// There is no half-state to be in: the refusal happens before the ceremony is stashed, so a
    /// person who declined leaves no account, no ceremony and nothing for a later surface to
    /// interpret.
    /// </summary>
    [Fact]
    public async Task Declining_leaves_no_account_behind()
    {
        var email = ApiClientExtensions.UniqueEmail("declined");

        await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/signup/begin",
            new { email, controlWord = "hunter2", acknowledgedNoticeVersion = (string?)null },
            WebApiFactory.Json);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.AnyAsync(u => u.Email == email)).Should().BeFalse();
    }

    // ---- Notify at next sign-in, and gate nothing ----------------------------------------------

    [Fact]
    public async Task An_expert_who_has_acknowledged_nothing_is_told_a_notice_is_waiting()
    {
        var (client, _) = factory.CreateClientFor(UserRole.Expert);

        var status = await (await client.GetAsync("/api/notice/status")).ReadOkAsync<NoticeStatusResponse>();

        status.PendingVersion.Should().Be(TransparencyNotice.CurrentVersion);
        status.AcknowledgedVersion.Should().BeNull();
    }

    [Fact]
    public async Task A_pending_notice_gates_nothing()
    {
        // A row of their own, and a notice they have never acknowledged.
        var staff = factory.CreateAuthenticatedClient();
        var expert = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());
        var (client, _) = factory.CreateExpertClientOwning(expert.Id);

        (await (await client.GetAsync("/api/notice/status")).ReadOkAsync<NoticeStatusResponse>())
            .PendingVersion.Should().NotBeNull();

        var own = await client.GetAsync($"/api/experts/{expert.Id}");
        own.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "a changed notice notifies — it never freezes anybody's data pending a click");
    }

    [Fact]
    public async Task Acknowledging_clears_the_pending_notice_and_records_it_on_the_account()
    {
        var (client, account) = factory.CreateClientFor(UserRole.Expert);

        var status = await (await client.PostAsJsonAsync(
                "/api/notice/acknowledge",
                new AcknowledgeNoticeRequest(TransparencyNotice.CurrentVersion),
                WebApiFactory.Json))
            .ReadOkAsync<NoticeStatusResponse>();

        status.PendingVersion.Should().BeNull();
        status.AcknowledgedVersion.Should().Be(TransparencyNotice.CurrentVersion);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == account.Id);
        stored.AcknowledgedNoticeVersion.Should().Be(TransparencyNotice.CurrentVersion);
        stored.NoticeAcknowledgedAt.Should().NotBeNull();
    }

    /// <summary>
    /// When the person acknowledging owns a roster row, the acknowledgment reaches the row's history
    /// too — appended, on the basis it was already on. A notice version is a new fact about the same
    /// relationship, not a new relationship.
    /// </summary>
    [Fact]
    public async Task Acknowledging_appends_to_the_owned_rows_history_without_moving_its_basis()
    {
        var staff = factory.CreateAuthenticatedClient();
        var expert = await staff.CreateExpertAsync(ApiClientExtensions.NewExpert());
        var (client, _) = factory.CreateExpertClientOwning(expert.Id);

        (await client.PostAsJsonAsync(
                "/api/notice/acknowledge",
                new AcknowledgeNoticeRequest(TransparencyNotice.CurrentVersion),
                WebApiFactory.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var history = await db.ProcessingRecords.AsNoTracking()
            .Where(r => r.ExpertId == expert.Id).OrderBy(r => r.Sequence).ToListAsync();

        history.Should().HaveCount(2);
        history[0].NoticeVersion.Should().BeNull();
        history[1].NoticeVersion.Should().Be(TransparencyNotice.CurrentVersion);
        history.Should().OnlyContain(r => r.Basis == LawfulBasis.LegitimateInterest);
    }

    [Fact]
    public async Task Acknowledging_a_version_nobody_published_is_refused()
    {
        var (client, _) = factory.CreateClientFor(UserRole.Expert);

        var response = await client.PostAsJsonAsync(
            "/api/notice/acknowledge", new AcknowledgeNoticeRequest("1999-01-01"), WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_acknowledge_endpoint_needs_a_session()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/notice/acknowledge",
            new AcknowledgeNoticeRequest(TransparencyNotice.CurrentVersion),
            WebApiFactory.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
