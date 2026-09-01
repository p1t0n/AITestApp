using System.Globalization;
using ExpertToJob.Domain.Entities;

namespace ExpertToJob.RetrievalEval;

/// <summary>
/// Maps the frozen eval corpus onto real domain entities so the eval exercises the exact production
/// pipeline: <c>ChunkProjection</c> renders these employees the same way it renders roster ones.
/// </summary>
public static class EvalCorpusSeeder
{
    public static Employee ToEmployee(EvalEmployee fixture)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            FirstName = fixture.FirstName,
            LastName = fixture.LastName,
            Title = fixture.Title,
            Location = fixture.Location,
            Summary = fixture.Summary,
            Email = $"{fixture.Key}@eval.example.com",
        };

        foreach (var experience in fixture.Experiences)
        {
            employee.Experiences.Add(new Experience
            {
                Id = Guid.NewGuid(),
                Company = experience.Company,
                Title = experience.Title,
                StartDate = ParseMonth(experience.StartMonth),
                EndDate = experience.EndMonth is { } end ? ParseMonth(end) : null,
                Summary = experience.Summary,
                Achievements = experience.Achievements
                    .Select((text, i) => new Achievement { Order = i + 1, Text = text })
                    .ToList(),
            });
        }

        return employee;
    }

    /// <summary>"yyyy-MM" fixture months land on the first of the month.</summary>
    private static DateOnly ParseMonth(string month)
        => DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
