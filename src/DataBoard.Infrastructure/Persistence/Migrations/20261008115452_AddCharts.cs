using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCharts : Migration
    {
        /// <summary>
        /// For every widget { id, title, datasetId, chartType, query } of every dashboard: insert a chart named after the
        /// title (a number is added when the name is taken in that organization) and replace the widget by
        /// { id, chartId }. A widget whose dataset no longer exists in the organization is dropped.
        /// </summary>
        private const string EmbeddedChartsToSavedCharts = """
            DO $$
            DECLARE
                d record;
                w jsonb;
                new_widgets jsonb;
                chart_id uuid;
                base_name text;
                chart_name text;
                n int;
            BEGIN
                FOR d IN SELECT "Id", "OrganizationId", "CreatedByUserId", definition FROM dashboards LOOP
                    new_widgets := '[]'::jsonb;
                    FOR w IN SELECT value FROM jsonb_array_elements(COALESCE(d.definition -> 'widgets', '[]'::jsonb)) LOOP
                        IF w ? 'chartId' THEN
                            new_widgets := new_widgets || jsonb_build_array(w);
                            CONTINUE;
                        END IF;

                        IF NOT EXISTS (
                            SELECT 1 FROM datasets
                            WHERE "Id" = (w ->> 'datasetId')::uuid AND "OrganizationId" = d."OrganizationId") THEN
                            CONTINUE;
                        END IF;

                        base_name := left(COALESCE(NULLIF(trim(w ->> 'title'), ''), 'Chart'), 190);
                        n := 1;
                        chart_name := base_name;
                        WHILE EXISTS (
                            SELECT 1 FROM charts WHERE "OrganizationId" = d."OrganizationId" AND lower("Name") = lower(chart_name)) LOOP
                            n := n + 1;
                            chart_name := base_name || ' (' || n || ')';
                        END LOOP;

                        chart_id := gen_random_uuid();
                        INSERT INTO charts ("Id", "OrganizationId", "DatasetId", "Name", "Description", "Type", definition,
                                            "CreatedByUserId", "CreatedUtc", "UpdatedUtc")
                        VALUES (chart_id, d."OrganizationId", (w ->> 'datasetId')::uuid, chart_name, NULL,
                                COALESCE(w ->> 'chartType', 'Table'),
                                jsonb_build_object(
                                    'query', COALESCE(w -> 'query', '{}'::jsonb),
                                    'style', jsonb_build_object('legend', true, 'labels', false, 'palette', 'default')),
                                d."CreatedByUserId", now(), now());

                        new_widgets := new_widgets || jsonb_build_array(jsonb_build_object(
                            'id', COALESCE(w ->> 'id', gen_random_uuid()::text), 'chartId', chart_id));
                    END LOOP;

                    UPDATE dashboards SET definition = jsonb_set(d.definition, '{widgets}', new_widgets) WHERE "Id" = d."Id";
                END LOOP;
            END $$;
            """;

        /// <summary>The reverse: widgets { id, chartId } become { id, title, datasetId, chartType, query } again.</summary>
        private const string SavedChartsToEmbeddedCharts = """
            UPDATE dashboards d
            SET definition = jsonb_set(d.definition, '{widgets}', COALESCE((
                SELECT jsonb_agg(jsonb_build_object(
                    'id', w.value ->> 'id',
                    'title', c."Name",
                    'datasetId', c."DatasetId",
                    'chartType', c."Type",
                    'query', COALESCE(c.definition -> 'query', '{}'::jsonb)))
                FROM jsonb_array_elements(COALESCE(d.definition -> 'widgets', '[]'::jsonb)) AS w(value)
                JOIN charts c ON c."Id" = (w.value ->> 'chartId')::uuid), '[]'::jsonb));
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_charts_datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_charts_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_charts_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_charts_CreatedByUserId",
                table: "charts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_charts_DatasetId",
                table: "charts",
                column: "DatasetId");

            migrationBuilder.CreateIndex(
                name: "IX_charts_OrganizationId_Name",
                table: "charts",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            // Charts used to live inside dashboards. Each embedded chart becomes a saved chart, and the dashboard
            // keeps only a reference to it.
            migrationBuilder.Sql(EmbeddedChartsToSavedCharts);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Put each chart back inside the dashboards that reference it, before the charts are dropped.
            migrationBuilder.Sql(SavedChartsToEmbeddedCharts);

            migrationBuilder.DropTable(
                name: "charts");
        }
    }
}
