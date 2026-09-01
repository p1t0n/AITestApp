using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Availability;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExpertToJob.Application.Tests;

public class AvailabilityServiceValidationTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"availability-{Guid.NewGuid()}")
            .Options);

    private static AvailabilityService NewService(AppDbContext db) =>
        new(db, new SaveAvailabilityEntryValidator(), new UnrestrictedOwnershipScopeProvider());

    [Fact]
    public async Task AddAsync_with_out_of_range_capacity_throws_ValidationException()
    {
        await using var db = NewDb();
        var svc = NewService(db);

        var act = () => svc.AddAsync(Guid.NewGuid(), new SaveAvailabilityEntryDto(new DateOnly(2027, 1, 1), 150));

        await act.Should().ThrowAsync<ValidationException>();
    }
}
