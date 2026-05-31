using EmployeeManager.Application.Abstractions;
using EmployeeManager.Application.Common;
using EmployeeManager.Domain.Entities;
using EmployeeManager.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManager.Application.Employees;

public record SaveQualificationDto(
    QualificationType Type,
    string Name,
    string? Institution,
    string? Field,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Issuer,
    string? CredentialId,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate);

public interface IQualificationService
{
    Task<QualificationDto> AddAsync(Guid employeeId, SaveQualificationDto dto, CancellationToken ct = default);
    Task<QualificationDto> UpdateAsync(Guid qualificationId, SaveQualificationDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid qualificationId, CancellationToken ct = default);
}

public class QualificationService : IQualificationService
{
    private readonly IAppDbContext _db;
    public QualificationService(IAppDbContext db) => _db = db;

    public async Task<QualificationDto> AddAsync(Guid employeeId, SaveQualificationDto dto, CancellationToken ct = default)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId, ct))
            throw new NotFoundException(nameof(Employee), employeeId);

        var q = new Qualification { Id = Guid.NewGuid(), EmployeeId = employeeId };
        Apply(q, dto);
        _db.Qualifications.Add(q);
        await _db.SaveChangesAsync(ct);
        return ToDto(q);
    }

    public async Task<QualificationDto> UpdateAsync(Guid qualificationId, SaveQualificationDto dto, CancellationToken ct = default)
    {
        var q = await _db.Qualifications.FirstOrDefaultAsync(x => x.Id == qualificationId, ct)
            ?? throw new NotFoundException(nameof(Qualification), qualificationId);
        Apply(q, dto);
        await _db.SaveChangesAsync(ct);
        return ToDto(q);
    }

    public async Task DeleteAsync(Guid qualificationId, CancellationToken ct = default)
    {
        var q = await _db.Qualifications.FirstOrDefaultAsync(x => x.Id == qualificationId, ct)
            ?? throw new NotFoundException(nameof(Qualification), qualificationId);
        _db.Qualifications.Remove(q);
        await _db.SaveChangesAsync(ct);
    }

    private static void Apply(Qualification q, SaveQualificationDto d)
    {
        q.Type = d.Type;
        q.Name = d.Name.Trim();
        q.Institution = d.Institution;
        q.Field = d.Field;
        q.StartDate = d.StartDate;
        q.EndDate = d.EndDate;
        q.Issuer = d.Issuer;
        q.CredentialId = d.CredentialId;
        q.IssueDate = d.IssueDate;
        q.ExpiryDate = d.ExpiryDate;
    }

    private static QualificationDto ToDto(Qualification q) => new(
        q.Id, q.Type, q.Name, q.Institution, q.Field, q.StartDate, q.EndDate,
        q.Issuer, q.CredentialId, q.IssueDate, q.ExpiryDate);
}

public class SaveQualificationValidator : AbstractValidator<SaveQualificationDto>
{
    public SaveQualificationValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(250);
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate!.Value)
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue)
            .WithMessage("EndDate must be on or after StartDate.");
    }
}
