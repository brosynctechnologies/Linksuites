using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Care2Patients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "next_bill_number",
                table: "clinic_profiles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L); // existing clinics start at 000001

            migrationBuilder.AddColumn<long>(
                name: "next_patient_number",
                table: "clinic_profiles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L); // existing clinics start at 000001

            migrationBuilder.AddColumn<long>(
                name: "next_receipt_number",
                table: "clinic_profiles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L); // existing clinics start at 000001

            migrationBuilder.CreateTable(
                name: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    last_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    sex = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    next_of_kin_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    next_of_kin_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    next_of_kin_relationship = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    blood_group = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    genotype = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    allergies = table.Column<string>(type: "jsonb", nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patients", x => x.id);
                    table.ForeignKey(
                        name: "fk_patients_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    category = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    price_minor = table.Column<long>(type: "bigint", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_price_items_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "patient_bills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    admission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    total_minor = table.Column<long>(type: "bigint", nullable: false),
                    paid_minor = table.Column<long>(type: "bigint", nullable: false),
                    balance_minor = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_bills", x => x.id);
                    table.ForeignKey(
                        name: "fk_patient_bills_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_patient_bills_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bill_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price_minor = table.Column<long>(type: "bigint", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    clinical_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    added_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    removed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bill_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_bill_lines_patient_bills_bill_id",
                        column: x => x.bill_id,
                        principalTable: "patient_bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bill_lines_price_items_price_item_id",
                        column: x => x.price_item_id,
                        principalTable: "price_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bill_lines_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bill_lines_bill_id",
                table: "bill_lines",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_bill_lines_price_item_id",
                table: "bill_lines",
                column: "price_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_bill_lines_tenant_id",
                table: "bill_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_bills_patient_id",
                table: "patient_bills",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_bills_tenant_id_admission_id",
                table: "patient_bills",
                columns: new[] { "tenant_id", "admission_id" },
                unique: true,
                filter: "admission_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_patient_bills_tenant_id_number",
                table: "patient_bills",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patient_bills_tenant_id_patient_id",
                table: "patient_bills",
                columns: new[] { "tenant_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_bills_tenant_id_visit_id",
                table: "patient_bills",
                columns: new[] { "tenant_id", "visit_id" },
                unique: true,
                filter: "visit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_last_name_first_name",
                table: "patients",
                columns: new[] { "tenant_id", "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_number",
                table: "patients",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patients_tenant_id_phone",
                table: "patients",
                columns: new[] { "tenant_id", "phone" });

            migrationBuilder.CreateIndex(
                name: "ix_price_items_tenant_id_name",
                table: "price_items",
                columns: new[] { "tenant_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bill_lines");

            migrationBuilder.DropTable(
                name: "patient_bills");

            migrationBuilder.DropTable(
                name: "price_items");

            migrationBuilder.DropTable(
                name: "patients");

            migrationBuilder.DropColumn(
                name: "next_bill_number",
                table: "clinic_profiles");

            migrationBuilder.DropColumn(
                name: "next_patient_number",
                table: "clinic_profiles");

            migrationBuilder.DropColumn(
                name: "next_receipt_number",
                table: "clinic_profiles");
        }
    }
}
