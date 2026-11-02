using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School16AttendanceCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "days_absent",
                table: "term_results",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "days_excused",
                table: "term_results",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "days_late",
                table: "term_results",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Unpublished rows still store the old present total (late included) and zeros for the new counts.
            // Mark them stale so the next view recomputes. Published rows stay frozen until they are unpublished.
            migrationBuilder.Sql("UPDATE term_results SET stale = true WHERE published_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "days_absent",
                table: "term_results");

            migrationBuilder.DropColumn(
                name: "days_excused",
                table: "term_results");

            migrationBuilder.DropColumn(
                name: "days_late",
                table: "term_results");
        }
    }
}
