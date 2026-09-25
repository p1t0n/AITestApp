using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RosterQaConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RosterQaConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterQaConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RosterQaConversations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RosterQaTurns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionText = table.Column<string>(type: "text", nullable: false),
                    AnswerText = table.Column<string>(type: "text", nullable: false),
                    ModelId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Grounded = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterQaTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RosterQaTurns_RosterQaConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "RosterQaConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RosterQaTurnExperts",
                columns: table => new
                {
                    TurnId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpertId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterQaTurnExperts", x => new { x.TurnId, x.ExpertId });
                    table.ForeignKey(
                        name: "FK_RosterQaTurnExperts_RosterQaTurns_TurnId",
                        column: x => x.TurnId,
                        principalTable: "RosterQaTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RosterQaConversations_UserId_LastActiveAt",
                table: "RosterQaConversations",
                columns: new[] { "UserId", "LastActiveAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RosterQaTurnExperts_ExpertId",
                table: "RosterQaTurnExperts",
                column: "ExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterQaTurns_ConversationId_CreatedAt",
                table: "RosterQaTurns",
                columns: new[] { "ConversationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RosterQaTurnExperts");

            migrationBuilder.DropTable(
                name: "RosterQaTurns");

            migrationBuilder.DropTable(
                name: "RosterQaConversations");
        }
    }
}
