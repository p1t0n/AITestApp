using System.Text;
using ExpertToJob.Application.Cv;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Documents;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The PDF render is a headless path with no golden output to diff against, so these tests hold the
/// two things that actually break in practice: that a real PDF comes out, and that a sparse CV — the
/// shape a freshly-ingested draft has — does not throw on the way through the layout engine.
/// </summary>
public class CvPdfRendererTests
{
    private static readonly ICvPdfRenderer Renderer = new CvPdfRenderer();

    private static ExpertDetailDto FullExpert() => new(
        Id: Guid.NewGuid(),
        FirstName: "Alice",
        LastName: "Nguyen",
        Title: "Senior Backend Engineer",
        Email: "alice@example.com",
        Phone: "+49 30 123456",
        Location: "Berlin",
        Summary: "Backend engineer with a decade on distributed .NET systems.",
        PhotoUrl: "https://example.com/alice.png",
        CurrentCapacityPercent: 50,
        Status: ExpertStatus.Active,
        SpokenLanguages: new[]
        {
            new SpokenLanguageDto(Guid.NewGuid(), "English", LanguageLevel.Fluent),
            new SpokenLanguageDto(Guid.NewGuid(), "German", LanguageLevel.Conversational),
        },
        AvailabilityEntries: new[]
        {
            new AvailabilityEntryDto(Guid.NewGuid(), new DateOnly(2027, 4, 1), 50),
            new AvailabilityEntryDto(Guid.NewGuid(), new DateOnly(2027, 10, 1), 100),
        },
        Skills: new[]
        {
            new ExpertSkillDto(Guid.NewGuid(), Guid.NewGuid(), "C#", "Backend", SkillLevel.Expert, 9),
            new ExpertSkillDto(Guid.NewGuid(), Guid.NewGuid(), "EF Core", "Backend", SkillLevel.Advanced, 7),
            new ExpertSkillDto(Guid.NewGuid(), Guid.NewGuid(), "PostgreSQL", "Data", SkillLevel.Advanced, 6),
        },
        Qualifications: new[]
        {
            new QualificationDto(Guid.NewGuid(), QualificationType.Degree, "MSc Computer Science", "TU Munich",
                "Distributed Systems", new DateOnly(2013, 9, 1), new DateOnly(2015, 6, 30), null, null, null, null),
            new QualificationDto(Guid.NewGuid(), QualificationType.Certification, "AWS Solutions Architect",
                null, null, null, null, "AWS", "ID-1", new DateOnly(2023, 3, 12), new DateOnly(2026, 3, 12)),
        },
        Experiences: new[]
        {
            new ExperienceDto(Guid.NewGuid(), "Acme", "Senior Engineer", "Berlin",
                new DateOnly(2020, 1, 1), null, "Lead backend for the billing platform.",
                new[]
                {
                    new AchievementDto(Guid.NewGuid(), 1, "Cut p99 checkout latency 40% by reshaping the write path."),
                    new AchievementDto(Guid.NewGuid(), 2, "Migrated 200 services off the shared database."),
                },
                new[] { new ExperienceSkillDto(Guid.NewGuid(), Guid.NewGuid(), "C#") }),
            new ExperienceDto(Guid.NewGuid(), "Globex", "Engineer", null,
                new DateOnly(2016, 5, 1), new DateOnly(2019, 12, 31), null,
                Array.Empty<AchievementDto>(), Array.Empty<ExperienceSkillDto>()),
        });

    /// <summary>A draft with nothing on it but a name — every optional section empty.</summary>
    private static ExpertDetailDto SparseExpert() => new(
        Id: Guid.NewGuid(),
        FirstName: "Bob",
        LastName: "Stone",
        Title: "Engineer",
        Email: "bob@example.com",
        Phone: null,
        Location: null,
        Summary: null,
        PhotoUrl: null,
        CurrentCapacityPercent: 100,
        Status: ExpertStatus.Draft,
        SpokenLanguages: Array.Empty<SpokenLanguageDto>(),
        AvailabilityEntries: Array.Empty<AvailabilityEntryDto>(),
        Skills: Array.Empty<ExpertSkillDto>(),
        Qualifications: Array.Empty<QualificationDto>(),
        Experiences: Array.Empty<ExperienceDto>());

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

    [Fact]
    public void Renders_a_full_cv_as_a_pdf()
    {
        var pdf = Renderer.Render(CvService.Build(FullExpert()));

        IsPdf(pdf).Should().BeTrue("the response is served as application/pdf");
        pdf.Length.Should().BeGreaterThan(1024, "a populated CV is more than an empty page");
    }

    [Fact]
    public void Renders_a_sparse_cv_without_throwing()
    {
        // Every optional section is empty — the layout must not emit an empty container,
        // which QuestPDF treats as a hard error rather than a blank space.
        var pdf = Renderer.Render(CvService.Build(SparseExpert()));

        IsPdf(pdf).Should().BeTrue();
    }

    [Fact]
    public void Renders_deterministically_for_the_same_input()
    {
        // No timestamp or run id may leak into the document: a byte-identical render is what lets
        // a caller cache or diff the output.
        var cv = CvService.Build(FullExpert());

        Renderer.Render(cv).Should().Equal(Renderer.Render(cv));
    }

    [Fact]
    public void Never_fetches_the_photo()
    {
        // PhotoUrl is a remote resource; invariant "degrade, never 500" is easiest to keep by not
        // making a network call inside a render at all. Out of scope for this slice by design.
        var withPhoto = Renderer.Render(CvService.Build(FullExpert()));
        var withoutPhoto = Renderer.Render(CvService.Build(FullExpert() with { PhotoUrl = null }));

        withPhoto.Should().Equal(withoutPhoto);
    }

    [Theory]
    [InlineData("Alice", "Nguyen", "alice-nguyen-cv.pdf")]
    [InlineData("Zoë", "O'Brien-Smith", "zoe-o-brien-smith-cv.pdf")]
    [InlineData("李", "雷", "cv.pdf")]
    public void Builds_a_download_filename_from_the_expert_name(string first, string last, string expected)
    {
        var cv = CvService.Build(SparseExpert() with { FirstName = first, LastName = last });

        CvPdfFileName.For(cv).Should().Be(expected);
    }
}
