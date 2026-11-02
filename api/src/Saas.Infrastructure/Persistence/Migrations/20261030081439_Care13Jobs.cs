using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Care13Jobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "last_bed_charge_day",
                table: "clinic_profiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "charge_day",
                table: "bill_lines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reminder_sent_at",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "low_deposit_notified_at",
                table: "admissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_bill_lines_bed_day",
                table: "bill_lines",
                columns: new[] { "bill_id", "price_item_id", "charge_day" },
                unique: true,
                filter: "charge_day IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_bill_lines_bed_day",
                table: "bill_lines");

            migrationBuilder.DropColumn(
                name: "last_bed_charge_day",
                table: "clinic_profiles");

            migrationBuilder.DropColumn(
                name: "charge_day",
                table: "bill_lines");

            migrationBuilder.DropColumn(
                name: "reminder_sent_at",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "low_deposit_notified_at",
                table: "admissions");
        }
    }
}
