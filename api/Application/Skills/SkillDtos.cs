namespace EmployeeManager.Application.Skills;

public record CategoryDto(Guid Id, string Name, Guid? ParentId);

public record CategoryNodeDto(Guid Id, string Name, IReadOnlyList<CategoryNodeDto> Children, IReadOnlyList<SkillDto> Skills);

public record SkillDto(Guid Id, string Name, Guid CategoryId, string CategoryName);

public record SaveCategoryDto(string Name, Guid? ParentId);

public record SaveSkillDto(string Name, Guid CategoryId);
