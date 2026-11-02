using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School9Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "needs_review",
                table: "student_invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "next_receipt_number",
                table: "school_profiles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L); // existing schools start at RCT-…-00001

            migrationBuilder.CreateTable(
                name: "fee_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    provider_ref = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    charged_amount_minor = table.Column<long>(type: "bigint", nullable: true),
                    charged_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_fee_payments_student_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "student_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fee_payments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fee_payments_invoice_id",
                table: "fee_payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_payments_provider_ref",
                table: "fee_payments",
                column: "provider_ref",
                unique: true,
                filter: "provider_ref IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fee_payments_tenant_id_receipt_number",
                table: "fee_payments",
                columns: new[] { "tenant_id", "receipt_number" },
                unique: true,
                filter: "receipt_number IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fee_payments");

            migrationBuilder.DropColumn(
                name: "needs_review",
                table: "student_invoices");

            migrationBuilder.DropColumn(
                name: "next_receipt_number",
                table: "school_profiles");
        }
    }
}
