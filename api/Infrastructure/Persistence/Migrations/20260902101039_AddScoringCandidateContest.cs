using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScoringCandidateContest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContestNote",
                table: "ScoringJobCandidates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContestOutcome",
                table: "ScoringJobCandidates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContestResponse",
                table: "ScoringJobCandidates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContestReviewedAt",
                table: "ScoringJobCandidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContestReviewedByUserId",
                table: "ScoringJobCandidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContestedAt",
                table: "ScoringJobCandidates",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContestNote",
                table: "ScoringJobCandidates");

            migrationBuilder.DropColumn(
                name: "ContestOutcome",
                table: "ScoringJobCandidates");

            migrationBuilder.DropColumn(
                name: "ContestResponse",
                table: "ScoringJobCandidates");

            migrationBuilder.DropColumn(
                name: "ContestReviewedAt",
                table: "ScoringJobCandidates");

            migrationBuilder.DropColumn(
                name: "ContestReviewedByUserId",
                table: "ScoringJobCandidates");

            migrationBuilder.DropColumn(
                name: "ContestedAt",
                table: "ScoringJobCandidates");
        }
    }
}
