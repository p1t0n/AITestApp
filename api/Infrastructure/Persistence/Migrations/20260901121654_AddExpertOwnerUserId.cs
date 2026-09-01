using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpertOwnerUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "Experts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Experts_OwnerUserId",
                table: "Experts",
                column: "OwnerUserId",
                unique: true,
                filter: "\"OwnerUserId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Experts_Users_OwnerUserId",
                table: "Experts",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Experts_Users_OwnerUserId",
                table: "Experts");

            migrationBuilder.DropIndex(
                name: "IX_Experts_OwnerUserId",
                table: "Experts");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Experts");
        }
    }
}
