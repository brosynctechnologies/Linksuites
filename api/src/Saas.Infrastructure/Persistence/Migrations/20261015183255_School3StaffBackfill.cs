using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class School3StaffBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-off: give every member of a school that predates staff profiles one, with a title that follows their role.
            migrationBuilder.Sql("""
                INSERT INTO staff_profiles (id, membership_id, user_id, job_title, created_at, tenant_id)
                SELECT gen_random_uuid(), m.id, m.user_id,
                       CASE m.role WHEN 'Owner' THEN 'Proprietor' WHEN 'Admin' THEN 'School admin' WHEN 'Accountant' THEN 'Bursar' ELSE 'Teacher' END,
                       now(), m.tenant_id
                FROM memberships m
                JOIN tenants t ON t.id = m.tenant_id
                WHERE t.product = 'school'
                  AND NOT EXISTS (SELECT 1 FROM staff_profiles p WHERE p.membership_id = m.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
