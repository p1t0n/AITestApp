using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgentUsageIterationsAndToolSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Iterations",
                table: "AgentUsages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolSequence",
                table: "AgentUsages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Iterations",
                table: "AgentUsages");

            migrationBuilder.DropColumn(
                name: "ToolSequence",
                table: "AgentUsages");
        }
    }
}
