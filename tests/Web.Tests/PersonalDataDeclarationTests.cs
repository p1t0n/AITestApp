using ExpertToJob.Application.Compliance;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Web.Tests;

/// <summary>
/// The audit behind the declaration (P1T-186): <b>a store carrying a person that nobody declared is
/// a store erasure does not reach</b>, and the only way that stays true as tables are added is to
/// ask the database what tables exist rather than to keep a list.
///
/// <para>It runs here, against the real Postgres model, for a reason that would otherwise bite
/// silently: <c>AppDbContext</c> branches on the provider, and under EF InMemory
/// <c>ExpertSearchChunk.Embedding</c> is <c>Ignore</c>d entirely — so an in-memory version of this
/// test could not see the column holding a vector of every person's CV.</para>
/// </summary>
[Collection(WebApiCollection.Name)]
public class PersonalDataDeclarationTests(WebApiFactory factory)
{
    /// <summary>
    /// A property name that ties a row to a person — any <c>*ExpertId</c> or <c>*UserId</c>, so
    /// <c>OwnerUserId</c>, <c>ClaimantUserId</c> and <c>RequestedByUserId</c> are all caught without
    /// a list of spellings somebody has to remember to extend.
    /// </summary>
    private static bool NamesAPerson(string property) =>
        property.EndsWith("ExpertId", StringComparison.Ordinal)
        || property.EndsWith("UserId", StringComparison.Ordinal);

    /// <summary>
    /// Stores that name a person but hold nothing of theirs and outlive them on purpose. Each one is
    /// a decision, written down — this list is not a place to put a table somebody has not thought
    /// about yet.
    /// </summary>
    private static readonly Dictionary<string, string> NotPersonalData = new()
    {
        ["ScoringJob"] =
            "the run, not the people in it: RequestedByUserId is the Service Manager who started "
            + "it and clears to null with their account. Its candidate rows are declared.",
        ["StaffingProposal"] =
            "declared for its package; its own user columns are the requester and the approver, "
            + "both staff, both SetNull.",
    };

    [Fact]
    public void Every_store_that_names_a_person_is_declared()
    {
        var undeclared = PersonBearingEntities()
            .Where(entity => !NotPersonalData.ContainsKey(entity)
                             && PersonalDataDeclaration.All.All(s => s.Entity != entity))
            .OrderBy(x => x)
            .ToList();

        undeclared.Should().BeEmpty(
            "a table carrying an ExpertId, a UserId or an OwnerUserId either holds personal data or "
            + "points at somebody, and erasure only reaches what is declared. Add it to "
            + $"{nameof(PersonalDataDeclaration)} with an action and a reason — or, if it genuinely "
            + $"holds nothing of theirs, to {nameof(NotPersonalData)} with the reason why. "
            + "Undeclared: " + string.Join(", ", undeclared));
    }

    /// <summary>Keeps the audit honest: a sweep that discovered nothing, or one whose exemptions had
    /// drifted onto tables that no longer exist, would otherwise pass in silence.</summary>
    [Fact]
    public void The_audit_reads_the_model_it_claims_to()
    {
        var found = PersonBearingEntities().ToList();

        found.Should().HaveCountGreaterThanOrEqualTo(12, "the schema is comfortably larger than this floor");
        found.Should().Contain("ExpertSearchChunk", "the store the in-memory provider cannot see");
        found.Should().Contain("Achievement", "two hops from the Expert, and the easiest to miss");
        found.Should().Contain("ScoringJobCandidate", "the store that had no foreign key at all");

        foreach (var (entity, reason) in NotPersonalData)
        {
            found.Should().Contain(entity, $"the exemption '{reason}' names a table that still exists");
        }
    }

    /// <summary>Every declared entity is a real one. A declaration naming a table that was renamed
    /// away is a declaration quietly covering nothing.</summary>
    [Fact]
    public void Every_declared_store_still_exists()
    {
        var model = ModelEntities();

        foreach (var store in PersonalDataDeclaration.All)
        {
            model.Should().Contain(store.Entity, $"'{store.Entity}' is declared but not in the model");
        }
    }

    /// <summary>Every declared personal field is a real property. This is the half that rots first:
    /// a renamed column leaves the declaration reading plausibly and covering nothing.</summary>
    [Fact]
    public void Every_declared_personal_field_is_a_real_column()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var store in PersonalDataDeclaration.All)
        {
            var entity = db.Model.GetEntityTypes().Single(e => e.ClrType.Name == store.Entity);
            var properties = entity.GetProperties().Select(p => p.Name).ToHashSet();

            foreach (var field in store.PersonalFields)
            {
                properties.Should().Contain(field,
                    $"{store.Entity}.{field} is declared personal but is not a mapped property");
            }
        }
    }

    /// <summary>Each entry says why. An action with no reason is a decision nobody made.</summary>
    [Fact]
    public void Every_entry_carries_a_reason()
    {
        PersonalDataDeclaration.All.Should().OnlyContain(s => s.Reason.Trim().Length > 15);
        PersonalDataDeclaration.All.Select(s => s.Entity).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Every entity that names a person, <b>or reaches one through a chain of foreign keys</b>. The
    /// transitive half is not decoration: <c>Achievement</c> is a person's own writing and carries
    /// no id at all — it hangs off <c>Experience</c>, which hangs off <c>Expert</c> — and a sweep
    /// that only read column names would have declared the schema clean while leaving every
    /// achievement bullet in the database. Iterated to a fixed point, so a third hop is covered the
    /// day somebody adds one.
    /// </summary>
    private IEnumerable<string> PersonBearingEntities()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entities = db.Model.GetEntityTypes().ToList();

        var bearing = entities
            .Where(e => e.GetProperties().Any(p => NamesAPerson(p.Name)))
            .Select(e => e.ClrType.Name)
            .ToHashSet();

        bool grew;
        do
        {
            grew = false;
            foreach (var entity in entities)
            {
                if (bearing.Contains(entity.ClrType.Name))
                {
                    continue;
                }

                var reaches = entity.GetForeignKeys()
                    .Any(fk => bearing.Contains(fk.PrincipalEntityType.ClrType.Name));
                if (reaches)
                {
                    bearing.Add(entity.ClrType.Name);
                    grew = true;
                }
            }
        }
        while (grew);

        return bearing.OrderBy(x => x).ToList();
    }

    private IEnumerable<string> ModelEntities()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToList();
    }
}
