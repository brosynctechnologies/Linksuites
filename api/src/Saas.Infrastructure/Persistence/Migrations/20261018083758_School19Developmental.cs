using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School19Developmental : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "developmental_areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    level_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_developmental_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_developmental_areas_levels_level_id",
                        column: x => x.level_id,
                        principalTable: "levels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_developmental_areas_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "developmental_scales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    level_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_developmental_scales", x => x.id);
                    table.ForeignKey(
                        name: "fk_developmental_scales_levels_level_id",
                        column: x => x.level_id,
                        principalTable: "levels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_developmental_scales_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "developmental_ratings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_developmental_ratings", x => x.id);
                    table.ForeignKey(
                        name: "fk_developmental_ratings_developmental_areas_area_id",
                        column: x => x.area_id,
                        principalTable: "developmental_areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_developmental_ratings_developmental_scales_scale_id",
                        column: x => x.scale_id,
                        principalTable: "developmental_scales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_developmental_ratings_enrolments_enrolment_id",
                        column: x => x.enrolment_id,
                        principalTable: "enrolments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_developmental_ratings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_developmental_ratings_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_developmental_areas_level_id",
                table: "developmental_areas",
                column: "level_id");

            migrationBuilder.CreateIndex(
                name: "ix_developmental_areas_tenant_id_level_id",
                table: "developmental_areas",
                columns: new[] { "tenant_id", "level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_developmental_ratings_area_id",
                table: "developmental_ratings",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "ix_developmental_ratings_enrolment_id_term_id_area_id",
                table: "developmental_ratings",
                columns: new[] { "enrolment_id", "term_id", "area_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_developmental_ratings_scale_id",
                table: "developmental_ratings",
                column: "scale_id");

            migrationBuilder.CreateIndex(
                name: "ix_developmental_ratings_tenant_id",
                table: "developmental_ratings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_developmental_ratings_term_id",
                table: "developmental_ratings",
                column: "term_id");

            migrationBuilder.CreateIndex(
                name: "ix_developmental_scales_level_id",
                table: "developmental_scales",
                column: "level_id");

            migrationBuilder.CreateIndex(
                name: "ix_developmental_scales_tenant_id_level_id",
                table: "developmental_scales",
                columns: new[] { "tenant_id", "level_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "developmental_ratings");

            migrationBuilder.DropTable(
                name: "developmental_areas");

            migrationBuilder.DropTable(
                name: "developmental_scales");
        }
    }
}
