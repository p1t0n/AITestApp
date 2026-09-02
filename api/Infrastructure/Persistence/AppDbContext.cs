using ExpertToJob.Application.Abstractions;
using ExpertToJob.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Expert> Experts => Set<Expert>();
    public DbSet<SpokenLanguage> SpokenLanguages => Set<SpokenLanguage>();
    public DbSet<AvailabilityEntry> AvailabilityEntries => Set<AvailabilityEntry>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<ExpertSkill> ExpertSkills => Set<ExpertSkill>();
    public DbSet<Qualification> Qualifications => Set<Qualification>();
    public DbSet<Experience> Experiences => Set<Experience>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<ExperienceSkill> ExperienceSkills => Set<ExperienceSkill>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PasskeyCredential> PasskeyCredentials => Set<PasskeyCredential>();
    public DbSet<AgentUsage> AgentUsages => Set<AgentUsage>();
    public DbSet<StaffingProposal> StaffingProposals => Set<StaffingProposal>();
    public DbSet<StaffingProposalCandidate> StaffingProposalCandidates => Set<StaffingProposalCandidate>();
    public DbSet<ScoringJob> ScoringJobs => Set<ScoringJob>();
    public DbSet<ScoringJobCandidate> ScoringJobCandidates => Set<ScoringJobCandidate>();
    public DbSet<ExpertSearchChunk> ExpertSearchChunks => Set<ExpertSearchChunk>();
    public DbSet<ProcessingRecord> ProcessingRecords => Set<ProcessingRecord>();
    public DbSet<PendingClaim> PendingClaims => Set<PendingClaim>();
    public DbSet<ClaimCode> ClaimCodes => Set<ClaimCode>();
    public DbSet<DataExportRecord> DataExportRecords => Set<DataExportRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Semantic roster search stores embeddings via the pgvector extension. Guarded because
        // test/in-memory providers neither support the extension nor the Vector column type.
        var isNpgsql = Database.IsNpgsql();
        if (isNpgsql)
        {
            b.HasPostgresExtension("vector");
        }

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

        b.Entity<Expert>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            // Uniqueness binds only published experts with a real address: drafts may share an
            // email (re-ingested resume) or carry none at all — the promote gate resolves clashes.
            e.HasIndex(x => x.Email).IsUnique()
                .HasFilter("\"Status\" = 'Active' AND \"Email\" <> ''");
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Location).HasMaxLength(200);

            // One person, one row (P1T-182). Unique where the row is claimed, silent where it is
            // not — a filtered index is the only shape that expresses "at most one owner per user,
            // and any number of unclaimed rows". Database truth, not service convention.
            e.HasIndex(x => x.OwnerUserId).IsUnique()
                .HasFilter("\"OwnerUserId\" IS NOT NULL");
            // Deleting the account unclaims the row; it never deletes the CV. What happens to the
            // data itself on erasure is its own decision (P1T-186), taken deliberately elsewhere.
            e.HasOne<User>().WithMany()
                .HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);

            // Cascade, because deleting the row is erasure (P1T-186) — a different act from
            // rewriting the basis, which the append-only trigger below refuses outright.
            e.HasMany(x => x.ProcessingRecords).WithOne(x => x.Expert)
                .HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.SpokenLanguages).WithOne(x => x.Expert)
                .HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.AvailabilityEntries).WithOne(x => x.Expert)
                .HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Skills).WithOne(x => x.Expert)
                .HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Qualifications).WithOne(x => x.Expert)
                .HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Experiences).WithOne(x => x.Expert)
                .HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProcessingRecord>(e =>
        {
            e.Property(x => x.NoticeVersion).HasMaxLength(32);
            e.Property(x => x.Reason).HasMaxLength(400).IsRequired();
            // "Which basis is in force" has to have exactly one answer, and timestamps tie.
            e.HasIndex(x => new { x.ExpertId, x.Sequence }).IsUnique();

            if (isNpgsql)
            {
                // Basis is a function of origin (ProcessingRecord.BasisFor) and this is the database
                // saying so. It is what makes "no global default path exists" structural rather than
                // a promise: no service, no seeder, no hand-written INSERT and no future code path
                // can land a row on a ground its origin does not carry. Enums store as their names,
                // so the constraint reads as the table in manuals/gdpr-processing-basis.md does.
                e.ToTable(t => t.HasCheckConstraint(
                    "CK_ProcessingRecords_BasisMatchesOrigin",
                    "(\"Origin\" = 'SelfRegistered' AND \"Basis\" = 'ContractNecessity') "
                    + "OR (\"Origin\" = 'StaffCreated' AND \"Basis\" = 'LegitimateInterest')"));
            }
        });

        b.Entity<PendingClaim>(e =>
        {
            e.Property(x => x.ClaimantEmail).HasMaxLength(256).IsRequired();

            e.HasOne(x => x.Claimant).WithMany()
                .HasForeignKey(x => x.ClaimantUserId).OnDelete(DeleteBehavior.Cascade);
            // Deleting the row takes its claim history with it — erasure (P1T-186) removes the
            // request along with the data it was a request for.
            e.HasOne(x => x.Expert).WithMany()
                .HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);

            // One open claim per person, and one open claim per row. Both are database truths for
            // the same reason the owner index is: a second approval racing the first would bind a
            // row twice, and "at most one" enforced only in a service is enforced only until the
            // next code path forgets. Filtered, because resolved rows are kept forever.
            e.HasIndex(x => x.ClaimantUserId).IsUnique()
                .HasFilter("\"State\" = 'Pending'");
            e.HasIndex(x => x.ExpertId).IsUnique()
                .HasFilter("\"State\" = 'Pending'");
        });

        b.Entity<ClaimCode>(e =>
        {
            e.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
            // The lookup a redemption does. Unique because a hash collision here would redeem the
            // wrong row, and because two identical codes cannot both be single-use.
            e.HasIndex(x => x.CodeHash).IsUnique();

            e.HasOne(x => x.Expert).WithMany()
                .HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DataExportRecord>(e =>
        {
            // Cascades with the Expert: after erasure there is no file to have taken, and the row
            // would be a bare reference to somebody we no longer hold (P1T-187).
            e.HasOne(x => x.Expert).WithMany()
                .HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);
            // The staff member's account can go without taking the fact of the export with it.
            e.HasOne<User>().WithMany()
                .HasForeignKey(x => x.ExportedByUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.ExpertId);
        });

        b.Entity<SpokenLanguage>(e =>
        {
            e.Property(x => x.Language).HasMaxLength(100).IsRequired();
            e.Property(x => x.Level).HasMaxLength(20);
        });

        b.Entity<AvailabilityEntry>(e =>
        {
            e.HasIndex(x => new { x.ExpertId, x.EffectiveFrom }).IsUnique();
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

        b.Entity<ExpertSkill>(e =>
        {
            e.Property(x => x.Level).HasMaxLength(20);
            e.Property(x => x.YearsExperience).HasPrecision(4, 1);
            e.HasIndex(x => new { x.ExpertId, x.SkillId }).IsUnique();
            e.HasOne(x => x.Skill).WithMany(x => x.ExpertSkills)
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
            e.Property(x => x.AcknowledgedNoticeVersion).HasMaxLength(32);
            // ServiceManager is the store default because a row written without a role predates
            // the split, and every account from then was staff. EF writes new accounts explicitly.
            e.Property(x => x.Role).HasMaxLength(30).IsRequired()
                .HasDefaultValue(Domain.Enums.UserRole.ServiceManager);
            // Session generation starts at 1 so "absent" and "first" stay distinguishable.
            e.Property(x => x.TokenVersion).HasDefaultValue(1);

            e.HasMany(x => x.Passkeys).WithOne(x => x.User)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PasskeyCredential>(e =>
        {
            e.HasIndex(x => x.CredentialId).IsUnique();
            e.Property(x => x.Transports).HasMaxLength(200);
            e.Property(x => x.Label).HasMaxLength(200);
        });

        b.Entity<AgentUsage>(e =>
        {
            e.Property(x => x.AgentName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Model).HasMaxLength(200);
            // Long enough for a pathological loop's full sequence (the worst run behind P1T-144
            // called 9 tools); truncating it would defeat the point of recording it.
            e.Property(x => x.ToolSequence).HasMaxLength(2000);
            // Window aggregation always filters by user + time range.
            e.HasIndex(x => new { x.UserId, x.Timestamp });
            e.HasOne<User>().WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StaffingProposal>(e =>
        {
            e.Property(x => x.JobDescription).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.DecisionNote).HasMaxLength(2000);
            if (isNpgsql)
            {
                e.Property(x => x.PackageJson).HasColumnType("jsonb");
            }

            // The approval inbox lists pending proposals newest-first.
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasMany(x => x.Candidates).WithOne()
                .HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Cascade);
            // Proposals outlive their users: the decision ledger keeps rows, the reference clears.
            e.HasOne<User>().WithMany()
                .HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany()
                .HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<StaffingProposalCandidate>(e =>
        {
            // Deliberately no foreign key to Expert (P1T-186). This row records a decision a human
            // made, so it has to outlive the person: a cascade would delete the decision and a
            // restrict would block the erasure. The surviving ExpertId is a restricted-processing
            // reference under Art. 18, not a link — pseudonymisation, and not something to call
            // anonymous.
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.MatchBand).HasMaxLength(50);
            e.Property(x => x.Rationale).IsRequired();
        });

        b.Entity<ScoringJob>(e =>
        {
            e.Property(x => x.JobDescription).IsRequired();
            e.Property(x => x.State).HasMaxLength(20).IsRequired();
            e.Property(x => x.PauseReason).HasMaxLength(20);
            e.Property(x => x.FailureDetail).HasMaxLength(2000);
            // The resume timer scans for due paused jobs; startup recovery scans for orphans.
            e.HasIndex(x => new { x.State, x.ResumeAt });
            // The polling list is per requester, newest-first.
            e.HasIndex(x => new { x.RequestedByUserId, x.CreatedAt });
            e.HasMany(x => x.Candidates).WithOne()
                .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            // Jobs outlive their users, like the proposal ledger.
            e.HasOne<User>().WithMany()
                .HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);

            if (isNpgsql)
            {
                e.Property(x => x.ExtractionJson).HasColumnType("jsonb");
                e.Property(x => x.FiltersJson).HasColumnType("jsonb");
            }
        });

        b.Entity<ScoringJobCandidate>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.Band).HasMaxLength(50);
            e.Property(x => x.Error).HasMaxLength(2000);
            // Progress counts group by status within a job; chunk writes look rows up per job.
            e.HasIndex(x => new { x.JobId, x.Status });

            // A scan candidate carries the person's name, title, whole career digest and a
            // model-written rationale, and until P1T-186 it had no foreign key at all — so erasing
            // an Expert left every one of those rows behind, and nothing in the schema noticed.
            // Cascade, because a scan is a working artefact rather than a decision: there is
            // nothing here worth keeping once the person it describes is gone.
            e.HasOne<Expert>().WithMany()
                .HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExpertSearchChunk>(e =>
        {
            e.Property(x => x.SourceType).HasMaxLength(20);
            e.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Model).HasMaxLength(200);

            if (isNpgsql)
            {
                e.Property(x => x.Embedding).HasColumnType("vector(1536)");
            }
            else
            {
                // The pgvector Vector CLR type has no mapping under the in-memory test provider.
                e.Ignore(x => x.Embedding);
            }

            // One chunk per source row; also the reconciler's upsert/lookup key.
            e.HasIndex(x => new { x.SourceType, x.SourceId }).IsUnique();
            // Pre-filter and aggregation always scope by expert.
            e.HasIndex(x => x.ExpertId);

            e.HasOne<Expert>().WithMany()
                .HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
