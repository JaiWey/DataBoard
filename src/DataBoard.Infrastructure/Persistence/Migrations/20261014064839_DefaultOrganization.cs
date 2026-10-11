using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DefaultOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_organizations_IsDefault",
                table: "organizations",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\"");

            // The default organization every user belongs to, with everyone already registered in it: the oldest account owns it,
            // the rest are Editors. Existing organizations and their data are left as they are.
            migrationBuilder.Sql("""
                INSERT INTO organizations ("Id", "Name", "CreatedUtc", "IsDefault")
                VALUES (gen_random_uuid(), 'Default', now(), true);

                INSERT INTO organization_members ("OrganizationId", "UserId", "Role", "JoinedUtc")
                SELECT (SELECT "Id" FROM organizations WHERE "IsDefault"), u."Id",
                       CASE WHEN u."Id" = (SELECT "Id" FROM users ORDER BY "CreatedUtc", "Id" LIMIT 1) THEN 'Owner' ELSE 'Editor' END,
                       now()
                FROM users u;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Members of the default organization go with it (the foreign key cascades). Anything made in it is not removed:
            // it must be moved or deleted first.
            migrationBuilder.Sql("""DELETE FROM organizations WHERE "IsDefault";""");

            migrationBuilder.DropIndex(
                name: "IX_organizations_IsDefault",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "organizations");
        }
    }
}
