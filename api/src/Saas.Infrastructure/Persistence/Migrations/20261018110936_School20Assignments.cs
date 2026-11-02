using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School20Assignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    instructions = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_marks = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    assessment_component_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignments_assessment_components_assessment_component_id",
                        column: x => x.assessment_component_id,
                        principalTable: "assessment_components",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_assignments_class_subjects_class_subject_id",
                        column: x => x.class_subject_id,
                        principalTable: "class_subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assignments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assignments_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assignment_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    file_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    file_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    late = table.Column<bool>(type: "boolean", nullable: false),
                    mark = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    feedback = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    marked_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reopened = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_submissions_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assignment_submissions_enrolments_enrolment_id",
                        column: x => x.enrolment_id,
                        principalTable: "enrolments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assignment_submissions_staff_profiles_marked_by_staff_id",
                        column: x => x.marked_by_staff_id,
                        principalTable: "staff_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_assignment_submissions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_submissions_assignment_id_enrolment_id",
                table: "assignment_submissions",
                columns: new[] { "assignment_id", "enrolment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assignment_submissions_enrolment_id",
                table: "assignment_submissions",
                column: "enrolment_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_submissions_marked_by_staff_id",
                table: "assignment_submissions",
                column: "marked_by_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_submissions_tenant_id",
                table: "assignment_submissions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_assessment_component_id",
                table: "assignments",
                column: "assessment_component_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_class_subject_id_term_id",
                table: "assignments",
                columns: new[] { "class_subject_id", "term_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assignments_tenant_id",
                table: "assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_term_id",
                table: "assignments",
                column: "term_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_submissions");

            migrationBuilder.DropTable(
                name: "assignments");
        }
    }
}
