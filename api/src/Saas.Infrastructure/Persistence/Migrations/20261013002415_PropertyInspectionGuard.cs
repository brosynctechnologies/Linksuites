using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PropertyInspectionGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inspections_agent_profile_id_scheduled_at",
                table: "inspections");

            migrationBuilder.CreateIndex(
                name: "ix_inspections_open_slot",
                table: "inspections",
                columns: new[] { "agent_profile_id", "scheduled_at" },
                unique: true,
                filter: "status IN ('Requested', 'Confirmed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inspections_open_slot",
                table: "inspections");

            migrationBuilder.CreateIndex(
                name: "ix_inspections_agent_profile_id_scheduled_at",
                table: "inspections",
                columns: new[] { "agent_profile_id", "scheduled_at" });
        }
    }
}
