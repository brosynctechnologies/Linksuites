using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoneySafetyFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refund_alerted_at",
                table: "stays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "refund_attempts",
                table: "stays",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refund_pending_since",
                table: "stays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refund_alerted_at",
                table: "course_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "refund_attempts",
                table: "course_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refund_pending_since",
                table: "course_orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refund_alerted_at",
                table: "stays");

            migrationBuilder.DropColumn(
                name: "refund_attempts",
                table: "stays");

            migrationBuilder.DropColumn(
                name: "refund_pending_since",
                table: "stays");

            migrationBuilder.DropColumn(
                name: "refund_alerted_at",
                table: "course_orders");

            migrationBuilder.DropColumn(
                name: "refund_attempts",
                table: "course_orders");

            migrationBuilder.DropColumn(
                name: "refund_pending_since",
                table: "course_orders");
        }
    }
}
