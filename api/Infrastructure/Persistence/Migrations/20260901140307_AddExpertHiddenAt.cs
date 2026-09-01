using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpertHiddenAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HiddenAt",
                table: "Experts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HiddenAt",
                table: "Experts");
        }
    }
}
