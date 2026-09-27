using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The embeddings provider history (EXP-66), and its one seeded row.
    ///
    /// <para>Every deployment that predates this table was on Gemini from its first Expert, so the
    /// seed opens a Gemini period with <b>no start date</b> — "since the beginning". Stamping the
    /// migration's own run time instead would claim a start nobody knows, and would then tell every
    /// person whose record predates this migration that their narrative was sent to nobody until
    /// today. The row is open (<c>EndedAt</c> null) until a host says otherwise.</para>
    ///
    /// <para>The fixed id is deliberate: re-running the seed can only ever collide, never duplicate.</para>
    /// </summary>
    public partial class EmbeddingsProviderPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmbeddingsProviderPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmbeddingsProviderPeriods", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "EmbeddingsProviderPeriods",
                columns: ["Id", "Provider", "StartedAt", "EndedAt"],
                values: [
                    new Guid("6a1f5a1e-6e2b-4f2e-9a3a-1d5b8f0e66a1"), "Gemini", null, null
                ]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmbeddingsProviderPeriods");
        }
    }
}
