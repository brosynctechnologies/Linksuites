using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School26FinalFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "days_late",
                table: "term_results",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "days_excused",
                table: "term_results",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "days_absent",
                table: "term_results",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "assessment_mode",
                table: "term_results",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // School16 added the three counts with DEFAULT 0, which would read as "absent 0 days" on cards published before they
            // were counted. Those rows get NULL (the card then prints present of open); compute writes all four from now on.
            migrationBuilder.Sql("""
                ALTER TABLE term_results ALTER COLUMN days_late DROP DEFAULT;
                ALTER TABLE term_results ALTER COLUMN days_absent DROP DEFAULT;
                ALTER TABLE term_results ALTER COLUMN days_excused DROP DEFAULT;
                UPDATE term_results SET days_late = NULL, days_absent = NULL, days_excused = NULL WHERE published_at IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assessment_mode",
                table: "term_results");

            migrationBuilder.Sql("""
                UPDATE term_results SET days_late = COALESCE(days_late, 0), days_absent = COALESCE(days_absent, 0), days_excused = COALESCE(days_excused, 0)
                WHERE days_late IS NULL OR days_absent IS NULL OR days_excused IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "days_late",
                table: "term_results",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "days_excused",
                table: "term_results",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "days_absent",
                table: "term_results",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
