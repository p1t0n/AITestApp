using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffingProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffingProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobDescription = table.Column<string>(type: "text", nullable: false),
                    RecommendedEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportDegraded = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffingProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffingProposals_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StaffingProposals_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StaffingProposalCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    MatchScore = table.Column<int>(type: "integer", nullable: true),
                    MatchBand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Rationale = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffingProposalCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffingProposalCandidates_StaffingProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "StaffingProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffingProposalCandidates_ProposalId",
                table: "StaffingProposalCandidates",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffingProposals_DecidedByUserId",
                table: "StaffingProposals",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffingProposals_RequestedByUserId",
                table: "StaffingProposals",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffingProposals_Status_CreatedAt",
                table: "StaffingProposals",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffingProposalCandidates");

            migrationBuilder.DropTable(
                name: "StaffingProposals");
        }
    }
}
