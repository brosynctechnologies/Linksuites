using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CareLabPharmacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "collected_at",
                table: "test_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "collected_by",
                table: "test_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_resulted_at",
                table: "test_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_reviewed_at",
                table: "test_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lab_test_id",
                table: "test_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sample_number",
                table: "test_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "next_sample_number",
                table: "clinic_profiles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L); // existing clinics start their sample numbers at 1

            migrationBuilder.CreateTable(
                name: "dispensings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prescription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispensed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispensings", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispensings_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispensings_prescriptions_prescription_id",
                        column: x => x.prescription_id,
                        principalTable: "prescriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispensings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drug_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    form = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    price_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reorder_level = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    low_stock_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drug_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_drug_items_price_items_price_item_id",
                        column: x => x.price_item_id,
                        principalTable: "price_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_drug_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lab_result_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analyte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    numeric_value = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    text_value = table.Column<string>(type: "text", nullable: true),
                    low = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    high = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    critical_low = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    critical_high = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    flag = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    entered_by = table.Column<Guid>(type: "uuid", nullable: false),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_result_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_lab_result_values_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lab_result_values_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lab_result_values_test_requests_test_request_id",
                        column: x => x.test_request_id,
                        principalTable: "test_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lab_tests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    sample_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    price_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    turnaround_hours = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_tests", x => x.id);
                    table.ForeignKey(
                        name: "fk_lab_tests_price_items_price_item_id",
                        column: x => x.price_item_id,
                        principalTable: "price_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lab_tests_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    received_by = table.Column<Guid>(type: "uuid", nullable: false),
                    total_cost_minor = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_receipts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lab_analytes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lab_test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ranges = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_analytes", x => x.id);
                    table.ForeignKey(
                        name: "fk_lab_analytes_lab_tests_lab_test_id",
                        column: x => x.lab_test_id,
                        principalTable: "lab_tests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lab_analytes_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drug_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    drug_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity_received = table.Column<int>(type: "integer", nullable: false),
                    quantity_on_hand = table.Column<int>(type: "integer", nullable: false),
                    unit_cost_minor = table.Column<long>(type: "bigint", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expiry_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drug_batches", x => x.id);
                    table.CheckConstraint("ck_drug_batches_on_hand", "quantity_on_hand >= 0");
                    table.ForeignKey(
                        name: "fk_drug_batches_drug_items_drug_item_id",
                        column: x => x.drug_item_id,
                        principalTable: "drug_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_drug_batches_stock_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "stock_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_drug_batches_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    drug_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    drug_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    balance_after = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dispensing_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prescription_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_movements_dispensings_dispensing_id",
                        column: x => x.dispensing_id,
                        principalTable: "dispensings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_drug_batches_drug_batch_id",
                        column: x => x.drug_batch_id,
                        principalTable: "drug_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_drug_items_drug_item_id",
                        column: x => x.drug_item_id,
                        principalTable: "drug_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_prescription_items_prescription_item_id",
                        column: x => x.prescription_item_id,
                        principalTable: "prescription_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_test_requests_lab_test_id",
                table: "test_requests",
                column: "lab_test_id");

            migrationBuilder.CreateIndex(
                name: "ix_test_requests_tenant_id_status_created_at",
                table: "test_requests",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_dispensings_patient_id",
                table: "dispensings",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispensings_prescription_id",
                table: "dispensings",
                column: "prescription_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispensings_tenant_id_prescription_id",
                table: "dispensings",
                columns: new[] { "tenant_id", "prescription_id" });

            migrationBuilder.CreateIndex(
                name: "ix_drug_batches_drug_item_id",
                table: "drug_batches",
                column: "drug_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_drug_batches_receipt_id",
                table: "drug_batches",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_drug_batches_tenant_id_drug_item_id_expires_on",
                table: "drug_batches",
                columns: new[] { "tenant_id", "drug_item_id", "expires_on" });

            migrationBuilder.CreateIndex(
                name: "ix_drug_items_price_item_id",
                table: "drug_items",
                column: "price_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_drug_items_tenant_id_name",
                table: "drug_items",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_drug_items_tenant_id_price_item_id",
                table: "drug_items",
                columns: new[] { "tenant_id", "price_item_id" },
                unique: true,
                filter: "price_item_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_lab_analytes_lab_test_id",
                table: "lab_analytes",
                column: "lab_test_id");

            migrationBuilder.CreateIndex(
                name: "ix_lab_analytes_tenant_id",
                table: "lab_analytes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_lab_result_values_patient_id",
                table: "lab_result_values",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_lab_result_values_tenant_id_test_request_id",
                table: "lab_result_values",
                columns: new[] { "tenant_id", "test_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lab_result_values_test_request_id",
                table: "lab_result_values",
                column: "test_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_lab_tests_price_item_id",
                table: "lab_tests",
                column: "price_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_lab_tests_tenant_id_name",
                table: "lab_tests",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_dispensing_id",
                table: "stock_movements",
                column: "dispensing_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_drug_batch_id",
                table: "stock_movements",
                column: "drug_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_drug_item_id",
                table: "stock_movements",
                column: "drug_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_prescription_item_id",
                table: "stock_movements",
                column: "prescription_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_at",
                table: "stock_movements",
                columns: new[] { "tenant_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_drug_item_id_at",
                table: "stock_movements",
                columns: new[] { "tenant_id", "drug_item_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipts_tenant_id_received_on",
                table: "stock_receipts",
                columns: new[] { "tenant_id", "received_on" });

            migrationBuilder.AddForeignKey(
                name: "fk_test_requests_lab_tests_lab_test_id",
                table: "test_requests",
                column: "lab_test_id",
                principalTable: "lab_tests",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_test_requests_lab_tests_lab_test_id",
                table: "test_requests");

            migrationBuilder.DropTable(
                name: "lab_analytes");

            migrationBuilder.DropTable(
                name: "lab_result_values");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "lab_tests");

            migrationBuilder.DropTable(
                name: "dispensings");

            migrationBuilder.DropTable(
                name: "drug_batches");

            migrationBuilder.DropTable(
                name: "drug_items");

            migrationBuilder.DropTable(
                name: "stock_receipts");

            migrationBuilder.DropIndex(
                name: "ix_test_requests_lab_test_id",
                table: "test_requests");

            migrationBuilder.DropIndex(
                name: "ix_test_requests_tenant_id_status_created_at",
                table: "test_requests");

            migrationBuilder.DropColumn(
                name: "collected_at",
                table: "test_requests");

            migrationBuilder.DropColumn(
                name: "collected_by",
                table: "test_requests");

            migrationBuilder.DropColumn(
                name: "first_resulted_at",
                table: "test_requests");

            migrationBuilder.DropColumn(
                name: "first_reviewed_at",
                table: "test_requests");

            migrationBuilder.DropColumn(
                name: "lab_test_id",
                table: "test_requests");

            migrationBuilder.DropColumn(
                name: "sample_number",
                table: "test_requests");

            migrationBuilder.DropColumn(
                name: "next_sample_number",
                table: "clinic_profiles");
        }
    }
}
