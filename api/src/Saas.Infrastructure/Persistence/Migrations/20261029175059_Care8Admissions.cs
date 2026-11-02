using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Care8Admissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "care_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    receipt_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_deposit = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_care_payments_patient_bills_bill_id",
                        column: x => x.bill_id,
                        principalTable: "patient_bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_care_payments_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_care_payments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    bed_price_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_deposit_minor = table.Column<long>(type: "bigint", nullable: true),
                    nurse_user_ids = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wards", x => x.id);
                    table.ForeignKey(
                        name: "fk_wards_price_items_bed_price_item_id",
                        column: x => x.bed_price_item_id,
                        principalTable: "price_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_wards_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "beds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ward_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beds", x => x.id);
                    table.ForeignKey(
                        name: "fk_beds_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_beds_wards_ward_id",
                        column: x => x.ward_id,
                        principalTable: "wards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "admissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    admitting_doctor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consultation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    admitting_diagnosis = table.Column<string>(type: "text", nullable: false),
                    bed_id = table.Column<Guid>(type: "uuid", nullable: true),
                    admitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deposit_override_by = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    discharge_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    discharged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    death_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    death_cause = table.Column<string>(type: "text", nullable: true),
                    ama_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_admissions_beds_bed_id",
                        column: x => x.bed_id,
                        principalTable: "beds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_admissions_consultations_consultation_id",
                        column: x => x.consultation_id,
                        principalTable: "consultations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_admissions_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_admissions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bed_moves",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_bed_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_bed_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bed_moves", x => x.id);
                    table.ForeignKey(
                        name: "fk_bed_moves_admissions_admission_id",
                        column: x => x.admission_id,
                        principalTable: "admissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bed_moves_beds_from_bed_id",
                        column: x => x.from_bed_id,
                        principalTable: "beds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bed_moves_beds_to_bed_id",
                        column: x => x.to_bed_id,
                        principalTable: "beds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bed_moves_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_test_requests_admission_id",
                table: "test_requests",
                column: "admission_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_admission_id",
                table: "prescriptions",
                column: "admission_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_assignments_admission_id",
                table: "care_assignments",
                column: "admission_id");

            migrationBuilder.CreateIndex(
                name: "ix_admissions_bed_id",
                table: "admissions",
                column: "bed_id");

            migrationBuilder.CreateIndex(
                name: "ix_admissions_consultation_id",
                table: "admissions",
                column: "consultation_id");

            migrationBuilder.CreateIndex(
                name: "ix_admissions_patient_id",
                table: "admissions",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_admissions_tenant_id_bed_id",
                table: "admissions",
                columns: new[] { "tenant_id", "bed_id" },
                unique: true,
                filter: "bed_id IS NOT NULL AND status = 'Admitted'");

            migrationBuilder.CreateIndex(
                name: "ix_admissions_tenant_id_patient_id_status",
                table: "admissions",
                columns: new[] { "tenant_id", "patient_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_admissions_open_patient",
                table: "admissions",
                columns: new[] { "tenant_id", "patient_id" },
                unique: true,
                filter: "status = 'Admitted'");

            migrationBuilder.CreateIndex(
                name: "ix_bed_moves_admission_id",
                table: "bed_moves",
                column: "admission_id");

            migrationBuilder.CreateIndex(
                name: "ix_bed_moves_from_bed_id",
                table: "bed_moves",
                column: "from_bed_id");

            migrationBuilder.CreateIndex(
                name: "ix_bed_moves_tenant_id_admission_id_at",
                table: "bed_moves",
                columns: new[] { "tenant_id", "admission_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_bed_moves_to_bed_id",
                table: "bed_moves",
                column: "to_bed_id");

            migrationBuilder.CreateIndex(
                name: "ix_beds_tenant_id_ward_id_label",
                table: "beds",
                columns: new[] { "tenant_id", "ward_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_beds_ward_id",
                table: "beds",
                column: "ward_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_payments_bill_id",
                table: "care_payments",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_payments_patient_id",
                table: "care_payments",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_payments_tenant_id_receipt_number",
                table: "care_payments",
                columns: new[] { "tenant_id", "receipt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wards_bed_price_item_id",
                table: "wards",
                column: "bed_price_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_wards_tenant_id_name",
                table: "wards",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            // M5: rows written during Task 7's admission stub window can name an admission that never existed. Clear those dangling
            // references before the foreign keys go on (in a real environment every row is already NULL).
            migrationBuilder.Sql("UPDATE prescriptions SET admission_id = NULL WHERE admission_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM admissions a WHERE a.id = prescriptions.admission_id);");
            migrationBuilder.Sql("UPDATE test_requests SET admission_id = NULL WHERE admission_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM admissions a WHERE a.id = test_requests.admission_id);");

            migrationBuilder.AddForeignKey(
                name: "fk_prescriptions_admissions_admission_id",
                table: "prescriptions",
                column: "admission_id",
                principalTable: "admissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_test_requests_admissions_admission_id",
                table: "test_requests",
                column: "admission_id",
                principalTable: "admissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_prescriptions_admissions_admission_id",
                table: "prescriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_test_requests_admissions_admission_id",
                table: "test_requests");

            migrationBuilder.DropTable(
                name: "bed_moves");

            migrationBuilder.DropTable(
                name: "care_payments");

            migrationBuilder.DropTable(
                name: "admissions");

            migrationBuilder.DropTable(
                name: "beds");

            migrationBuilder.DropTable(
                name: "wards");

            migrationBuilder.DropIndex(
                name: "ix_test_requests_admission_id",
                table: "test_requests");

            migrationBuilder.DropIndex(
                name: "ix_prescriptions_admission_id",
                table: "prescriptions");

            migrationBuilder.DropIndex(
                name: "ix_care_assignments_admission_id",
                table: "care_assignments");
        }
    }
}
