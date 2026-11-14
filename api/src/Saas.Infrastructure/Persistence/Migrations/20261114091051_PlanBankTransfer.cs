using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanBankTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_payment_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    online_payments_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    plan_provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    bank_account_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_payment_settings", x => x.id);
                });

            // The one settings row (owner's decision, 14 Nov 2026): online payment off, plans paid by bank transfer to the company
            // account. The provider is left empty, so Billing:PlanProvider from config applies until an admin chooses one.
            migrationBuilder.InsertData(
                table: "platform_payment_settings",
                columns: ["id", "online_payments_enabled", "plan_provider", "bank_name", "bank_account_number", "bank_account_name"],
                values: new object[] { 1, false, null, "OPay", "9067382888", "Brosync Technologies" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_payment_settings");
        }
    }
}
