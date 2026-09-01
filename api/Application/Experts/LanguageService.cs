using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Experts;

public record SaveSpokenLanguageDto(string Language, LanguageLevel Level);

public interface ILanguageService
{
    Task<SpokenLanguageDto> AddAsync(Guid expertId, SaveSpokenLanguageDto dto, CancellationToken ct = default);
    Task<SpokenLanguageDto> UpdateAsync(Guid languageId, SaveSpokenLanguageDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid languageId, CancellationToken ct = default);
}

public class LanguageService : ILanguageService
{
    private readonly IAppDbContext _db;
    private readonly IValidator<SaveSpokenLanguageDto> _validator;
    public LanguageService(IAppDbContext db, IValidator<SaveSpokenLanguageDto> validator)
    {
        _db = db;
        _validator = validator;
    }

    public async Task<SpokenLanguageDto> AddAsync(Guid expertId, SaveSpokenLanguageDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        if (!await _db.Experts.AnyAsync(e => e.Id == expertId, ct))
            throw new NotFoundException(nameof(Expert), expertId);

        var l = new SpokenLanguage
        {
            Id = Guid.NewGuid(),
            ExpertId = expertId,
            Language = dto.Language.Trim(),
            Level = dto.Level,
        };
        _db.SpokenLanguages.Add(l);
        await _db.SaveChangesAsync(ct);
        return new SpokenLanguageDto(l.Id, l.Language, l.Level);
    }

    public async Task<SpokenLanguageDto> UpdateAsync(Guid languageId, SaveSpokenLanguageDto dto, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(dto, ct);
        var l = await _db.SpokenLanguages.FirstOrDefaultAsync(x => x.Id == languageId, ct)
            ?? throw new NotFoundException(nameof(SpokenLanguage), languageId);
        l.Language = dto.Language.Trim();
        l.Level = dto.Level;
        await _db.SaveChangesAsync(ct);
        return new SpokenLanguageDto(l.Id, l.Language, l.Level);
    }

    public async Task DeleteAsync(Guid languageId, CancellationToken ct = default)
    {
        var l = await _db.SpokenLanguages.FirstOrDefaultAsync(x => x.Id == languageId, ct)
            ?? throw new NotFoundException(nameof(SpokenLanguage), languageId);
        _db.SpokenLanguages.Remove(l);
        await _db.SaveChangesAsync(ct);
    }
}

public class SaveSpokenLanguageValidator : AbstractValidator<SaveSpokenLanguageDto>
{
    public SaveSpokenLanguageValidator()
    {
        RuleFor(x => x.Language).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Level).IsInEnum();
    }
}
