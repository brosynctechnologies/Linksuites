using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Care1Setup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "clinical_role",
                table: "invitations",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "care_staff_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinical_role = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    licence_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    signature_key = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_staff_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_care_staff_profiles_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_care_staff_profiles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clinic_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    logo_key = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    patient_prefix = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    paystack_secret_key_protected = table.Column<string>(type: "text", nullable: true),
                    paystack_previous_key_protected = table.Column<string>(type: "text", nullable: true),
                    previous_key_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispenses_drugs = table.Column<bool>(type: "boolean", nullable: false),
                    min_deposit_minor = table.Column<long>(type: "bigint", nullable: false),
                    low_deposit_threshold_minor = table.Column<long>(type: "bigint", nullable: false),
                    registration_price_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consultation_price_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinic_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_clinic_profiles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_care_staff_profiles_membership_id",
                table: "care_staff_profiles",
                column: "membership_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_care_staff_profiles_tenant_id_user_id",
                table: "care_staff_profiles",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clinic_profiles_slug",
                table: "clinic_profiles",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clinic_profiles_tenant_id",
                table: "clinic_profiles",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_staff_profiles");

            migrationBuilder.DropTable(
                name: "clinic_profiles");

            migrationBuilder.DropColumn(
                name: "clinical_role",
                table: "invitations");
        }
    }
}
