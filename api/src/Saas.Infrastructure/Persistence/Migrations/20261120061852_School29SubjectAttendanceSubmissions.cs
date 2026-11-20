using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School29SubjectAttendanceSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "subject_attendance_enabled",
                table: "school_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sent_by_parent",
                table: "assignment_submissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "subject_registers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    taken_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subject_registers", x => x.id);
                    table.ForeignKey(
                        name: "fk_subject_registers_class_subjects_class_subject_id",
                        column: x => x.class_subject_id,
                        principalTable: "class_subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subject_registers_school_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "school_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subject_registers_staff_profiles_taken_by_staff_id",
                        column: x => x.taken_by_staff_id,
                        principalTable: "staff_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_subject_registers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subject_registers_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subject_attendance_marks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subject_attendance_marks", x => x.id);
                    table.ForeignKey(
                        name: "fk_subject_attendance_marks_enrolments_enrolment_id",
                        column: x => x.enrolment_id,
                        principalTable: "enrolments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subject_attendance_marks_subject_registers_register_id",
                        column: x => x.register_id,
                        principalTable: "subject_registers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subject_attendance_marks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subject_attendance_marks_enrolment_id",
                table: "subject_attendance_marks",
                column: "enrolment_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_attendance_marks_register_id_enrolment_id",
                table: "subject_attendance_marks",
                columns: new[] { "register_id", "enrolment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subject_attendance_marks_tenant_id",
                table: "subject_attendance_marks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_registers_class_id",
                table: "subject_registers",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_registers_class_subject_id_date_period",
                table: "subject_registers",
                columns: new[] { "class_subject_id", "date", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subject_registers_class_subject_id_term_id",
                table: "subject_registers",
                columns: new[] { "class_subject_id", "term_id" });

            migrationBuilder.CreateIndex(
                name: "ix_subject_registers_taken_by_staff_id",
                table: "subject_registers",
                column: "taken_by_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_registers_tenant_id_class_id_date",
                table: "subject_registers",
                columns: new[] { "tenant_id", "class_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_subject_registers_term_id",
                table: "subject_registers",
                column: "term_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subject_attendance_marks");

            migrationBuilder.DropTable(
                name: "subject_registers");

            migrationBuilder.DropColumn(
                name: "subject_attendance_enabled",
                table: "school_profiles");

            migrationBuilder.DropColumn(
                name: "sent_by_parent",
                table: "assignment_submissions");
        }
    }
}
