using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School3Staff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    job_title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_profiles_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_profiles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_school_classes_class_teacher_staff_id",
                table: "school_classes",
                column: "class_teacher_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_subjects_teacher_staff_id",
                table: "class_subjects",
                column: "teacher_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_profiles_membership_id",
                table: "staff_profiles",
                column: "membership_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_profiles_tenant_id_staff_no",
                table: "staff_profiles",
                columns: new[] { "tenant_id", "staff_no" },
                unique: true,
                filter: "staff_no is not null");

            migrationBuilder.AddForeignKey(
                name: "fk_class_subjects_staff_profiles_teacher_staff_id",
                table: "class_subjects",
                column: "teacher_staff_id",
                principalTable: "staff_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_school_classes_staff_profiles_class_teacher_staff_id",
                table: "school_classes",
                column: "class_teacher_staff_id",
                principalTable: "staff_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_class_subjects_staff_profiles_teacher_staff_id",
                table: "class_subjects");

            migrationBuilder.DropForeignKey(
                name: "fk_school_classes_staff_profiles_class_teacher_staff_id",
                table: "school_classes");

            migrationBuilder.DropTable(
                name: "staff_profiles");

            migrationBuilder.DropIndex(
                name: "ix_school_classes_class_teacher_staff_id",
                table: "school_classes");

            migrationBuilder.DropIndex(
                name: "ix_class_subjects_teacher_staff_id",
                table: "class_subjects");
        }
    }
}
