using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School27Imports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "import_batches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "students");

            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "import_batches",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "scope",
                table: "import_batches");
        }
    }
}
