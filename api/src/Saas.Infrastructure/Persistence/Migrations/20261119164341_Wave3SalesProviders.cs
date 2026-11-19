using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wave3SalesProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every course order, stay and boost taken before this was paid on Paystack, so existing rows are "paystack".
            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "stays",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "paystack");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rechecked_at",
                table: "stays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "course_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "paystack");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rechecked_at",
                table: "course_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "boosts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "paystack");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rechecked_at",
                table: "boosts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "provider",
                table: "stays");

            migrationBuilder.DropColumn(
                name: "rechecked_at",
                table: "stays");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "course_orders");

            migrationBuilder.DropColumn(
                name: "rechecked_at",
                table: "course_orders");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "boosts");

            migrationBuilder.DropColumn(
                name: "rechecked_at",
                table: "boosts");
        }
    }
}
