// PROTOTYPE (P1T-81) — throwaway. Extraction contract, prompts, scoring, report.
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CvManager.Tools.IngestionGate;

public static class Prompts
{
    // Mirrors the P1T-80 contract: employee core + children, NO availability, dates only as
    // precise as the text states them, nulls over guesses.
    public const string System = """
        You extract structured data from a resume. Reply with ONE JSON object, no prose:
        {
          "employee": { "firstName", "lastName", "title", "email", "phone", "location", "summary" },
          "languages": [ { "language", "level" } ],
          "skills": [ { "name", "level", "yearsExperience" } ],
          "qualifications": [ { "type", "name", "institution", "field", "startDate", "endDate", "issuer", "credentialId" } ],
          "experiences": [ { "company", "title", "location", "startDate", "endDate", "summary", "achievements": ["..."], "skills": ["..."] } ]
        }
        Rules:
        - Use ONLY facts present in the resume text. If a value is absent (even email), use null — NEVER invent one.
        - Dates: "yyyy", "yyyy-MM", or "yyyy-MM-dd" — only as precise as the text states. A current role has endDate null.
        - language level: Basic|Conversational|Professional|Fluent|Native. skill level: Beginner|Intermediate|Advanced|Expert or null.
        - qualification type: Degree|Certification.
        - achievements: concrete result bullets for that role, verbatim-faithful, no embellishment.
        - "title" on employee: the person's current/primary job title.
        """;

    public static string Correction(IReadOnlyList<string> errors) =>
        "The extraction failed validation:\n- " + string.Join("\n- ", errors) +
        "\nReply with the FULL corrected JSON object. Fix only what the errors name; if a required fact truly is not in the resume, keep it null.";
}

public sealed class Extraction
{
    public ExtractedEmployee? Employee { get; set; }
    public List<ExtractedLanguage>? Languages { get; set; }
    public List<ExtractedSkill>? Skills { get; set; }
    public List<ExtractedQualification>? Qualifications { get; set; }
    public List<ExtractedExperience>? Experiences { get; set; }
}

public sealed class ExtractedEmployee
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Title { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Location { get; set; }
    public string? Summary { get; set; }
}

public sealed class ExtractedLanguage { public string? Language { get; set; } public string? Level { get; set; } }
public sealed class ExtractedSkill { public string? Name { get; set; } public string? Level { get; set; } public decimal? YearsExperience { get; set; } }

public sealed class ExtractedQualification
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Institution { get; set; }
    public string? Field { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Issuer { get; set; }
    public string? CredentialId { get; set; }
}

