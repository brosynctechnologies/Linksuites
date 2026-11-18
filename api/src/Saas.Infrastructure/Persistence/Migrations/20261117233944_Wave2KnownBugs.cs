using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wave2KnownBugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "signature_key",
                table: "prescriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "due_notice_balance_minor",
                table: "patient_bills",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_key",
                table: "discharge_summaries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_key",
                table: "consultations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "balance_after_minor",
                table: "care_payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_key",
                table: "care_payments",
                type: "text",
                nullable: true);

            // Papers signed before this migration keep the signature they print today (each signer's current file), frozen from now on.
            migrationBuilder.Sql("""
                UPDATE consultations c SET signature_key = s.signature_key
                FROM care_staff_profiles s
                WHERE c.status = 'Signed' AND s.tenant_id = c.tenant_id AND s.user_id = c.doctor_user_id AND s.signature_key IS NOT NULL;
                UPDATE discharge_summaries d SET signature_key = s.signature_key
                FROM care_staff_profiles s
                WHERE d.status = 'Signed' AND s.tenant_id = d.tenant_id AND s.user_id = d.doctor_user_id AND s.signature_key IS NOT NULL;
                UPDATE prescriptions p SET signature_key = s.signature_key
                FROM care_staff_profiles s
                WHERE p.consultation_id IS NULL AND s.tenant_id = p.tenant_id AND s.user_id = p.doctor_user_id AND s.signature_key IS NOT NULL;
                UPDATE care_payments p SET signature_key = s.signature_key
                FROM care_staff_profiles s
                WHERE p.recorded_by_user_id IS NOT NULL AND s.tenant_id = p.tenant_id AND s.user_id = p.recorded_by_user_id AND s.signature_key IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "signature_key",
                table: "prescriptions");

            migrationBuilder.DropColumn(
                name: "due_notice_balance_minor",
                table: "patient_bills");

            migrationBuilder.DropColumn(
                name: "signature_key",
                table: "discharge_summaries");

            migrationBuilder.DropColumn(
                name: "signature_key",
                table: "consultations");

            migrationBuilder.DropColumn(
                name: "balance_after_minor",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "signature_key",
                table: "care_payments");
        }
    }
}
