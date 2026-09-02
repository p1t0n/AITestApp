using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpertLastActivityAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastActivityAt",
                table: "Experts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "Experts");
        }
    }
}
