using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only (no schema change): InvoiceLink subscription snapshots get the feature copy the InvoiceDesigns migration gave
    /// the plans ("All 10 templates", "5 templates"). See <see cref="Wave2DataFixes.SubscriptionFeaturesSql"/>. Expense
    /// receipts move to private storage in the invoice job instead (ExpenseReceiptMigration), because that copies files.
    /// </summary>
    public partial class Wave2InvoiceSubscriptionCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Wave2DataFixes.SubscriptionFeaturesSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old copy described templates that no longer match the plans; it is not restored.
        }
    }
}
