using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomDomainOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "custom_domain_pending_since",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_domain_token",
                table: "tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(ExistingClaimsSql);
        }

        /// <summary>
        /// Claims made before the TXT proof existed proved nothing (any domain already served by the same nginx resolved here), so each
        /// gets a random code and goes back to PendingDns, with 7 days from now to add the TXT record before it is released. Live ones
        /// stop being served until they verify again. Safe to run twice (only rows without a code).
        /// </summary>
        public const string ExistingClaimsSql = """
            UPDATE tenants SET
                custom_domain_token = md5(random()::text || clock_timestamp()::text || id::text),
                custom_domain_status = 'PendingDns',
                custom_domain_pending_since = now(),
                custom_domain_verified_at = NULL,
                custom_domain_live_at = NULL,
                custom_domain_cert_failed_at = NULL,
                custom_domain_error = 'Add the TXT record below to show the domain is yours; we now check it before serving your store.'
            WHERE custom_domain IS NOT NULL AND custom_domain_token IS NULL;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_domain_pending_since",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "custom_domain_token",
                table: "tenants");
        }
    }
}
