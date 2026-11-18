using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PropertyAdminWave2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                table: "listings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "mismatch_alerted_at",
                table: "boosts",
                type: "timestamp with time zone",
                nullable: true);

            // Listings already waiting keep the time the queue showed before (their last edit), so the order doesn't jump.
            migrationBuilder.Sql("""
                UPDATE listings SET submitted_at = COALESCE(updated_at, created_at)
                WHERE status IN ('Pending', 'Hidden') AND submitted_at IS NULL;
                """);

            // Boosts whose wrong-amount alert already went out (found by reference, as the old check did) are not alerted again.
            migrationBuilder.Sql("""
                UPDATE boosts b SET mismatch_alerted_at = n.first_at
                FROM (SELECT payload->>'reference' AS reference, MIN(created_at) AS first_at FROM notifications
                      WHERE template_key = 'boost.payment_mismatch' GROUP BY payload->>'reference') n
                WHERE n.reference = b.reference AND b.mismatch_alerted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "mismatch_alerted_at",
                table: "boosts");
        }
    }
}
