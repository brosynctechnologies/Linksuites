using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School18Traits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trait_criteria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    level_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trait_criteria", x => x.id);
                    table.ForeignKey(
                        name: "fk_trait_criteria_levels_level_id",
                        column: x => x.level_id,
                        principalTable: "levels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trait_criteria_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trait_ratings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criterion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trait_ratings", x => x.id);
                    table.ForeignKey(
                        name: "fk_trait_ratings_enrolments_enrolment_id",
                        column: x => x.enrolment_id,
                        principalTable: "enrolments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trait_ratings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trait_ratings_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trait_ratings_trait_criteria_criterion_id",
                        column: x => x.criterion_id,
                        principalTable: "trait_criteria",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trait_criteria_level_id",
                table: "trait_criteria",
                column: "level_id");

            migrationBuilder.CreateIndex(
                name: "ix_trait_criteria_tenant_id_level_id_kind",
                table: "trait_criteria",
                columns: new[] { "tenant_id", "level_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_trait_ratings_criterion_id",
                table: "trait_ratings",
                column: "criterion_id");

            migrationBuilder.CreateIndex(
                name: "ix_trait_ratings_enrolment_id_term_id_criterion_id",
                table: "trait_ratings",
                columns: new[] { "enrolment_id", "term_id", "criterion_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trait_ratings_tenant_id",
                table: "trait_ratings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_trait_ratings_term_id",
                table: "trait_ratings",
                column: "term_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trait_ratings");

            migrationBuilder.DropTable(
                name: "trait_criteria");
        }
    }
}
