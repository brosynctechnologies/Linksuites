using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School9PaymentKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "previous_key_retired_at",
                table: "school_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_paystack_secret_key_protected",
                table: "school_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "unverified_alerted_at",
                table: "fee_payments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "previous_key_retired_at",
                table: "school_profiles");

            migrationBuilder.DropColumn(
                name: "previous_paystack_secret_key_protected",
                table: "school_profiles");

            migrationBuilder.DropColumn(
                name: "unverified_alerted_at",
                table: "fee_payments");
        }
    }
}
