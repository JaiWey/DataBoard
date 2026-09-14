using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardRefreshSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "last_refresh_failures",
                table: "dashboards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "last_refresh_message",
                table: "dashboards",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_refresh_utc",
                table: "dashboards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_refresh_utc",
                table: "dashboards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "refresh_interval_minutes",
                table: "dashboards",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_dashboards_next_refresh_utc",
                table: "dashboards",
                column: "next_refresh_utc",
                filter: "refresh_interval_minutes IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_dashboards_next_refresh_utc",
                table: "dashboards");

            migrationBuilder.DropColumn(
                name: "last_refresh_failures",
                table: "dashboards");

            migrationBuilder.DropColumn(
                name: "last_refresh_message",
                table: "dashboards");

            migrationBuilder.DropColumn(
                name: "last_refresh_utc",
                table: "dashboards");

            migrationBuilder.DropColumn(
                name: "next_refresh_utc",
                table: "dashboards");

            migrationBuilder.DropColumn(
                name: "refresh_interval_minutes",
                table: "dashboards");
        }
    }
}
