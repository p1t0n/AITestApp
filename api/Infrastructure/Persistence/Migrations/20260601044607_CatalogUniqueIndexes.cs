using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmployeeManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replace the old global unique index on skill name with case-insensitive,
            // category-scoped uniqueness so the same skill name can exist in different categories.
            migrationBuilder.DropIndex(
                name: "IX_Skills_Name",
                table: "Skills");

            // Functional lower() indexes can't be expressed via EF fluent config, hence raw SQL
            // (not tracked in the model snapshot). NULLS NOT DISTINCT (PG15+) makes two root
            // categories sharing a name collide despite ParentId being NULL.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_Categories_ParentId_NameCI\" " +
                "ON \"Categories\" (\"ParentId\", lower(\"Name\")) NULLS NOT DISTINCT;");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_Skills_CategoryId_NameCI\" " +
                "ON \"Skills\" (\"CategoryId\", lower(\"Name\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX \"IX_Skills_CategoryId_NameCI\";");
            migrationBuilder.Sql("DROP INDEX \"IX_Categories_ParentId_NameCI\";");

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Name",
                table: "Skills",
                column: "Name",
                unique: true);
        }
    }
}
