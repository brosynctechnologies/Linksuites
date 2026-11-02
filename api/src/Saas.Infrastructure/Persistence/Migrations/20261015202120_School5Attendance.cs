using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School5Attendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "absence_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absence_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_absence_alerts_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_absence_alerts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance_registers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    taken_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_registers", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_registers_school_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "school_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attendance_registers_staff_profiles_taken_by_staff_id",
                        column: x => x.taken_by_staff_id,
                        principalTable: "staff_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_attendance_registers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance_marks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_marks", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_marks_attendance_registers_register_id",
                        column: x => x.register_id,
                        principalTable: "attendance_registers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attendance_marks_enrolments_enrolment_id",
                        column: x => x.enrolment_id,
                        principalTable: "enrolments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attendance_marks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_absence_alerts_student_id",
                table: "absence_alerts",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_absence_alerts_tenant_id_student_id_date",
                table: "absence_alerts",
                columns: new[] { "tenant_id", "student_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_marks_enrolment_id",
                table: "attendance_marks",
                column: "enrolment_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_marks_register_id_enrolment_id",
                table: "attendance_marks",
                columns: new[] { "register_id", "enrolment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_marks_tenant_id",
                table: "attendance_marks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_registers_class_id",
                table: "attendance_registers",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_registers_taken_by_staff_id",
                table: "attendance_registers",
                column: "taken_by_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_registers_tenant_id_class_id_date",
                table: "attendance_registers",
                columns: new[] { "tenant_id", "class_id", "date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "absence_alerts");

            migrationBuilder.DropTable(
                name: "attendance_marks");

            migrationBuilder.DropTable(
                name: "attendance_registers");
        }
    }
}
