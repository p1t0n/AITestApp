using FluentValidation;

namespace CvManager.Application.Skills;

public record CategoryDto(Guid Id, string Name, Guid? ParentId);

public record CategoryNodeDto(Guid Id, string Name, IReadOnlyList<CategoryNodeDto> Children, IReadOnlyList<SkillDto> Skills);

public record SkillDto(Guid Id, string Name, Guid CategoryId, string CategoryName, int Rank);

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
