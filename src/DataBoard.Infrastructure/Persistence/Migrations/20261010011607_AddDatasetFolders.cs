using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDatasetFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "datasets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dataset_folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dataset_folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dataset_folders_dataset_folders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "dataset_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dataset_folders_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dataset_folders_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_datasets_FolderId",
                table: "datasets",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_dataset_folders_CreatedByUserId",
                table: "dataset_folders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_dataset_folders_ParentId",
                table: "dataset_folders",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "ix_dataset_folders_sibling_name",
                table: "dataset_folders",
                columns: new[] { "OrganizationId", "ParentId", "Name" },
                unique: true,
                filter: "\"ParentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dataset_folders_top_level_name",
                table: "dataset_folders",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "\"ParentId\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_datasets_dataset_folders_FolderId",
                table: "datasets",
                column: "FolderId",
                principalTable: "dataset_folders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_datasets_dataset_folders_FolderId",
                table: "datasets");

            migrationBuilder.DropTable(
                name: "dataset_folders");

            migrationBuilder.DropIndex(
                name: "IX_datasets_FolderId",
                table: "datasets");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "datasets");
        }
    }
}
