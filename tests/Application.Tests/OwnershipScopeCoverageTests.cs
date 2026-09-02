using System.Reflection;
using ExpertToJob.Application.Auth;
using ExpertToJob.Application.Availability;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Cv;
using ExpertToJob.Application.Experts;
using ExpertToJob.Application.Skills;
using ExpertToJob.Application.Users;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The audit for row ownership (P1T-182). A service that forgets to apply the ownership scope is a
/// silent hole — the call succeeds, the caller gets someone else's data, and no test that only ever
/// looks at the happy path notices. So this does not check a list of methods somebody remembered to
/// write down: it reflects over every roster service in the Application assembly, calls **every**
/// method that addresses a row by id as an Expert who owns a *different* row, and requires each one
/// to behave as though the row did not exist.
///
/// <para>Two ways this fails on purpose. A method that drops the scope returns or mutates data and
/// no <see cref="NotFoundException"/> arrives. A method with a new id parameter this fixture cannot
/// seed throws instead of being skipped — a skipped method is exactly the hole being hunted.</para>
///
/// <para>The unrestricted direction is asserted too: with <see cref="OwnershipScope.Unrestricted"/>
/// every one of the same calls reaches the row. Without that half, a service could pass by refusing
/// everybody.</para>
/// </summary>
public class OwnershipScopeCoverageTests
{
    /// <summary>
    /// Services deliberately outside row ownership, each for a reason that is not "we didn't get to
    /// it". Anything else in the assembly is audited automatically, so a new roster service is
    /// covered the moment it exists — which is the property a hand-kept include-list cannot have.
    /// </summary>
    private static readonly Dictionary<Type, string> NotRosterOwned = new()
    {
        [typeof(IUserService)] = "accounts, not roster rows: a staff-only surface with no owner column",
        [typeof(Compliance.IErasureService)] =
            "not addressed by row id: its Guid is the acting account, and the row it erases is "
            + "whichever one that account owns. Registered by the Web host alongside the control-"
            + "word hasher it depends on, so it is not in this container at all (P1T-186)",
        [typeof(Visibility.IExpertVisibilityService)] =
            "not addressed by row id at all: every method takes the acting account and resolves " +
            "that account's own row through OwnerUserId, which is why the API cannot express " +
            "\"pause somebody else\" in the first place (P1T-185)",
        [typeof(Claims.IClaimService)] =
            "the surface that decides ownership: staff act on rows nobody owns yet, and a claim " +
            "code is redeemed by somebody precisely because they do not own the row yet. Scoping it " +
            "to the owner column would make the column a precondition for writing itself. What " +
            "guards it is the endpoint policy above it and the code itself (P1T-184).",
        [typeof(ISkillCatalogService)] =
            "the catalog is shared vocabulary — nobody's personal data; its writes are refused at the endpoint",
        [typeof(Search.IExpertDigestService)] = "an agent surface: roster-wide by definition",
        [typeof(Search.IExpertFilterService)] = "an agent surface: roster-wide by definition",
        [typeof(Search.ISemanticSearchService)] = "an agent surface: roster-wide by definition",
        [typeof(Search.IExemplarSearchService)] =
            "an agent surface, and its output is anonymized before it leaves",
        [typeof(Search.IShortlistSearchService)] = "an agent surface: roster-wide by definition",
    };

    /// <summary>Every audited service interface, discovered rather than listed.</summary>
    public static TheoryData<Type> RosterServices()
    {
        var data = new TheoryData<Type>();
        foreach (var type in AuditedServices())
        {
            data.Add(type);
        }

        return data;
    }

