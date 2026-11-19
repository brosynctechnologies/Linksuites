using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaystackPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                table: "payouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                table: "payouts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "transfer_attempts",
                table: "payouts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "transfer_code",
                table: "payouts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transfer_reference",
                table: "payouts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "transfer_sent_at",
                table: "payouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transfer_status",
                table: "payouts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "settled_by_provider",
                table: "earnings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "payment_splits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subaccount_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fee_minor = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_splits", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payouts_transfer_reference",
                table: "payouts",
                column: "transfer_reference",
                unique: true,
                filter: "transfer_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_splits_reference",
                table: "payment_splits",
                column: "reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_splits");

            migrationBuilder.DropIndex(
                name: "ix_payouts_transfer_reference",
                table: "payouts");

            migrationBuilder.DropColumn(
                name: "approved_by",
                table: "payouts");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                table: "payouts");

            migrationBuilder.DropColumn(
                name: "transfer_attempts",
                table: "payouts");

            migrationBuilder.DropColumn(
                name: "transfer_code",
                table: "payouts");

            migrationBuilder.DropColumn(
                name: "transfer_reference",
                table: "payouts");

            migrationBuilder.DropColumn(
                name: "transfer_sent_at",
                table: "payouts");

            migrationBuilder.DropColumn(
                name: "transfer_status",
                table: "payouts");

            migrationBuilder.DropColumn(
                name: "settled_by_provider",
                table: "earnings");
        }
    }
}
