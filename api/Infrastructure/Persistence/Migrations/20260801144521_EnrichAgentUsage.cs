using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnrichAgentUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LatencyMs",
                table: "AgentUsages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Step",
                table: "AgentUsages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "AgentUsages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LatencyMs",
                table: "AgentUsages");

            migrationBuilder.DropColumn(
                name: "Step",
                table: "AgentUsages");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "AgentUsages");
        }
    }
}
