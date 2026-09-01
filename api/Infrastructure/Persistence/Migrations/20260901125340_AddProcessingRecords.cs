using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedNoticeVersion",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NoticeAcknowledgedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProcessingRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpertId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    Basis = table.Column<string>(type: "text", nullable: false),
                    NoticeVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingRecords", x => x.Id);
                    table.CheckConstraint("CK_ProcessingRecords_BasisMatchesOrigin", "(\"Origin\" = 'SelfRegistered' AND \"Basis\" = 'ContractNecessity') OR (\"Origin\" = 'StaffCreated' AND \"Basis\" = 'LegitimateInterest')");
                    table.ForeignKey(
                        name: "FK_ProcessingRecords_Experts_ExpertId",
                        column: x => x.ExpertId,
                        principalTable: "Experts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingRecords_ExpertId_Sequence",
                table: "ProcessingRecords",
                columns: new[] { "ExpertId", "Sequence" },
                unique: true);

            // Append-only, as database truth rather than service convention (P1T-183). A lawful
            // basis is superseded by a new row, never rewritten — EDPB GL 05/2020 §123 makes the
            // history the artefact, and "we were on legitimate interest until March" is a fact with
            // consequences (no Art. 22(2) route in that window) that an UPDATE would quietly erase.
            //
            // UPDATE only. DELETE stays open because deleting is erasure (P1T-186) and because the
            // Expert cascade has to be able to take these rows with it; refusing it here would make
            // a person's right to erasure depend on a trigger written for a different purpose.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION refuse_processing_record_update() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        'ProcessingRecords are append-only: supersede a lawful basis with a new row, never rewrite one.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER "ProcessingRecords_append_only"
                    BEFORE UPDATE ON "ProcessingRecords"
                    FOR EACH ROW EXECUTE FUNCTION refuse_processing_record_update();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "ProcessingRecords_append_only" ON "ProcessingRecords";
                DROP FUNCTION IF EXISTS refuse_processing_record_update();
                """);

            migrationBuilder.DropTable(
                name: "ProcessingRecords");

            migrationBuilder.DropColumn(
                name: "AcknowledgedNoticeVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NoticeAcknowledgedAt",
                table: "Users");
        }
    }
}
