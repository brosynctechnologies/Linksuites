using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Course earnings become the shared ledger. Rename in place so existing earnings and balances survive.
            migrationBuilder.Sql("""
                ALTER TABLE tutor_earnings RENAME TO earnings;
                ALTER TABLE earnings RENAME CONSTRAINT pk_tutor_earnings TO pk_earnings;
                ALTER TABLE earnings RENAME CONSTRAINT fk_tutor_earnings_tenants_tenant_id TO fk_earnings_tenants_tenant_id;
                ALTER INDEX ix_tutor_earnings_tenant_id_available_at RENAME TO ix_earnings_tenant_id_available_at;
                DROP INDEX ix_tutor_earnings_order_id;
                ALTER TABLE earnings RENAME COLUMN order_id TO source_id;
                ALTER TABLE earnings ADD COLUMN source character varying(30) NOT NULL DEFAULT 'CourseOrder';
                ALTER TABLE earnings ALTER COLUMN source DROP DEFAULT;
                ALTER TABLE earnings ADD COLUMN description text NOT NULL DEFAULT '';
                UPDATE earnings e SET description = c.title FROM courses c WHERE c.id = e.course_id;
                ALTER TABLE earnings ALTER COLUMN course_id DROP NOT NULL;
                ALTER TABLE earnings ALTER COLUMN channel DROP NOT NULL;
                CREATE UNIQUE INDEX ix_earnings_source_source_id ON earnings (source, source_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM earnings WHERE source <> 'CourseOrder';
                DROP INDEX ix_earnings_source_source_id;
                ALTER TABLE earnings ALTER COLUMN channel SET NOT NULL;
                ALTER TABLE earnings ALTER COLUMN course_id SET NOT NULL;
                ALTER TABLE earnings DROP COLUMN description;
                ALTER TABLE earnings DROP COLUMN source;
                ALTER TABLE earnings RENAME COLUMN source_id TO order_id;
                CREATE UNIQUE INDEX ix_tutor_earnings_order_id ON earnings (order_id);
                ALTER INDEX ix_earnings_tenant_id_available_at RENAME TO ix_tutor_earnings_tenant_id_available_at;
                ALTER TABLE earnings RENAME CONSTRAINT fk_earnings_tenants_tenant_id TO fk_tutor_earnings_tenants_tenant_id;
                ALTER TABLE earnings RENAME CONSTRAINT pk_earnings TO pk_tutor_earnings;
                ALTER TABLE earnings RENAME TO tutor_earnings;
                """);
        }
    }
}
