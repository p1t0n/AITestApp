// PROTOTYPE (P1T-81) — throwaway resume-extraction quality gate. Delete after verdict.
//
// Question: is gpt-4o-mini structured extraction good enough on varied resumes to build the
// ingestion pipeline? Runs each fixture through extract → map to real Application DTOs →
// FluentValidation → self-correction (≤2 retries feeding errors back), then scores against
// hand-written ground truth and emits docs/ingestion-gate-report.md.
//
// Run: GEMINI_API_KEY=... dotnet run --project tools/IngestionGatePrototype
// (GitHub Models retired 2026-07-30; provider decision revised to Gemini free tier.)
using System.Text;
using System.Text.Json;
using CvManager.Application.Employees;
using CvManager.Application.Skills;
using CvManager.Domain.Enums;
using CvManager.Tools.IngestionGate;
using FluentValidation;
using FluentValidation.Results;

var token = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
var endpoint = Environment.GetEnvironmentVariable("MODEL_ENDPOINT")
    ?? "https://generativelanguage.googleapis.com/v1beta/openai";
var model = Environment.GetEnvironmentVariable("MODEL_NAME") ?? "gemini-flash-lite-latest";
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("GEMINI_API_KEY not set — aborting. Create a free key at https://aistudio.google.com/apikey");
    return 1;
}

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.Authorization = new("Bearer", token);

var results = new List<FixtureResult>();
foreach (var fixture in Fixtures.All)
{
    Console.WriteLine($"\n=== {fixture.Id} ({fixture.Style}) ===");
    results.Add(await RunFixtureAsync(fixture));
    await Task.Delay(TimeSpan.FromSeconds(2)); // free-tier RPM headroom
}

var report = Report.Render(results);
var reportPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docs/ingestion-gate-report.md"));
File.WriteAllText(reportPath, report);
Console.WriteLine($"\nReport written: {reportPath}");
return 0;

