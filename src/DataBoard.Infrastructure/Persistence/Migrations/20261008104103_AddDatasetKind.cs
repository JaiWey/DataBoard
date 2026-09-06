using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDatasetKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "datasets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Sql");

            migrationBuilder.AddColumn<string>(
                name: "definition",
                table: "datasets",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "definition",
                table: "datasets");
        }
    }
}
