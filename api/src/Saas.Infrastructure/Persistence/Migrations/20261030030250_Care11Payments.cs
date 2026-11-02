using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Care11Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "adjusted_minor",
                table: "patient_bills",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "credit_minor",
                table: "patient_bills",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "needs_review",
                table: "patient_bills",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "receipt_number",
                table: "care_payments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<long>(
                name: "charged_amount_minor",
                table: "care_payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "charged_currency",
                table: "care_payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "care_payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NGN");

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "care_payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "payer_user_id",
                table: "care_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_ref",
                table: "care_payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reversal_reason",
                table: "care_payments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reversed_at",
                table: "care_payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reversed_by_user_id",
                table: "care_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "care_payments",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "Succeeded");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "unverified_alerted_at",
                table: "care_payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bill_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bill_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "fk_bill_adjustments_patient_bills_bill_id",
                        column: x => x.bill_id,
                        principalTable: "patient_bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bill_adjustments_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bill_adjustments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_care_payments_status_created_at",
                table: "care_payments",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_care_payments_tenant_id_idempotency_key",
                table: "care_payments",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_care_payments_tenant_id_provider_ref",
                table: "care_payments",
                columns: new[] { "tenant_id", "provider_ref" },
                unique: true,
                filter: "provider_ref IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bill_adjustments_bill_id",
                table: "bill_adjustments",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_bill_adjustments_patient_id",
                table: "bill_adjustments",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_bill_adjustments_tenant_id",
                table: "bill_adjustments",
                column: "tenant_id");

            // Bills that already hold more than they charge (admission deposits before the charges) show as credit (Task 11 ruling).
            migrationBuilder.Sql("""
                UPDATE patient_bills
                SET credit_minor = paid_minor - total_minor, balance_minor = 0, status = 'InCredit'
                WHERE paid_minor > total_minor;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bill_adjustments");

            migrationBuilder.DropIndex(
                name: "ix_care_payments_status_created_at",
                table: "care_payments");

            migrationBuilder.DropIndex(
                name: "ix_care_payments_tenant_id_idempotency_key",
                table: "care_payments");

            migrationBuilder.DropIndex(
                name: "ix_care_payments_tenant_id_provider_ref",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "adjusted_minor",
                table: "patient_bills");

            migrationBuilder.DropColumn(
                name: "credit_minor",
                table: "patient_bills");

            migrationBuilder.DropColumn(
                name: "needs_review",
                table: "patient_bills");

            migrationBuilder.DropColumn(
                name: "charged_amount_minor",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "charged_currency",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "payer_user_id",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "provider_ref",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "reversal_reason",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "reversed_at",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "reversed_by_user_id",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "status",
                table: "care_payments");

            migrationBuilder.DropColumn(
                name: "unverified_alerted_at",
                table: "care_payments");

            migrationBuilder.AlterColumn<string>(
                name: "receipt_number",
                table: "care_payments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);
        }
    }
}