async Task<FixtureResult> RunFixtureAsync(ResumeFixture fixture)
{
    var messages = new List<object>
    {
        new { role = "system", content = Prompts.System },
        new { role = "user", content = fixture.Text },
    };

    Extraction? extraction = null;
    List<string> lastErrors = [];
    int attempts = 0, correctionRecovered = -1;

    for (var attempt = 1; attempt <= 3; attempt++)
    {
        attempts = attempt;
        var raw = await ChatAsync(messages);
        try
        {
            extraction = JsonSerializer.Deserialize<Extraction>(raw, Json.Options);
        }
        catch (JsonException ex)
        {
            lastErrors = [$"Response was not valid JSON for the contract: {ex.Message}"];
            messages.Add(new { role = "assistant", content = raw });
            messages.Add(new { role = "user", content = Prompts.Correction(lastErrors) });
            continue;
        }

        lastErrors = Validate(extraction!);
        Console.WriteLine($"  attempt {attempt}: {(lastErrors.Count == 0 ? "valid" : $"{lastErrors.Count} validation errors")}");
        if (lastErrors.Count == 0)
        {
            if (attempt > 1) correctionRecovered = attempt;
            break;
        }

        messages.Add(new { role = "assistant", content = raw });
        messages.Add(new { role = "user", content = Prompts.Correction(lastErrors) });
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    var score = extraction is null
        ? Score.Empty("no parseable extraction")
        : Scorer.ScoreFixture(fixture, extraction, lastErrors);
    Console.WriteLine($"  {score.Summary}");
    return new FixtureResult(fixture, extraction, attempts, correctionRecovered, lastErrors, score);
}

async Task<string> ChatAsync(List<object> messages)
{
    var payload = JsonSerializer.Serialize(new
    {
        model,
        temperature = 0,
        response_format = new { type = "json_object" },
        messages,
    });

    for (var wait = 5; ; wait *= 2)
    {
        var response = await http.PostAsync($"{endpoint}/chat/completions",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        if ((int)response.StatusCode == 429 && wait <= 40)
        {
            Console.WriteLine($"  429 — waiting {wait}s");
            await Task.Delay(TimeSpan.FromSeconds(wait));
            continue;
        }
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;
    }
}

// Maps the extraction onto the REAL Application write DTOs and runs the REAL validators —
// this is exactly the error surface the ingestion agent's self-correction loop would see.
static List<string> Validate(Extraction e)
{
    var errors = new List<string>();
    void Collect(string scope, ValidationResult r) =>
        errors.AddRange(r.Errors.Select(f => $"{scope}.{f.PropertyName}: {f.ErrorMessage}"));

    var emp = e.Employee ?? new ExtractedEmployee();
    Collect("employee", new SaveEmployeeValidator().Validate(new SaveEmployeeDto(
        emp.FirstName ?? "", emp.LastName ?? "", emp.Title ?? "", emp.Email ?? "",
        emp.Phone, emp.Location, emp.Summary, null)));

    var langValidator = new SaveSpokenLanguageValidator();
    foreach (var (l, i) in (e.Languages ?? []).Select((l, i) => (l, i)))
    {
        if (!Enum.TryParse<LanguageLevel>(l.Level, true, out var level))
        { errors.Add($"languages[{i}].Level: '{l.Level}' is not one of Basic|Conversational|Professional|Fluent|Native."); continue; }
        Collect($"languages[{i}]", langValidator.Validate(new SaveSpokenLanguageDto(l.Language ?? "", level)));
    }

    var expValidator = new SaveExperienceValidator();
    foreach (var (x, i) in (e.Experiences ?? []).Select((x, i) => (x, i)))
    {
        if (!Dates.TryParse(x.StartDate, out var start))
        { errors.Add($"experiences[{i}].StartDate: '{x.StartDate}' is not yyyy, yyyy-MM, or yyyy-MM-dd."); continue; }
        DateOnly? end = null;
        if (x.EndDate is not null)
        {
            if (!Dates.TryParse(x.EndDate, out var parsedEnd))
            { errors.Add($"experiences[{i}].EndDate: '{x.EndDate}' is not yyyy, yyyy-MM, or yyyy-MM-dd."); continue; }
            end = parsedEnd;
        }
        Collect($"experiences[{i}]", expValidator.Validate(new SaveExperienceDto(
            x.Company ?? "", x.Title ?? "", x.Location, start, end, x.Summary,
            (x.Achievements ?? []).Select((a, o) => new SaveAchievementDto(o, a)).ToList(), [])));
    }

    var qualValidator = new SaveQualificationValidator();
    foreach (var (q, i) in (e.Qualifications ?? []).Select((q, i) => (q, i)))
    {
        if (!Enum.TryParse<QualificationType>(q.Type, true, out var type))
        { errors.Add($"qualifications[{i}].Type: '{q.Type}' is not Degree or Certification."); continue; }
        Dates.TryParse(q.StartDate, out var qs);
        DateOnly? qe = Dates.TryParse(q.EndDate, out var qeParsed) ? qeParsed : null;
        Collect($"qualifications[{i}]", qualValidator.Validate(new SaveQualificationDto(
            type, q.Name ?? "", q.Institution, q.Field,
            q.StartDate is null ? null : qs, qe, q.Issuer, q.CredentialId, null, null)));
    }

    var skillValidator = new SaveSkillValidator();
    foreach (var (s, i) in (e.Skills ?? []).Select((s, i) => (s, i)))
    {
        if (s.Level is not null && !Enum.TryParse<SkillLevel>(s.Level, true, out _))
            errors.Add($"skills[{i}].Level: '{s.Level}' is not one of Beginner|Intermediate|Advanced|Expert.");
        if (string.IsNullOrWhiteSpace(s.Name))
            errors.Add($"skills[{i}].Name: must not be empty.");
    }
    _ = skillValidator; // catalog-write validator unused: agent proposes, never creates (P1T-80 §3)

    return errors;
}
