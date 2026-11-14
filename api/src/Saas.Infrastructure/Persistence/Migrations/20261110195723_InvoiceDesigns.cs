using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceDesigns : Migration
    {
        /// <summary>Existing businesses without an address get no line under their name (the column default covers the rest).</summary>
        public const string HeaderLineSql = "UPDATE business_settings SET header_line = 'none' WHERE address IS NULL OR btrim(address) = '';";

        /// <summary>
        /// Plans are seeded insert-only, so the existing InvoiceLink plans get <c>brand_images</c> (when missing) and the new feature
        /// copy here. Plans other than free, pro and business follow their premium-templates limit. Active subscriptions keep a
        /// snapshot of their plan's limits, so a snapshot without the key gets it the same way (from its own premium-templates
        /// value), so paying businesses keep their logo on documents. Safe to run more than once.
        /// </summary>
        public const string PlansSql = """
            UPDATE plans SET limits = limits || jsonb_build_object('brand_images',
                CASE code WHEN 'free' THEN 0 WHEN 'pro' THEN 1 WHEN 'business' THEN 1
                    ELSE CASE WHEN COALESCE((limits->>'premium_templates')::bigint, 0) <> 0 THEN 1 ELSE 0 END END)
            WHERE product = 'invoices' AND NOT (limits ? 'brand_images');
            UPDATE plans SET features = array_replace(array_replace(features, '3 templates', '5 templates'), 'All 5 templates', 'All 10 templates')
            WHERE product = 'invoices';
            UPDATE plans SET features = array_append(features, 'Your logo, signature and stamp on documents')
            WHERE product = 'invoices' AND code = 'pro' AND NOT ('Your logo, signature and stamp on documents' = ANY (features));
            UPDATE subscriptions s SET limits = s.limits || jsonb_build_object('brand_images',
                CASE WHEN COALESCE((s.limits->>'premium_templates')::bigint, 0) <> 0 THEN 1 ELSE 0 END)
            FROM plans p
            WHERE p.id = s.plan_id AND p.product = 'invoices' AND s.limits IS NOT NULL AND NOT (s.limits ? 'brand_images');
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "document_title",
                table: "business_settings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Invoice");

            migrationBuilder.AddColumn<string>(
                name: "font_style",
                table: "business_settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "modern");

            migrationBuilder.AddColumn<string>(
                name: "footer_text",
                table: "business_settings",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "header_line",
                table: "business_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "address");

            migrationBuilder.AddColumn<string>(
                name: "logo_size",
                table: "business_settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "medium");

            migrationBuilder.AddColumn<string>(
                name: "registration_number",
                table: "business_settings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "secondary_color",
                table: "business_settings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_bank_details",
                table: "business_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_registration_number",
                table: "business_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_signature",
                table: "business_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "show_tax_id",
                table: "business_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_image_url",
                table: "business_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_name",
                table: "business_settings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_role",
                table: "business_settings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stamp_image_url",
                table: "business_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tagline",
                table: "business_settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(HeaderLineSql);
            migrationBuilder.Sql(PlansSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "document_title",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "font_style",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "footer_text",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "header_line",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "logo_size",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "registration_number",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "secondary_color",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "show_bank_details",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "show_registration_number",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "show_signature",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "show_tax_id",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "signature_image_url",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "signature_name",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "signature_role",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "stamp_image_url",
                table: "business_settings");

            migrationBuilder.DropColumn(
                name: "tagline",
                table: "business_settings");
        }
    }
}
