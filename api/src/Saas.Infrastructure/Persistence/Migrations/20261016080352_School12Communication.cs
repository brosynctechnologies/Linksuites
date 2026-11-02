using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School12Communication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    body = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    attachment_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    attachment_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    attachment_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    audience = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    level_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    class_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    email_guardians = table.Column<bool>(type: "boolean", nullable: false),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcements", x => x.id);
                    table.ForeignKey(
                        name: "fk_announcements_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_threads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_office = table.Column<bool>(type: "boolean", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_threads", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_threads_guardians_guardian_id",
                        column: x => x.guardian_id,
                        principalTable: "guardians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_message_threads_staff_profiles_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_message_threads_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_message_threads_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "announcement_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    announcement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcement_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_announcement_deliveries_announcements_announcement_id",
                        column: x => x.announcement_id,
                        principalTable: "announcements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_announcement_deliveries_guardians_guardian_id",
                        column: x => x.guardian_id,
                        principalTable: "guardians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_announcement_deliveries_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_parent = table.Column<bool>(type: "boolean", nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    attachment_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_message_threads_thread_id",
                        column: x => x.thread_id,
                        principalTable: "message_threads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_messages_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_announcement_deliveries_announcement_id_guardian_id",
                table: "announcement_deliveries",
                columns: new[] { "announcement_id", "guardian_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_announcement_deliveries_guardian_id",
                table: "announcement_deliveries",
                column: "guardian_id");

            migrationBuilder.CreateIndex(
                name: "ix_announcement_deliveries_tenant_id_status_created_at",
                table: "announcement_deliveries",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_announcements_tenant_id_posted_at",
                table: "announcements",
                columns: new[] { "tenant_id", "posted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_message_threads_guardian_id_student_id",
                table: "message_threads",
                columns: new[] { "guardian_id", "student_id" },
                unique: true,
                filter: "is_office = true");

            migrationBuilder.CreateIndex(
                name: "ix_message_threads_guardian_id_student_id_staff_id",
                table: "message_threads",
                columns: new[] { "guardian_id", "student_id", "staff_id" },
                unique: true,
                filter: "is_office = false");

            migrationBuilder.CreateIndex(
                name: "ix_message_threads_staff_id",
                table: "message_threads",
                column: "staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_threads_student_id",
                table: "message_threads",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_threads_tenant_id_last_message_at",
                table: "message_threads",
                columns: new[] { "tenant_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id",
                table: "messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_thread_id_created_at",
                table: "messages",
                columns: new[] { "thread_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcement_deliveries");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropTable(
                name: "message_threads");
        }
    }
}
