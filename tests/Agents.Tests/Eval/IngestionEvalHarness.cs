using System.Text.Json;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Tests.Eval;

/// <summary>Everything one eval run staged, captured from the fake MCP tools' arguments.</summary>
public sealed class IngestionWriteLog
{
    public SaveExpertDto? Draft { get; set; }
    public List<SaveSpokenLanguageDto> Languages { get; } = [];
    public List<Guid> SkillIds { get; } = [];
    public List<SaveQualificationDto> Qualifications { get; } = [];
    public List<SaveExperienceDto> Experiences { get; } = [];
    public int ValidationRejections { get; set; }
}

/// <summary>
/// The fake MCP surface for the ingestion eval: same tool names and result shapes as the real
/// server — successes return DTO-ish JSON, failures return the structured
/// {"code","message","fields"} error produced by the REAL Application validators, so the agent's
/// self-correction loop sees production error shapes. Writes are recorded for scoring; nothing
/// touches a database.
/// </summary>
public static class IngestionEvalTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static (IReadOnlyList<AITool> Tools, IngestionWriteLog Log, IReadOnlyDictionary<Guid, string> Catalog)
        Create()
    {
        var log = new IngestionWriteLog();
        var draftId = Guid.NewGuid();

        // Deterministic ids per catalog name so id→name scoring is exact.
        var catalog = Fixtures.Catalog
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select((name, i) => (Name: name, Id: new Guid($"{i + 1:D8}-0000-0000-0000-000000000000")))
            .ToList();
        var catalogById = catalog.ToDictionary(c => c.Id, c => c.Name);

        // Honours nameContains the way SkillCatalogService.OrderedSkills does (case-insensitive
        // substring, trimmed), because since P1T-155 the agent's step 1 resolves one skill name
        // per call rather than loading the catalog. A fake that ignored the filter would hand back
        // the whole catalog and quietly score a run the production tool could not produce.
        var skillList = AIFunctionFactory.Create(
            (string? nameContains) => JsonSerializer.Serialize(
                catalog
                    .Where(c => string.IsNullOrWhiteSpace(nameContains)
                                || c.Name.Contains(nameContains.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(c => new { id = c.Id, name = c.Name, categoryId = Guid.Empty, categoryName = "Eval" }),
                Json),
            "skill_list");

        var createDraft = AIFunctionFactory.Create(
            (SaveExpertDto dto) => Guard(log, new SaveExpertValidator().Validate(dto), () =>
            {
                log.Draft = dto;
                return JsonSerializer.Serialize(
                    new { expert = new { id = draftId, dto.FirstName, dto.LastName }, duplicateWarning = (string?)null }, Json);
            }),
            "expert_create_draft",
            "Create a DRAFT expert from root fields.");

        var addLanguage = AIFunctionFactory.Create(
            (Guid expertId, SaveSpokenLanguageDto dto) => Guard(log, new SaveSpokenLanguageValidator().Validate(dto), () =>
            {
                log.Languages.Add(dto);
                return Created();
            }),
            "language_add",
            "Add a spoken language to an expert.");

        var addSkill = AIFunctionFactory.Create(
            (Guid expertId, SaveExpertSkillDto dto) =>
            {
                if (!catalogById.ContainsKey(dto.SkillId))
                {
                    log.ValidationRejections++;
                    return Error("not_found", $"Skill {dto.SkillId} does not exist in the catalog.");
                }

                log.SkillIds.Add(dto.SkillId);
                return Created();
            },
            "expert_skill_add",
            "Add a catalog skill to an expert.");

        var addQualification = AIFunctionFactory.Create(
            (Guid expertId, SaveQualificationDto dto) => Guard(log, new SaveQualificationValidator().Validate(dto), () =>
            {
                log.Qualifications.Add(dto);
                return Created();
            }),
            "qualification_add",
            "Add a degree or certification to an expert.");

        var addExperience = AIFunctionFactory.Create(
            (Guid expertId, SaveExperienceDto dto) => Guard(log, new SaveExperienceValidator().Validate(dto), () =>
            {
                log.Experiences.Add(dto);
                return Created();
            }),
            "experience_add",
            "Add a work-experience record (with achievements and skill ids) to an expert.");

        return ([skillList, createDraft, addLanguage, addSkill, addQualification, addExperience], log, catalogById);
    }

    private static string Guard(IngestionWriteLog log, ValidationResult validation, Func<string> onValid)
    {
        if (validation.IsValid)
        {
            return onValid();
        }

        log.ValidationRejections++;
        return JsonSerializer.Serialize(new
        {
            code = "validation_failed",
            message = "Validation failed.",
            fields = validation.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage }),
        }, Json);
    }

    private static string Created() =>
        JsonSerializer.Serialize(new { id = Guid.NewGuid() }, Json);

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { code, message, fields = Array.Empty<object>() }, Json);
}

