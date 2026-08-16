using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScoringJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScoringJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobDescription = table.Column<string>(type: "text", nullable: false),
                    ExtractionJson = table.Column<string>(type: "jsonb", nullable: true),
                    FiltersJson = table.Column<string>(type: "jsonb", nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PauseReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ResumeAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ChunkSize = table.Column<int>(type: "integer", nullable: false),
                    FailureDetail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoringJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoringJobs_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ScoringJobCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    Band = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Rationale = table.Column<string>(type: "text", nullable: true),
                    Scorable = table.Column<bool>(type: "boolean", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoringJobCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoringJobCandidates_ScoringJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ScoringJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScoringJobCandidates_JobId_Status",
                table: "ScoringJobCandidates",
                columns: new[] { "JobId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScoringJobs_RequestedByUserId_CreatedAt",
                table: "ScoringJobs",
                columns: new[] { "RequestedByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScoringJobs_State_ResumeAt",
                table: "ScoringJobs",
                columns: new[] { "State", "ResumeAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScoringJobCandidates");

            migrationBuilder.DropTable(
                name: "ScoringJobs");
        }
    }
}
