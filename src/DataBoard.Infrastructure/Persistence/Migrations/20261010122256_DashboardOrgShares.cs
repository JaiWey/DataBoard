using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DashboardOrgShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dashboard_org_shares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DashboardId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SharedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dashboard_org_shares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dashboard_org_shares_dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dashboard_org_shares_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dashboard_org_shares_organizations_TargetOrganizationId",
                        column: x => x.TargetOrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dashboard_org_shares_users_SharedByUserId",
                        column: x => x.SharedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dashboard_org_shares_DashboardId_TargetOrganizationId",
                table: "dashboard_org_shares",
                columns: new[] { "DashboardId", "TargetOrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dashboard_org_shares_OrganizationId",
                table: "dashboard_org_shares",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_dashboard_org_shares_SharedByUserId",
                table: "dashboard_org_shares",
                column: "SharedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_dashboard_org_shares_TargetOrganizationId",
                table: "dashboard_org_shares",
                column: "TargetOrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dashboard_org_shares");
        }
    }
}
