namespace Saas.Infrastructure.Persistence;

/// <summary>Data steps run by the Wave 2 migration, kept here so tests can run them against rows as they were.</summary>
public static class Wave2DataFixes
{
    /// <summary>
    /// Active InvoiceLink subscriptions keep a snapshot of their plan's feature copy, so the InvoiceDesigns copy change
    /// ("All 5 templates" to "All 10 templates", "3 templates" to "5 templates") is applied to the snapshots too.
    /// Safe to run more than once.
    /// </summary>
    public const string SubscriptionFeaturesSql = """
        UPDATE subscriptions s
        SET features = array_replace(array_replace(s.features, '3 templates', '5 templates'), 'All 5 templates', 'All 10 templates')
        FROM plans p
        WHERE p.id = s.plan_id AND p.product = 'invoices' AND s.features IS NOT NULL
          AND (s.features && ARRAY['3 templates', 'All 5 templates']::text[]);
        """;
}
