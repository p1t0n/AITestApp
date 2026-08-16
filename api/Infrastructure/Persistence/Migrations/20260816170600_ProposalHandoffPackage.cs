using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProposalHandoffPackage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PackageJson",
                table: "StaffingProposals",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackageJson",
                table: "StaffingProposals");
        }
    }
}
