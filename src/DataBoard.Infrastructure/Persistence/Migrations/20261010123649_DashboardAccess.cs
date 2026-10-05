using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DashboardAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "access_member_ids",
                table: "folders",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<int>(
                name: "access_min_role",
                table: "folders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "access_member_ids",
                table: "dashboards",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<int>(
                name: "access_min_role",
                table: "dashboards",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "access_member_ids",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "access_min_role",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "access_member_ids",
                table: "dashboards");

            migrationBuilder.DropColumn(
                name: "access_min_role",
                table: "dashboards");
        }
    }
}
