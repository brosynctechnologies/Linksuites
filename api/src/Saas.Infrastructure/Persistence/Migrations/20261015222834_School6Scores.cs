using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School6Scores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    entered_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scores", x => x.id);
                    table.ForeignKey(
                        name: "fk_scores_assessment_components_component_id",
                        column: x => x.component_id,
                        principalTable: "assessment_components",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scores_class_subjects_class_subject_id",
                        column: x => x.class_subject_id,
                        principalTable: "class_subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scores_enrolments_enrolment_id",
                        column: x => x.enrolment_id,
                        principalTable: "enrolments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scores_staff_profiles_entered_by_staff_id",
                        column: x => x.entered_by_staff_id,
                        principalTable: "staff_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scores_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scores_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "term_result_locks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_term_result_locks", x => x.id);
                    table.ForeignKey(
                        name: "fk_term_result_locks_school_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "school_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_term_result_locks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_term_result_locks_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scores_class_subject_id",
                table: "scores",
                column: "class_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_scores_component_id",
                table: "scores",
                column: "component_id");

            migrationBuilder.CreateIndex(
                name: "ix_scores_enrolment_id_class_subject_id_term_id_component_id",
                table: "scores",
                columns: new[] { "enrolment_id", "class_subject_id", "term_id", "component_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scores_entered_by_staff_id",
                table: "scores",
                column: "entered_by_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_scores_tenant_id_class_subject_id_term_id",
                table: "scores",
                columns: new[] { "tenant_id", "class_subject_id", "term_id" });

            migrationBuilder.CreateIndex(
                name: "ix_scores_term_id",
                table: "scores",
                column: "term_id");

            migrationBuilder.CreateIndex(
                name: "ix_term_result_locks_class_id",
                table: "term_result_locks",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_term_result_locks_tenant_id_class_id_term_id",
                table: "term_result_locks",
                columns: new[] { "tenant_id", "class_id", "term_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_term_result_locks_term_id",
                table: "term_result_locks",
                column: "term_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scores");

            migrationBuilder.DropTable(
                name: "term_result_locks");
        }
    }
}
