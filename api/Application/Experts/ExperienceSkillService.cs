using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Experts;

public interface IExperienceSkillService
{
    Task<ExperienceSkillDto> AddAsync(Guid experienceId, Guid skillId, CancellationToken ct = default);
    Task DeleteAsync(Guid experienceSkillId, CancellationToken ct = default);
}

public class ExperienceSkillService : IExperienceSkillService
{
    private readonly IAppDbContext _db;
    public ExperienceSkillService(IAppDbContext db) => _db = db;

    public async Task<ExperienceSkillDto> AddAsync(Guid experienceId, Guid skillId, CancellationToken ct = default)
    {
        if (!await _db.Experiences.AnyAsync(e => e.Id == experienceId, ct))
            throw new NotFoundException(nameof(Experience), experienceId);
        if (!await _db.Skills.AnyAsync(s => s.Id == skillId, ct))
            throw new NotFoundException(nameof(Skill), skillId);
        if (await _db.ExperienceSkills.AnyAsync(x => x.ExperienceId == experienceId && x.SkillId == skillId, ct))
            throw new ConflictException("Experience already links this skill.");

        var link = new ExperienceSkill { Id = Guid.NewGuid(), ExperienceId = experienceId, SkillId = skillId };
        _db.ExperienceSkills.Add(link);
        await _db.SaveChangesAsync(ct);
        return await _db.ExperienceSkills.AsNoTracking()
            .Where(x => x.Id == link.Id)
            .Select(x => new ExperienceSkillDto(x.Id, x.SkillId, x.Skill.Name))
            .FirstAsync(ct);
    }

    public async Task DeleteAsync(Guid experienceSkillId, CancellationToken ct = default)
    {
        var link = await _db.ExperienceSkills.FirstOrDefaultAsync(x => x.Id == experienceSkillId, ct)
            ?? throw new NotFoundException(nameof(ExperienceSkill), experienceSkillId);
        _db.ExperienceSkills.Remove(link);
        await _db.SaveChangesAsync(ct);
    }
}
