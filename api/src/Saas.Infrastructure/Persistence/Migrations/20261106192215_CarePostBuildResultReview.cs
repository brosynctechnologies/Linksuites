using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CarePostBuildResultReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                table: "test_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                table: "test_requests",
                type: "uuid",
                nullable: true);

            // Ruling (post-build): results recorded before review existed count as reviewed at their recorded time (no reviewer), so
            // nothing a patient could already see disappears from the portal, and nobody is told again.
            migrationBuilder.Sql("UPDATE test_requests SET reviewed_at = resulted_at WHERE status = 'Resulted' AND resulted_at IS NOT NULL AND reviewed_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "test_requests");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                table: "test_requests");
        }
    }
}
