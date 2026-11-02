using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Care10Discharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "finalised_at",
                table: "patient_bills",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "discharged_by_user_id",
                table: "admissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "unsigned_rounds_at_discharge",
                table: "admissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "discharge_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    diagnoses = table.Column<string>(type: "text", nullable: false),
                    treatment = table.Column<string>(type: "text", nullable: false),
                    condition = table.Column<string>(type: "text", nullable: false),
                    take_home = table.Column<string>(type: "text", nullable: false),
                    follow_up = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discharge_summaries", x => x.id);
                    table.ForeignKey(
                        name: "fk_discharge_summaries_admissions_admission_id",
                        column: x => x.admission_id,
                        principalTable: "admissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_discharge_summaries_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_discharge_summaries_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discharge_summaries_admission_id",
                table: "discharge_summaries",
                column: "admission_id");

            migrationBuilder.CreateIndex(
                name: "ix_discharge_summaries_patient_id",
                table: "discharge_summaries",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_discharge_summaries_tenant_id_admission_id",
                table: "discharge_summaries",
                columns: new[] { "tenant_id", "admission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discharge_summaries_tenant_id_patient_id",
                table: "discharge_summaries",
                columns: new[] { "tenant_id", "patient_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discharge_summaries");

            migrationBuilder.DropColumn(
                name: "finalised_at",
                table: "patient_bills");

            migrationBuilder.DropColumn(
                name: "discharged_by_user_id",
                table: "admissions");

            migrationBuilder.DropColumn(
                name: "unsigned_rounds_at_discharge",
                table: "admissions");
        }
    }
}
