using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Auth;
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
    private readonly IOwnershipScopeProvider _scope;
    public ExperienceSkillService(IAppDbContext db, IOwnershipScopeProvider scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<ExperienceSkillDto> AddAsync(Guid experienceId, Guid skillId, CancellationToken ct = default)
    {
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        // Two hops to the owner (link → experience → expert); the catalog skill itself is shared
        // reference data and is not owned by anyone.
        if (!await _db.Experiences.AnyAsync(
                e => e.Id == experienceId && (unrestricted || e.ExpertId == owned), ct))
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
        var (unrestricted, owned) = await _scope.CurrentAsync(ct);
        var link = await _db.ExperienceSkills
            .FirstOrDefaultAsync(
                x => x.Id == experienceSkillId && (unrestricted || x.Experience.ExpertId == owned), ct)
            ?? throw new NotFoundException(nameof(ExperienceSkill), experienceSkillId);
        _db.ExperienceSkills.Remove(link);
        await _db.SaveChangesAsync(ct);
    }
}
