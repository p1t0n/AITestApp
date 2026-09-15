using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpertToJob.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// <c>UserRole { ServiceManager, Expert }</c> becomes <c>{ Administrator, User }</c> (P1T-236).
    /// Roles are persisted by name, so the enum rename is a data migration: without this, every
    /// existing account carries a string the enum no longer has and cannot be materialised at all.
    ///
    /// <para>Order matters and is the point of writing this by hand. The rows are rewritten first,
    /// so nothing is ever read against a role the app does not know; the column default goes next,
    /// because <c>'ServiceManager'</c> spoke for pre-split rows and past the rename would turn an
    /// omitted role into the most privileged account there is; the <c>CHECK</c> follows, as the
    /// backstop for the raw SQL that bypasses the enum entirely (the e2e seed, a fix-up script);
    /// and the token-version bump comes last, so no session is minted against a half-renamed row.
    /// Everybody signs in again once.</para>
    ///
    /// <para>One transaction: EF wraps a migration in one, and every statement here is inside it.
    /// A partial application would leave the two halves of one fact disagreeing.</para>
    /// </summary>
    /// <inheritdoc />
    public partial class RenameRolesToAdministratorAndUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE "Users" SET "Role" = 'Administrator' WHERE "Role" = 'ServiceManager';""");
            migrationBuilder.Sql(
                """UPDATE "Users" SET "Role" = 'User' WHERE "Role" = 'Expert';""");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "ServiceManager");

            migrationBuilder.Sql(
                """
                ALTER TABLE "Users"
                ADD CONSTRAINT "CK_Users_Role" CHECK ("Role" IN ('Administrator','User'));
                """);

            migrationBuilder.Sql("""UPDATE "Users" SET "TokenVersion" = "TokenVersion" + 1;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "Users" DROP CONSTRAINT "CK_Users_Role";""");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ServiceManager",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.Sql(
                """UPDATE "Users" SET "Role" = 'ServiceManager' WHERE "Role" = 'Administrator';""");
            migrationBuilder.Sql(
                """UPDATE "Users" SET "Role" = 'Expert' WHERE "Role" = 'User';""");

            // Rolling back is as much a role change as rolling forward: the tokens minted while the
            // new names were live claim roles the rolled-back app does not have.
            migrationBuilder.Sql("""UPDATE "Users" SET "TokenVersion" = "TokenVersion" + 1;""");
        }
    }
}