    private static IEnumerable<Type> AuditedServices() =>
        typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.IsPublic && t.Name.StartsWith('I') && t.Name.EndsWith("Service"))
            .Where(t => !NotRosterOwned.ContainsKey(t))
            .OrderBy(t => t.Name);

    [Fact]
    public void The_audit_covers_the_services_it_claims_to()
    {
        var audited = AuditedServices().Select(t => t.Name).ToList();

        // A floor, so an audit that discovered nothing cannot pass in silence.
        audited.Should().HaveCountGreaterThanOrEqualTo(7);
        audited.Should().Contain(nameof(IAchievementService), "the two-hop route is the easiest to miss");
        audited.Should().Contain(nameof(ICvService), "the CV is the whole record in one response");
    }

    [Theory]
    [MemberData(nameof(RosterServices))]
    public async Task Every_method_that_addresses_a_row_refuses_a_foreign_owner(Type serviceType)
    {
        var methods = RowAddressingMethods(serviceType);
        methods.Should().NotBeEmpty($"{serviceType.Name} has no id-taking method — is it a service at all?");

        foreach (var method in methods)
        {
            // A world per method: several of these mutate, and a delete running before an update
            // would make the update 404 for the wrong reason entirely.
            await using var world = await World.CreateAsync(OwnershipScope.OwnedBy(Guid.NewGuid()));
            var act = async () => await world.InvokeAsync(world.Resolve(serviceType), method);

            await act.Should().ThrowAsync<NotFoundException>(
                $"{serviceType.Name}.{method.Name} must not reach a row this caller does not own — " +
                "and must say 'no such row', never 'not yours'");
        }
    }

    [Theory]
    [MemberData(nameof(RosterServices))]
    public async Task Every_method_that_addresses_a_row_reaches_it_when_unrestricted(Type serviceType)
    {
        foreach (var method in RowAddressingMethods(serviceType))
        {
            await using var world = await World.CreateAsync(OwnershipScope.Unrestricted);
            var act = async () => await world.InvokeAsync(world.Resolve(serviceType), method);

            // Keeps the refusal above honest: a service that refused everyone would pass it.
            await act.Should().NotThrowAsync<NotFoundException>(
                $"{serviceType.Name}.{method.Name} refused an unrestricted caller — the roster " +
                "would be invisible to staff and to every agent");
        }
    }

    /// <summary>
    /// Methods that name an existing row. A method with no id cannot address one (creating, listing
    /// the catalog), and <see cref="IExpertService.ListAsync"/>'s only parameter is a flag.
    /// </summary>
    private static List<MethodInfo> RowAddressingMethods(Type serviceType) =>
        serviceType.GetMethods()
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(Guid)))
            .OrderBy(m => m.Name)
            .ToList();

    /// <summary>
    /// One expert with one of everything, owned by somebody else, plus the container the services
    /// come out of. The scope is fixed per world, which is what lets the same invocation run once as
    /// a foreign owner and once unrestricted.
    /// </summary>
    private sealed class World : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly Dictionary<string, Guid> _ids;

        private World(ServiceProvider provider, Dictionary<string, Guid> ids)
        {
            _provider = provider;
            _ids = ids;
        }

        public static async Task<World> CreateAsync(OwnershipScope scope)
        {
            var dbName = $"ownership-{Guid.NewGuid()}";
            var services = new ServiceCollection();
            services.AddApplication();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
            services.AddScoped<Abstractions.IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
            services.AddSingleton<IOwnershipScopeProvider>(new FixedScope(scope));
            var provider = services.BuildServiceProvider();

            var db = provider.GetRequiredService<AppDbContext>();
            var ids = await SeedAsync(db);
            return new World(provider, ids);
        }

        public object Resolve(Type serviceType) => _provider.GetRequiredService(serviceType);

        /// <summary>Calls the method with this world's real ids, and a plausible payload for
        /// whatever else it asks for.</summary>
        public async Task InvokeAsync(object service, MethodInfo method)
        {
            var args = method.GetParameters().Select(Argument).ToArray();
            var result = method.Invoke(service, args)
                ?? throw new InvalidOperationException($"{method.Name} returned null, not a Task.");
            await (Task)result;
        }

        private object? Argument(ParameterInfo parameter)
        {
            if (parameter.ParameterType == typeof(CancellationToken))
            {
                return CancellationToken.None;
            }

            // A published notice version, because the compliance service validates the string it is
            // handed. "Some text" would fail validation rather than ownership, and a method that
            // threw for the wrong reason would look audited without being audited.
            if (parameter.ParameterType == typeof(string) && parameter.Name == "noticeVersion")
            {
                return TransparencyNotice.CurrentVersion;
            }

            if (parameter.ParameterType == typeof(Guid))
            {
                // An unknown id name is a failure, not a skip: a method nobody thought to seed for
                // is exactly the method that might be missing its ownership check.
                return _ids.TryGetValue(parameter.Name!, out var id)
                    ? id
                    : throw new InvalidOperationException(
                        $"No seeded row for the id parameter '{parameter.Name}'. Add one to " +
                        $"{nameof(World)}.{nameof(SeedAsync)} — leaving it out would silently drop " +
                        "a method from the ownership audit.");
            }

            return Payload(parameter.ParameterType);
        }

        private object Payload(Type type) => type switch
        {
            _ when type == typeof(string) => "Some text",
            _ when type == typeof(bool) => false,
            _ when type == typeof(ProcessingOrigin) => ProcessingOrigin.StaffCreated,
            _ when type == typeof(SaveSpokenLanguageDto) => new SaveSpokenLanguageDto("Welsh", LanguageLevel.Fluent),
            _ when type == typeof(SaveExpertSkillDto) => new SaveExpertSkillDto(_ids["skillId"], SkillLevel.Advanced, 3),
            _ when type == typeof(SaveQualificationDto) => new SaveQualificationDto(
                QualificationType.Degree, "BSc", null, null, null, null, null, null, null, null),
            _ when type == typeof(SaveAchievementDto) => new SaveAchievementDto(1, "Shipped something"),
            _ when type == typeof(SaveExperienceDto) => new SaveExperienceDto(
                "Acme", "Engineer", null, new DateOnly(2021, 1, 1), null, null, [], []),
            _ when type == typeof(SaveAvailabilityEntryDto) => new SaveAvailabilityEntryDto(new DateOnly(2030, 1, 1), 50),
            _ when type == typeof(SaveExpertDto) => new SaveExpertDto(
                "Ada", "Lovelace", "Engineer", $"ada-{Guid.NewGuid():N}@example.com", null, null, null, null),
            _ when type == typeof(UpdateExpertDto) => new UpdateExpertDto(
                "Ada", null, null, null, null, null, null, null),
            _ => throw new InvalidOperationException(
                $"The ownership audit does not know how to build a {type.Name}. Add it to " +
                $"{nameof(Payload)} rather than excluding the method."),
        };

        private static async Task<Dictionary<string, Guid>> SeedAsync(AppDbContext db)
        {
            var otherPerson = Guid.NewGuid();
            var category = new Category { Id = Guid.NewGuid(), Name = "Backend" };
            var skill = new Skill { Id = Guid.NewGuid(), Name = "C#", CategoryId = category.Id };
            var expert = new Expert
            {
                Id = Guid.NewGuid(),
                FirstName = "Grace",
                LastName = "Hopper",
                Title = "Engineer",
                Email = $"grace-{Guid.NewGuid():N}@example.com",
                // Owned by somebody — and in the restricted world, not by the caller.
                OwnerUserId = otherPerson,
            };
            var experience = new Experience
            {
                Id = Guid.NewGuid(),
                ExpertId = expert.Id,
                Company = "Univac",
                Title = "Engineer",
                StartDate = new DateOnly(2019, 1, 1),
            };
            var achievement = new Achievement
            {
                Id = Guid.NewGuid(), ExperienceId = experience.Id, Order = 1, Text = "Wrote a compiler",
            };
            var language = new SpokenLanguage
            {
                Id = Guid.NewGuid(), ExpertId = expert.Id, Language = "English", Level = LanguageLevel.Native,
            };
            var availability = new AvailabilityEntry
            {
                Id = Guid.NewGuid(), ExpertId = expert.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1), CapacityPercent = 100,
            };
            var expertSkill = new ExpertSkill
            {
                Id = Guid.NewGuid(), ExpertId = expert.Id, SkillId = skill.Id,
                Level = SkillLevel.Expert, YearsExperience = 10,
            };
            var qualification = new Qualification
            {
                Id = Guid.NewGuid(), ExpertId = expert.Id, Type = QualificationType.Degree, Name = "BSc",
            };
            var experienceSkill = new ExperienceSkill
            {
                Id = Guid.NewGuid(), ExperienceId = experience.Id, SkillId = skill.Id,
            };
            // Every roster row carries its lawful basis from the moment it exists (P1T-183), so a
            // fixture without one is not a roster row this audit should be reasoning about — and
            // the reads on IProcessingRecordService would 404 for the wrong reason.
            var basis = ProcessingRecord.For(
                expert.Id, sequence: 1, ProcessingOrigin.StaffCreated, noticeVersion: null,
                "Seeded by the ownership audit.", DateTimeOffset.UtcNow);

            db.Categories.Add(category);
            db.Skills.Add(skill);
            db.Experts.Add(expert);
            db.Experiences.Add(experience);
            db.Achievements.Add(achievement);
            db.SpokenLanguages.Add(language);
            db.AvailabilityEntries.Add(availability);
            db.ExpertSkills.Add(expertSkill);
            db.Qualifications.Add(qualification);
            db.ExperienceSkills.Add(experienceSkill);
            db.ProcessingRecords.Add(basis);
            await db.SaveChangesAsync();

            // Keyed by parameter name, because that is what the reflection has to go on.
            return new Dictionary<string, Guid>
            {
                ["id"] = expert.Id,
                ["expertId"] = expert.Id,
                ["experienceId"] = experience.Id,
                ["achievementId"] = achievement.Id,
                ["languageId"] = language.Id,
                ["entryId"] = availability.Id,
                ["expertSkillId"] = expertSkill.Id,
                ["qualificationId"] = qualification.Id,
                ["experienceSkillId"] = experienceSkill.Id,
                ["skillId"] = skill.Id,
                // Not a roster row: the Service Manager taking somebody's file on their
                // behalf (P1T-187). Seeded so the audit exercises that method too.
                ["staffUserId"] = Guid.NewGuid(),
            };
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }

    private sealed class FixedScope(OwnershipScope scope) : IOwnershipScopeProvider
    {
        public ValueTask<OwnershipScope> CurrentAsync(CancellationToken ct = default) => new(scope);
    }
}
