using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameEmployeeToExpert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AvailabilityEntries_Employees_EmployeeId",
                table: "AvailabilityEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Experiences_Employees_EmployeeId",
                table: "Experiences");

            migrationBuilder.DropForeignKey(
                name: "FK_Qualifications_Employees_EmployeeId",
                table: "Qualifications");

            migrationBuilder.DropForeignKey(
                name: "FK_SpokenLanguages_Employees_EmployeeId",
                table: "SpokenLanguages");

            migrationBuilder.DropTable(
                name: "EmployeeSearchChunks");

            migrationBuilder.DropTable(
                name: "EmployeeSkills");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.RenameColumn(
                name: "RecommendedEmployeeId",
                table: "StaffingProposals",
                newName: "RecommendedExpertId");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "StaffingProposalCandidates",
                newName: "ExpertId");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "SpokenLanguages",
                newName: "ExpertId");

            migrationBuilder.RenameIndex(
                name: "IX_SpokenLanguages_EmployeeId",
                table: "SpokenLanguages",
                newName: "IX_SpokenLanguages_ExpertId");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "ScoringJobCandidates",
                newName: "ExpertId");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "Qualifications",
                newName: "ExpertId");

            migrationBuilder.RenameIndex(
                name: "IX_Qualifications_EmployeeId",
                table: "Qualifications",
                newName: "IX_Qualifications_ExpertId");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "Experiences",
                newName: "ExpertId");

            migrationBuilder.RenameIndex(
                name: "IX_Experiences_EmployeeId",
                table: "Experiences",
                newName: "IX_Experiences_ExpertId");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "AvailabilityEntries",
                newName: "ExpertId");

            migrationBuilder.RenameIndex(
                name: "IX_AvailabilityEntries_EmployeeId_EffectiveFrom",
                table: "AvailabilityEntries",
                newName: "IX_AvailabilityEntries_ExpertId_EffectiveFrom");

            migrationBuilder.CreateTable(
                name: "Experts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    PhotoUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Experts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertSearchChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpertId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EmbeddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertSearchChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpertSearchChunks_Experts_ExpertId",
                        column: x => x.ExpertId,
                        principalTable: "Experts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpertSkills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpertId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    YearsExperience = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpertSkills_Experts_ExpertId",
                        column: x => x.ExpertId,
                        principalTable: "Experts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpertSkills_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Experts_Email",
                table: "Experts",
                column: "Email",
                unique: true,
                filter: "\"Status\" = 'Active' AND \"Email\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertSearchChunks_ExpertId",
                table: "ExpertSearchChunks",
                column: "ExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertSearchChunks_SourceType_SourceId",
                table: "ExpertSearchChunks",
                columns: new[] { "SourceType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpertSkills_ExpertId_SkillId",
                table: "ExpertSkills",
                columns: new[] { "ExpertId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpertSkills_SkillId",
                table: "ExpertSkills",
                column: "SkillId");

            migrationBuilder.AddForeignKey(
                name: "FK_AvailabilityEntries_Experts_ExpertId",
                table: "AvailabilityEntries",
                column: "ExpertId",
                principalTable: "Experts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Experiences_Experts_ExpertId",
                table: "Experiences",
                column: "ExpertId",
                principalTable: "Experts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Qualifications_Experts_ExpertId",
                table: "Qualifications",
                column: "ExpertId",
                principalTable: "Experts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SpokenLanguages_Experts_ExpertId",
                table: "SpokenLanguages",
                column: "ExpertId",
                principalTable: "Experts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AvailabilityEntries_Experts_ExpertId",
                table: "AvailabilityEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Experiences_Experts_ExpertId",
                table: "Experiences");

            migrationBuilder.DropForeignKey(
                name: "FK_Qualifications_Experts_ExpertId",
                table: "Qualifications");

            migrationBuilder.DropForeignKey(
                name: "FK_SpokenLanguages_Experts_ExpertId",
                table: "SpokenLanguages");

            migrationBuilder.DropTable(
                name: "ExpertSearchChunks");

            migrationBuilder.DropTable(
                name: "ExpertSkills");

            migrationBuilder.DropTable(
                name: "Experts");

            migrationBuilder.RenameColumn(
                name: "RecommendedExpertId",
                table: "StaffingProposals",
                newName: "RecommendedEmployeeId");

            migrationBuilder.RenameColumn(
                name: "ExpertId",
                table: "StaffingProposalCandidates",
                newName: "EmployeeId");

            migrationBuilder.RenameColumn(
                name: "ExpertId",
                table: "SpokenLanguages",
                newName: "EmployeeId");

            migrationBuilder.RenameIndex(
                name: "IX_SpokenLanguages_ExpertId",
                table: "SpokenLanguages",
                newName: "IX_SpokenLanguages_EmployeeId");

            migrationBuilder.RenameColumn(
                name: "ExpertId",
                table: "ScoringJobCandidates",
                newName: "EmployeeId");

            migrationBuilder.RenameColumn(
                name: "ExpertId",
                table: "Qualifications",
                newName: "EmployeeId");

            migrationBuilder.RenameIndex(
                name: "IX_Qualifications_ExpertId",
                table: "Qualifications",
                newName: "IX_Qualifications_EmployeeId");

            migrationBuilder.RenameColumn(
                name: "ExpertId",
                table: "Experiences",
                newName: "EmployeeId");

            migrationBuilder.RenameIndex(
                name: "IX_Experiences_ExpertId",
                table: "Experiences",
                newName: "IX_Experiences_EmployeeId");

            migrationBuilder.RenameColumn(
                name: "ExpertId",
                table: "AvailabilityEntries",
                newName: "EmployeeId");

            migrationBuilder.RenameIndex(
                name: "IX_AvailabilityEntries_ExpertId_EffectiveFrom",
                table: "AvailabilityEntries",
                newName: "IX_AvailabilityEntries_EmployeeId_EffectiveFrom");

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PhotoUrl = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeSearchChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmbeddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSearchChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeSearchChunks_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeSkills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    YearsExperience = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeSkills_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeSkills_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Email",
                table: "Employees",
                column: "Email",
                unique: true,
                filter: "\"Status\" = 'Active' AND \"Email\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSearchChunks_EmployeeId",
                table: "EmployeeSearchChunks",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSearchChunks_SourceType_SourceId",
                table: "EmployeeSearchChunks",
                columns: new[] { "SourceType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSkills_EmployeeId_SkillId",
                table: "EmployeeSkills",
                columns: new[] { "EmployeeId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSkills_SkillId",
                table: "EmployeeSkills",
                column: "SkillId");

            migrationBuilder.AddForeignKey(
                name: "FK_AvailabilityEntries_Employees_EmployeeId",
                table: "AvailabilityEntries",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Experiences_Employees_EmployeeId",
                table: "Experiences",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Qualifications_Employees_EmployeeId",
                table: "Qualifications",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SpokenLanguages_Employees_EmployeeId",
                table: "SpokenLanguages",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
