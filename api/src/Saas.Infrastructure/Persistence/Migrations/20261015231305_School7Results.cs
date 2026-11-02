using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School7Results : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "results_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_results_notices", x => x.id);
                    table.ForeignKey(
                        name: "fk_results_notices_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_results_notices_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_results_notices_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subject_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    grade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    remark = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    class_average = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    position_in_subject = table.Column<int>(type: "integer", nullable: false),
                    incomplete = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subject_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_subject_results_class_subjects_class_subject_id",
                        column: x => x.class_subject_id,
                        principalTable: "class_subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subject_results_enrolments_enrolment_id",
                        column: x => x.enrolment_id,
                        principalTable: "enrolments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subject_results_school_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "school_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subject_results_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subject_results_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "term_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: false),
                    average = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    class_size = table.Column<int>(type: "integer", nullable: false),
                    days_present = table.Column<int>(type: "integer", nullable: false),
                    days_open = table.Column<int>(type: "integer", nullable: false),
                    teacher_remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    principal_remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    incomplete = table.Column<bool>(type: "boolean", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stale = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_term_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_term_results_enrolments_enrolment_id",
                        column: x => x.enrolment_id,
                        principalTable: "enrolments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_term_results_school_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "school_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_term_results_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_term_results_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_results_notices_student_id_term_id",
                table: "results_notices",
                columns: new[] { "student_id", "term_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_results_notices_tenant_id",
                table: "results_notices",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_results_notices_term_id",
                table: "results_notices",
                column: "term_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_results_class_id",
                table: "subject_results",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_results_class_subject_id",
                table: "subject_results",
                column: "class_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_results_enrolment_id_class_subject_id_term_id",
                table: "subject_results",
                columns: new[] { "enrolment_id", "class_subject_id", "term_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subject_results_tenant_id_class_id_term_id",
                table: "subject_results",
                columns: new[] { "tenant_id", "class_id", "term_id" });

            migrationBuilder.CreateIndex(
                name: "ix_subject_results_term_id",
                table: "subject_results",
                column: "term_id");

            migrationBuilder.CreateIndex(
                name: "ix_term_results_class_id",
                table: "term_results",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_term_results_enrolment_id_class_id_term_id",
                table: "term_results",
                columns: new[] { "enrolment_id", "class_id", "term_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_term_results_tenant_id_class_id_term_id",
                table: "term_results",
                columns: new[] { "tenant_id", "class_id", "term_id" });

            migrationBuilder.CreateIndex(
                name: "ix_term_results_term_id",
                table: "term_results",
                column: "term_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "results_notices");

            migrationBuilder.DropTable(
                name: "subject_results");

            migrationBuilder.DropTable(
                name: "term_results");
        }
    }
}
