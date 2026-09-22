using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DashboardBackground : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "background_fit",
                table: "dashboards",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "cover");

            migrationBuilder.AddColumn<string>(
                name: "background_image",
                table: "dashboards",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "background_opacity",
                table: "dashboards",
                type: "integer",
                nullable: false,
                defaultValue: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "background_fit",
                table: "dashboards");

            migrationBuilder.DropColumn(
                name: "background_image",
                table: "dashboards");

            migrationBuilder.DropColumn(
                name: "background_opacity",
                table: "dashboards");
        }
    }
}
