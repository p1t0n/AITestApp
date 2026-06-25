using EmployeeManager.Application.Abstractions;
using EmployeeManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManager.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<SpokenLanguage> SpokenLanguages => Set<SpokenLanguage>();
    public DbSet<AvailabilityEntry> AvailabilityEntries => Set<AvailabilityEntry>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<EmployeeSkill> EmployeeSkills => Set<EmployeeSkill>();
    public DbSet<Qualification> Qualifications => Set<Qualification>();
    public DbSet<Experience> Experiences => Set<Experience>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<ExperienceSkill> ExperienceSkills => Set<ExperienceSkill>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PasskeyCredential> PasskeyCredentials => Set<PasskeyCredential>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Store all enums as readable strings (better for inspection + future AI consumers).
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                if (prop.ClrType.IsEnum || (Nullable.GetUnderlyingType(prop.ClrType)?.IsEnum ?? false))
                {
                    prop.SetProviderClrType(typeof(string));
                }
            }
        }

        b.Entity<Employee>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Location).HasMaxLength(200);

            e.HasMany(x => x.SpokenLanguages).WithOne(x => x.Employee)
                .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.AvailabilityEntries).WithOne(x => x.Employee)
                .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Skills).WithOne(x => x.Employee)
                .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Qualifications).WithOne(x => x.Employee)
                .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Experiences).WithOne(x => x.Employee)
                .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SpokenLanguage>(e =>
        {
            e.Property(x => x.Language).HasMaxLength(100).IsRequired();
            e.Property(x => x.Level).HasMaxLength(20);
        });

        b.Entity<AvailabilityEntry>(e =>
        {
            e.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom }).IsUnique();
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.HasOne(x => x.Parent).WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Skills).WithOne(x => x.Category)
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Skill>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Rank).HasDefaultValue(0);
            // Skill names are unique per category (case-insensitive), enforced by a functional
            // index created in raw SQL in the CatalogUniqueIndexes migration — not globally unique.
        });

        b.Entity<EmployeeSkill>(e =>
        {
            e.Property(x => x.Level).HasMaxLength(20);
            e.Property(x => x.YearsExperience).HasPrecision(4, 1);
            e.HasIndex(x => new { x.EmployeeId, x.SkillId }).IsUnique();
            e.HasOne(x => x.Skill).WithMany(x => x.EmployeeSkills)
                .HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Qualification>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(250).IsRequired();
            e.Property(x => x.Institution).HasMaxLength(250);
            e.Property(x => x.Field).HasMaxLength(200);
            e.Property(x => x.Issuer).HasMaxLength(250);
            e.Property(x => x.CredentialId).HasMaxLength(150);
        });

        b.Entity<Experience>(e =>
        {
            e.Property(x => x.Company).HasMaxLength(200).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Location).HasMaxLength(200);
            e.HasMany(x => x.Achievements).WithOne(x => x.Experience)
                .HasForeignKey(x => x.ExperienceId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Skills).WithOne(x => x.Experience)
                .HasForeignKey(x => x.ExperienceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Achievement>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(1000).IsRequired();
        });

        b.Entity<ExperienceSkill>(e =>
        {
            e.HasIndex(x => new { x.ExperienceId, x.SkillId }).IsUnique();
            e.HasOne(x => x.Skill).WithMany()
                .HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.ControlWordHash).HasMaxLength(512).IsRequired();

            e.HasMany(x => x.Passkeys).WithOne(x => x.User)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PasskeyCredential>(e =>
        {
            e.HasIndex(x => x.CredentialId).IsUnique();
            e.Property(x => x.Transports).HasMaxLength(200);
            e.Property(x => x.Label).HasMaxLength(200);
        });
    }
}
