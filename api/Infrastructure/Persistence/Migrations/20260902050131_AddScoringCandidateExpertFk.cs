using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScoringCandidateExpertFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The rows this foreign key exists to catch are already in the table: every Expert
            // deleted before P1T-186 left their name, title, career digest and rationale behind
            // here, pointing at an id that no longer resolves. They have to go before the
            // constraint can be created — and they had to go regardless, which is the whole point.
            migrationBuilder.Sql(
                """
                DELETE FROM "ScoringJobCandidates"
                WHERE "ExpertId" NOT IN (SELECT "Id" FROM "Experts");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ScoringJobCandidates_ExpertId",
                table: "ScoringJobCandidates",
                column: "ExpertId");

            migrationBuilder.AddForeignKey(
                name: "FK_ScoringJobCandidates_Experts_ExpertId",
                table: "ScoringJobCandidates",
                column: "ExpertId",
                principalTable: "Experts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScoringJobCandidates_Experts_ExpertId",
                table: "ScoringJobCandidates");

            migrationBuilder.DropIndex(
                name: "IX_ScoringJobCandidates_ExpertId",
                table: "ScoringJobCandidates");
        }
    }
}
