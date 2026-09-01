using FluentValidation;

namespace ExpertToJob.Application.Skills;

public record CategoryDto(Guid Id, string Name, Guid? ParentId);

public record CategoryNodeDto(Guid Id, string Name, IReadOnlyList<CategoryNodeDto> Children, IReadOnlyList<SkillDto> Skills);

public record SkillDto(Guid Id, string Name, Guid CategoryId, string CategoryName, int Rank);

/// <summary>What a caller wants out of the skill catalog (P1T-145): an optional case-insensitive
/// name substring, and which page of the matches. Resolving one skill name is the common case, and
/// it should cost a row rather than the whole catalog.</summary>
public sealed record SkillQuery(string? NameContains = null, int? Page = null, int? PageSize = null);

/// <summary>One page of catalog skills, with the match total so a caller can size a full sweep
/// (pages = ceil(Total/PageSize)) and tell "no matches" apart from "past the last page".</summary>
public sealed record SkillPage(int Page, int PageSize, int Total, IReadOnlyList<SkillDto> Items);

public record SaveCategoryDto(string Name, Guid? ParentId);

public record SaveSkillDto(string Name, Guid CategoryId);

public class SaveCategoryValidator : AbstractValidator<SaveCategoryDto>
{
    public SaveCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class SaveSkillValidator : AbstractValidator<SaveSkillDto>
{
    public SaveSkillValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CategoryId).NotEmpty();
    }
}
