using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Care3AccessReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "reason",
                table: "record_accesses",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Wave 2: reasons are stored encrypted and are longer than 300 characters, so shortening the column would fail half way. Refuse
            // with a clear message while any such row exists (an empty or test database still rolls back).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM record_accesses WHERE length(reason) > 300) THEN
                        RAISE EXCEPTION 'Care3AccessReason cannot be rolled back: access-log reasons are stored encrypted and longer than 300 characters. Restore a backup taken before this migration instead.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "reason",
                table: "record_accesses",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
