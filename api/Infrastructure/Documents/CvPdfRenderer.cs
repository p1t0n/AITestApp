using System.Globalization;
using CvManager.Application.Cv;
using CvManager.Application.Employees;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CvManager.Infrastructure.Documents;

/// <summary>
/// Renders a <see cref="CvDto"/> to PDF with QuestPDF. Chosen over a headless-browser print of the SPA
/// because it needs no Chromium process, no extra container and no network: the whole render is a pure
/// function of the DTO, which keeps it usable from a background worker or an agent export path.
/// Stateless and thread-safe, so it is registered as a singleton.
/// </summary>
public sealed class CvPdfRenderer : ICvPdfRenderer
{
    private const float SectionSpacing = 14f;

    /// <summary>QuestPDF's Community licence — free for the POC bar this project sets, and the reason
    /// no licence key or paid tier enters the dependency graph.</summary>
    static CvPdfRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(CvDto cv) =>
        Document.Create(doc => doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.6f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Colors.Grey.Darken4));

                page.Header().Element(c => Header(c, cv));
                page.Content().PaddingTop(SectionSpacing).Element(c => Body(c, cv));
                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Medium));
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            }))
            // Pinned so the same CV renders byte-identically every time; QuestPDF would otherwise
            // stamp DateTime.Now into the document metadata and make every render differ.
            .WithMetadata(new DocumentMetadata
            {
                Title = $"{cv.FullName} — CV",
                Author = cv.FullName,
                CreationDate = DateTimeOffset.UnixEpoch,
                ModifiedDate = DateTimeOffset.UnixEpoch,
            })
            .GeneratePdf();

    private static void Header(IContainer container, CvDto cv)
    {
        // PhotoUrl is deliberately ignored: fetching it would put a network call — and a failure mode —
        // inside a render that is otherwise a pure projection.
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(name =>
                {
                    name.Item().Text(cv.FullName).FontSize(20).SemiBold();
                    name.Item().Text(cv.Title).FontSize(11).FontColor(Colors.Blue.Darken2);
                });

                row.ConstantItem(110).AlignRight().AlignMiddle()
                    .Text($"{cv.Availability.CurrentCapacityPercent}% available")
                    .FontSize(9).SemiBold()
                    .FontColor(cv.Availability.CurrentCapacityPercent >= 100
                        ? Colors.Green.Darken2
                        : Colors.Orange.Darken2);
            });

            var contacts = Join(" · ", cv.Email, cv.Phone, cv.Location);
            if (contacts.Length > 0)
                col.Item().PaddingTop(2).Text(contacts).FontSize(9).FontColor(Colors.Grey.Darken1);

            col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
        });
    }

    private static void Body(IContainer container, CvDto cv)
    {
        container.Column(col =>
        {
            col.Spacing(SectionSpacing);

            if (!string.IsNullOrWhiteSpace(cv.Summary))
                Section(col, "Summary", s => s.Item().Text(cv.Summary));

            if (cv.SkillGroups.Count > 0)
                Section(col, "Skills", s =>
                {
                    foreach (var group in cv.SkillGroups)
                    {
                        s.Item().Text(t =>
                        {
                            t.Span($"{group.Category}: ").SemiBold();
                            t.Span(string.Join(", ", group.Skills.Select(sk => $"{sk.SkillName} ({sk.Level})")));
                        });
                    }
                });

            if (cv.Experiences.Count > 0)
                // Roles need more air between them than list lines do, or two jobs read as one.
                Section(col, "Experience", s =>
                {
                    foreach (var experience in cv.Experiences)
                        s.Item().Element(c => Experience(c, experience));
                }, itemSpacing: 8f);

            if (cv.Education.Count > 0)
                Section(col, "Education", s =>
                {
                    foreach (var q in cv.Education) s.Item().Text(DegreeLine(q));
                });

            if (cv.Certifications.Count > 0)
                Section(col, "Certifications", s =>
                {
                    foreach (var q in cv.Certifications) s.Item().Text(CertificationLine(q));
                });

            if (cv.Languages.Count > 0)
                Section(col, "Languages", s => s.Item()
                    .Text(string.Join(" · ", cv.Languages.Select(l => $"{l.Language} ({l.Level})"))));

            // A CV with nothing on it but a header would leave the content column empty, which
            // QuestPDF rejects outright — say so instead.
            if (IsEmpty(cv))
                col.Item().Text("No CV content recorded for this employee yet.")
                    .Italic().FontColor(Colors.Grey.Darken1);
        });
    }

    private static bool IsEmpty(CvDto cv) =>
        string.IsNullOrWhiteSpace(cv.Summary) && cv.SkillGroups.Count == 0 && cv.Experiences.Count == 0
        && cv.Education.Count == 0 && cv.Certifications.Count == 0 && cv.Languages.Count == 0;

    private static void Section(
        ColumnDescriptor col, string title, Action<ColumnDescriptor> content, float itemSpacing = 3f)
    {
        col.Item().Column(section =>
        {
            section.Item().Text(title.ToUpperInvariant())
                .FontSize(8.5f).SemiBold().LetterSpacing(0.12f).FontColor(Colors.Blue.Darken2);
            section.Item().PaddingBottom(4).LineHorizontal(0.6f).LineColor(Colors.Grey.Lighten1);
            section.Item().Column(body =>
            {
                body.Spacing(itemSpacing);
                content(body);
            });
        });
    }

    private static void Experience(IContainer container, CvExperienceDto experience)
    {
        container.Column(col =>
        {
            col.Spacing(2);

            col.Item().Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span($"{experience.Title} · {experience.Company}").SemiBold();
                    if (!string.IsNullOrWhiteSpace(experience.Location))
                        t.Span($" — {experience.Location}").FontColor(Colors.Grey.Darken1);
                });
                row.ConstantItem(100).AlignRight()
                    .Text(experience.Period).FontColor(Colors.Grey.Darken1);
            });

            if (!string.IsNullOrWhiteSpace(experience.Summary))
                col.Item().Text(experience.Summary);

            foreach (var achievement in experience.Achievements)
            {
                col.Item().PaddingLeft(8).Row(row =>
                {
                    row.ConstantItem(10).Text("•");
                    row.RelativeItem().Text(achievement.Text);
                });
            }

            if (experience.Skills.Count > 0)
                col.Item().Text(string.Join(" · ", experience.Skills))
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
        });
    }

    private static string DegreeLine(QualificationDto q) =>
        Join(" · ", q.Name, q.Institution, q.Field, Period(q.StartDate, q.EndDate));

    private static string CertificationLine(QualificationDto q)
    {
        var when = q.ExpiryDate is { } expiry ? $"expires {Date(expiry)}"
            : q.IssueDate is { } issued ? $"issued {Date(issued)}"
            : null;
        return Join(" · ", q.Name, q.Issuer, q.CredentialId, when);
    }

    private static string Period(DateOnly? start, DateOnly? end)
    {
        var parts = new[] { start, end }.Where(d => d.HasValue).Select(d => Date(d!.Value));
        return string.Join(" – ", parts);
    }

    private static string Date(DateOnly value) => value.ToString("MMM yyyy", CultureInfo.InvariantCulture);

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
