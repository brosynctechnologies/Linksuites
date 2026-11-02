using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School21LessonPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "lesson_plans_need_review",
                table: "school_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "lesson_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    objectives = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    materials = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    methods = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    activities = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    previous_knowledge = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    introduction = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    presentation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    conclusion = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    evaluation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    teacher_remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reviewer_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_plans", x => x.id);
                    table.ForeignKey(
                        name: "fk_lesson_plans_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_lesson_plans_class_subjects_class_subject_id",
                        column: x => x.class_subject_id,
                        principalTable: "class_subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lesson_plans_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lesson_plans_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lesson_plans_assignment_id",
                table: "lesson_plans",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_plans_class_subject_id_term_id",
                table: "lesson_plans",
                columns: new[] { "class_subject_id", "term_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lesson_plans_tenant_id",
                table: "lesson_plans",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_plans_term_id",
                table: "lesson_plans",
                column: "term_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lesson_plans");

            migrationBuilder.DropColumn(
                name: "lesson_plans_need_review",
                table: "school_profiles");
        }
    }
}