public sealed class ExtractedExperience
{
    public string? Company { get; set; }
    public string? Title { get; set; }
    public string? Location { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Summary { get; set; }
    public List<string>? Achievements { get; set; }
    public List<string>? Skills { get; set; }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}

public static class Dates
{
    /// <summary>Accepts yyyy / yyyy-MM / yyyy-MM-dd; pads missing parts with 01 for DTO validation.</summary>
    public static bool TryParse(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split('-');
        if (parts.Length is < 1 or > 3) return false;
        if (!int.TryParse(parts[0], out var y) || parts[0].Length != 4) return false;
        var m = 1;
        var d = 1;
        if (parts.Length > 1 && (!int.TryParse(parts[1], out m) || m is < 1 or > 12)) return false;
        if (parts.Length > 2 && (!int.TryParse(parts[2], out d) || d is < 1 or > 31)) return false;
        try { date = new DateOnly(y, m, d); return true; } catch { return false; }
    }
}

public sealed record Score(
    int FieldsCorrect, int FieldsTotal,
    double SkillRecall, double SkillPrecision, int HallucinatedSkills,
    int CatalogMatched, int Proposals,
    double LanguageRecall, double QualRecall,
    int ExpMatched, int ExpTotal, int DateErrors, int InventedDatePrecision,
    bool FabricatedEmail, string Notes, string Summary)
{
    public static Score Empty(string reason) =>
        new(0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, reason, "FAILED: " + reason);
}

public sealed record FixtureResult(
    ResumeFixture Fixture, Extraction? Extraction, int Attempts, int CorrectionRecoveredAt,
    IReadOnlyList<string> RemainingErrors, Score Score);

public static class Scorer
{
    private static string Norm(string? s) =>
        string.Join(' ', (s ?? "").ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static Score ScoreFixture(ResumeFixture fixture, Extraction e, IReadOnlyList<string> remainingErrors)
    {
        var truth = fixture.Truth;
        var notes = new List<string>();
        var emp = e.Employee ?? new ExtractedEmployee();

        var fieldsTotal = 4;
        var fieldsCorrect = 0;
        if (Norm(emp.FirstName) == Norm(truth.FirstName)) fieldsCorrect++; else notes.Add($"firstName '{emp.FirstName}'");
        if (Norm(emp.LastName) == Norm(truth.LastName)) fieldsCorrect++; else notes.Add($"lastName '{emp.LastName}'");
        if (Norm(emp.Title).Contains(Norm(truth.Title)) || Norm(truth.Title).Contains(Norm(emp.Title)) && emp.Title is not null)
            fieldsCorrect++;
        else notes.Add($"title '{emp.Title}'");
        var fabricatedEmail = false;
        if (truth.Email is null)
        {
            if (string.IsNullOrWhiteSpace(emp.Email)) fieldsCorrect++;
            else { fabricatedEmail = true; notes.Add($"FABRICATED email '{emp.Email}'"); }
        }
        else if (Norm(emp.Email) == Norm(truth.Email)) fieldsCorrect++;
        else notes.Add($"email '{emp.Email}'");

        var truthSkills = truth.Skills.Select(s => Norm(s.Name)).ToHashSet();
        var extractedSkills = (e.Skills ?? []).Where(s => s.Name is not null).Select(s => Norm(s.Name)).ToHashSet();
        var hits = truthSkills.Intersect(extractedSkills).Count();
        var skillRecall = truthSkills.Count == 0 ? 1 : (double)hits / truthSkills.Count;
        var skillPrecision = extractedSkills.Count == 0 ? 1 : (double)hits / extractedSkills.Count;
        var resumeNorm = Norm(fixture.Text);
        var hallucinated = extractedSkills.Count(s => !truthSkills.Contains(s) && !resumeNorm.Contains(s));
        var catalogMatched = extractedSkills.Count(s => Fixtures.Catalog.Contains(s));
        var proposals = extractedSkills.Count - catalogMatched;

        var truthLangs = truth.Languages.Select(Norm).ToHashSet();
        var extractedLangs = (e.Languages ?? []).Select(l => Norm(l.Language)).ToHashSet();
        var langRecall = truthLangs.Count == 0 ? 1 : (double)truthLangs.Intersect(extractedLangs).Count() / truthLangs.Count;

        var extractedQuals = (e.Qualifications ?? []).Select(q => Norm(q.Name)).ToList();
        var qualHits = truth.Qualifications.Count(tq =>
            extractedQuals.Any(eq => eq.Contains(Norm(tq)) || Norm(tq).Contains(eq) && eq.Length > 0));
        var qualRecall = truth.Qualifications.Count == 0 ? 1 : (double)qualHits / truth.Qualifications.Count;

        int expMatched = 0, dateErrors = 0, inventedPrecision = 0;
        foreach (var te in truth.Experiences)
        {
            var match = (e.Experiences ?? []).FirstOrDefault(x =>
                Norm(x.Company).Contains(Norm(te.Company)) || Norm(te.Company).Contains(Norm(x.Company)) && x.Company is not null);
            if (match is null) { notes.Add($"missing experience '{te.Company}'"); continue; }
            expMatched++;
            CheckDate(te.StartDate, match.StartDate, "start");
            CheckDate(te.EndDate, match.EndDate, "end");

            void CheckDate(string? truthDate, string? extractedDate, string which)
            {
                if (truthDate is null)
                {
                    if (extractedDate is not null) { dateErrors++; notes.Add($"{te.Company} {which} '{extractedDate}' (truth: open)"); }
                    return;
                }
                if (extractedDate is null) { dateErrors++; notes.Add($"{te.Company} {which} missing"); return; }
                if (!extractedDate.StartsWith(truthDate)) { dateErrors++; notes.Add($"{te.Company} {which} '{extractedDate}' ≠ '{truthDate}'"); }
                else if (extractedDate.Length > truthDate.Length) inventedPrecision++;
            }
        }

        var summary = $"fields {fieldsCorrect}/{fieldsTotal}, skills R{skillRecall:P0}/P{skillPrecision:P0}, " +
                      $"halluc {hallucinated}, exp {expMatched}/{truth.Experiences.Count}, dateErr {dateErrors}" +
                      (remainingErrors.Count > 0 ? $", UNRESOLVED {remainingErrors.Count} validation errors" : "");

        return new Score(fieldsCorrect, fieldsTotal, skillRecall, skillPrecision, hallucinated,
            catalogMatched, proposals, langRecall, qualRecall,
            expMatched, truth.Experiences.Count, dateErrors, inventedPrecision,
            fabricatedEmail, string.Join("; ", notes), summary);
    }
}

public static class Report
{
    public static string Render(IReadOnlyList<FixtureResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Ingestion gate prototype (P1T-81): Gemini flash resume extraction");
        sb.AppendLine();
        sb.AppendLine($"Model: Gemini flash (free tier, OpenAI-compatible endpoint), temperature 0, JSON mode. {results.Count} fixtures, ≤2 self-correction retries against the real Application validators.");
        sb.AppendLine();
        sb.AppendLine("| fixture | style | attempts | fields | skill recall | skill precision | halluc. skills | catalog/proposed | lang recall | qual recall | experiences | date errors | invented date precision | fabricated email |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in results)
        {
            var s = r.Score;
            sb.AppendLine($"| {r.Fixture.Id} | {r.Fixture.Style} | {r.Attempts}{(r.CorrectionRecoveredAt > 0 ? " (recovered)" : "")} " +
                $"| {s.FieldsCorrect}/{s.FieldsTotal} | {s.SkillRecall:P0} | {s.SkillPrecision:P0} | {s.HallucinatedSkills} " +
                $"| {s.CatalogMatched}/{s.Proposals} | {s.LanguageRecall:P0} | {s.QualRecall:P0} " +
                $"| {s.ExpMatched}/{s.ExpTotal} | {s.DateErrors} | {s.InventedDatePrecision} | {(s.FabricatedEmail ? "YES" : "no")} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Aggregates");
        sb.AppendLine();
        double Avg(Func<Score, double> f) => results.Average(r => f(r.Score));
        sb.AppendLine($"- Employee fields exact: {results.Sum(r => r.Score.FieldsCorrect)}/{results.Sum(r => r.Score.FieldsTotal)}");
        sb.AppendLine($"- Skill recall avg {Avg(s => s.SkillRecall):P0}, precision avg {Avg(s => s.SkillPrecision):P0}, hallucinated total {results.Sum(r => r.Score.HallucinatedSkills)}");
        sb.AppendLine($"- Experiences matched: {results.Sum(r => r.Score.ExpMatched)}/{results.Sum(r => r.Score.ExpTotal)}; date errors {results.Sum(r => r.Score.DateErrors)}; invented precision {results.Sum(r => r.Score.InventedDatePrecision)}");
        sb.AppendLine($"- Language recall avg {Avg(s => s.LanguageRecall):P0}; qualification recall avg {Avg(s => s.QualRecall):P0}");
        sb.AppendLine($"- Runs needing self-correction: {results.Count(r => r.Attempts > 1)}; recovered: {results.Count(r => r.CorrectionRecoveredAt > 0)}; still invalid after retries: {results.Count(r => r.RemainingErrors.Count > 0)}");
        sb.AppendLine($"- Fabricated emails: {results.Count(r => r.Score.FabricatedEmail)} (no-email fixture must abort, not invent)");
        sb.AppendLine();
        sb.AppendLine("## Per-fixture notes");
        sb.AppendLine();
        foreach (var r in results.Where(r => r.Score.Notes.Length > 0 || r.RemainingErrors.Count > 0))
        {
            sb.AppendLine($"- **{r.Fixture.Id}**: {r.Score.Notes}");
            foreach (var err in r.RemainingErrors) sb.AppendLine($"  - unresolved: {err}");
        }
        sb.AppendLine();
        sb.AppendLine("## Verdict");
        sb.AppendLine();
        sb.AppendLine("_(human)_ go / adjust / kill —");
        return sb.ToString();
    }
}
