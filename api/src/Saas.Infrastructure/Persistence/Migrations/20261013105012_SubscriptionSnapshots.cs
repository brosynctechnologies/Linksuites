using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "features",
                table: "subscriptions",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "limits",
                table: "subscriptions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "price_minor",
                table: "subscriptions",
                type: "bigint",
                nullable: true);

            // Freeze what running subscriptions have today, so later plan edits wait for their next renewal.
            migrationBuilder.Sql("""
                UPDATE subscriptions s
                SET price_minor = p.price_minor, currency = p.currency, limits = p.limits, features = p.features
                FROM plans p
                WHERE p.id = s.plan_id AND s.price_minor IS NULL AND s.status = 'Active';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "currency",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "features",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "limits",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "price_minor",
                table: "subscriptions");
        }
    }
}
