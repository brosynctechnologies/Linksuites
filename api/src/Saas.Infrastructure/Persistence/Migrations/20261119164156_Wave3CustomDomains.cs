using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wave3CustomDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "custom_domain",
                table: "tenants",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "custom_domain_cert_failed_at",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "custom_domain_checked_at",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_domain_error",
                table: "tenants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "custom_domain_live_at",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_domain_status",
                table: "tenants",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "custom_domain_verified_at",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(ShowCustomDomainSql);
        }

        /// <summary>
        /// HideCustomDomain in reverse, now that custom domains work: "Custom domain" goes back on every OrderLink plan whose limits include
        /// it (Pro), and on the frozen copies of subscriptions whose own limits include it. Safe to run twice.
        /// </summary>
        public const string ShowCustomDomainSql = """
            UPDATE plans SET features = array_append(features, 'Custom domain')
            WHERE product = 'whatsapp-orders' AND COALESCE((limits ->> 'custom_domain')::bigint, 0) <> 0
              AND NOT ('Custom domain' = ANY (features));
            UPDATE subscriptions s SET features = array_append(s.features, 'Custom domain')
            FROM plans p
            WHERE s.plan_id = p.id AND p.product = 'whatsapp-orders' AND s.features IS NOT NULL
              AND COALESCE((s.limits ->> 'custom_domain')::bigint, 0) <> 0 AND NOT ('Custom domain' = ANY (s.features));
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_domain_cert_failed_at",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "custom_domain_checked_at",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "custom_domain_error",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "custom_domain_live_at",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "custom_domain_status",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "custom_domain_verified_at",
                table: "tenants");

            migrationBuilder.AlterColumn<string>(
                name: "custom_domain",
                table: "tenants",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(253)",
                oldMaxLength: 253,
                oldNullable: true);
        }
    }
}
