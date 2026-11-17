using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CareWave1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "amended_at",
                table: "test_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "provider_paid_at",
                table: "care_payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "test_result_amendments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_text = table.Column<string>(type: "text", nullable: true),
                    result_file_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    result_file_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    resulted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resulted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_result_amendments", x => x.id);
                    table.ForeignKey(
                        name: "fk_test_result_amendments_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_test_result_amendments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_test_result_amendments_test_requests_test_request_id",
                        column: x => x.test_request_id,
                        principalTable: "test_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_test_result_amendments_patient_id",
                table: "test_result_amendments",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_test_result_amendments_tenant_id_test_request_id",
                table: "test_result_amendments",
                columns: new[] { "tenant_id", "test_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_test_result_amendments_test_request_id",
                table: "test_result_amendments",
                column: "test_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "test_result_amendments");

            migrationBuilder.DropColumn(
                name: "amended_at",
                table: "test_requests");

            migrationBuilder.DropColumn(
                name: "provider_paid_at",
                table: "care_payments");
        }
    }
}
