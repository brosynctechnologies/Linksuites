using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HideCustomDomain : Migration
    {
        /// <summary>Takes "Custom domain" off OrderLink Pro until custom domains are built (roadmap wave 3).</summary>
        public const string Sql = """
            UPDATE plans SET features = array_remove(features, 'Custom domain')
            WHERE 'Custom domain' = ANY (features);
            UPDATE subscriptions SET features = array_remove(features, 'Custom domain')
            WHERE features IS NOT NULL AND 'Custom domain' = ANY (features);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
