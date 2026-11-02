using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School28ImportConfirmed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "result",
                table: "import_batches",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "import_batches",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                // Batches held when this runs are waiting for their confirm.
                defaultValue: "Held");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "result",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "status",
                table: "import_batches");
        }
    }
}
