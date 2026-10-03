using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DashboardShareOrganizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "AllowedOrganizationIds",
                table: "dashboard_shares",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedOrganizationIds",
                table: "dashboard_shares");
        }
    }
}
