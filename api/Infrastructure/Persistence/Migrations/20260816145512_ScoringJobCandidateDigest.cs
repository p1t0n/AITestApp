using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScoringJobCandidateDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Digest",
                table: "ScoringJobCandidates",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Digest",
                table: "ScoringJobCandidates");
        }
    }
}