/// <summary>Per-fixture scores over the recorded writes, plus the aggregate report.</summary>
public sealed record IngestionFixtureScore(
    string FixtureId,
    int FieldsCorrect,
    double SkillRecall,
    double SkillPrecision,
    int HallucinatedSkills,
    bool FabricatedEmail,
    int ExperiencesMatched,
    int ExperiencesExpected,
    int DateErrors,
    double LanguageRecall,
    double QualificationRecall,
    IReadOnlyList<string> Proposals,
    string Notes);

public static class IngestionEvalScorer
{
    private static string Norm(string? s) =>
        string.Join(' ', (s ?? "").ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static IngestionFixtureScore Score(
        ResumeFixture fixture,
        IngestionWriteLog log,
        IReadOnlyDictionary<Guid, string> catalog,
        IReadOnlyList<string> proposals)
    {
        var truth = fixture.Truth;
        var notes = new List<string>();
        var draft = log.Draft;

        var fields = 0;
        if (draft is not null)
        {
            if (Norm(draft.FirstName) == Norm(truth.FirstName)) fields++; else notes.Add($"firstName '{draft.FirstName}'");
            if (Norm(draft.LastName) == Norm(truth.LastName)) fields++; else notes.Add($"lastName '{draft.LastName}'");
            var titleMatch = Norm(draft.Title).Contains(Norm(truth.Title)) || Norm(truth.Title).Contains(Norm(draft.Title));
            if (titleMatch && draft.Title.Length > 0) fields++; else notes.Add($"title '{draft.Title}'");
        }

        var fabricated = false;
        if (truth.Email is null)
        {
            if (string.IsNullOrWhiteSpace(draft?.Email)) fields++;
            else { fabricated = true; notes.Add($"FABRICATED email '{draft?.Email}'"); }
        }
        else if (Norm(draft?.Email) == Norm(truth.Email)) fields++;
        else notes.Add($"email '{draft?.Email}'");

        // Recall is judged against catalog-AVAILABLE truth skills only: a skill the catalog
        // doesn't carry cannot be written — the correct behavior is proposing it, not adding it.
        var truthSkills = truth.Skills.Where(s => s.InCatalog).Select(s => Norm(s.Name)).ToHashSet();
        var writtenSkills = log.SkillIds
            .Where(catalog.ContainsKey)
            .Select(id => Norm(catalog[id]))
            .ToHashSet();
        var hits = truthSkills.Intersect(writtenSkills).Count();
        var skillRecall = truthSkills.Count == 0 ? 1 : (double)hits / truthSkills.Count;
        var skillPrecision = writtenSkills.Count == 0 ? 1 : (double)hits / writtenSkills.Count;
        var resumeNorm = Norm(fixture.Text);
        // A written skill neither in the truth set nor mentioned in the text is a hallucination;
        // synonym-mapped writes (e.g. "golang" → Go) count against precision but not honesty.
        var hallucinated = writtenSkills.Count(s => !truthSkills.Contains(s) && !resumeNorm.Contains(s));

        var truthLangs = truth.Languages.Select(Norm).ToHashSet();
        var writtenLangs = log.Languages.Select(l => Norm(l.Language)).ToHashSet();
        var langRecall = truthLangs.Count == 0 ? 1 : (double)truthLangs.Intersect(writtenLangs).Count() / truthLangs.Count;

        var writtenQuals = log.Qualifications.Select(q => Norm(q.Name)).ToList();
        var qualHits = truth.Qualifications.Count(tq =>
            writtenQuals.Any(wq => wq.Contains(Norm(tq)) || (Norm(tq).Contains(wq) && wq.Length > 0)));
        var qualRecall = truth.Qualifications.Count == 0 ? 1 : (double)qualHits / truth.Qualifications.Count;

        int matched = 0, dateErrors = 0;
        foreach (var te in truth.Experiences)
        {
            var exp = log.Experiences.FirstOrDefault(x =>
                Norm(x.Company).Contains(Norm(te.Company)) || Norm(te.Company).Contains(Norm(x.Company)));
            if (exp is null) { notes.Add($"missing experience '{te.Company}'"); continue; }
            matched++;
            if (te.StartDate is not null && !exp.StartDate.ToString("yyyy-MM-dd").StartsWith(te.StartDate))
            { dateErrors++; notes.Add($"{te.Company} start {exp.StartDate:yyyy-MM-dd} ≠ {te.StartDate}"); }
            if (te.EndDate is null && exp.EndDate is not null)
            { dateErrors++; notes.Add($"{te.Company} end {exp.EndDate:yyyy-MM-dd} (truth: open)"); }
            else if (te.EndDate is not null && exp.EndDate is not null
                     && !exp.EndDate.Value.ToString("yyyy-MM-dd").StartsWith(te.EndDate))
            { dateErrors++; notes.Add($"{te.Company} end {exp.EndDate:yyyy-MM-dd} ≠ {te.EndDate}"); }
        }

        return new IngestionFixtureScore(
            fixture.Id, fields, skillRecall, skillPrecision, hallucinated, fabricated,
            matched, truth.Experiences.Count, dateErrors, langRecall, qualRecall,
            proposals, string.Join("; ", notes));
    }
}
