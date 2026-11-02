using Microsoft.EntityFrameworkCore;
using Npgsql;
using Saas.Infrastructure.Bookings;

namespace Saas.IntegrationTests;

/// <summary>Database errors are recognised however deeply EF wraps them (e.g. inside its "transient failure" exception).</summary>
public sealed class DbErrorsTests
{
    private static Exception Wrapped(string sqlState) =>
        new InvalidOperationException("An exception has been raised that is likely due to a transient failure.",
            new DbUpdateException("An error occurred while saving the entity changes.", new PostgresException("boom", "ERROR", "ERROR", sqlState)));

    [Fact]
    public void Deeply_wrapped_conflicts_are_recognised()
    {
        var errors = new PostgresDbErrors();
        Assert.True(errors.IsExclusionViolation(Wrapped(PostgresErrorCodes.ExclusionViolation)));
        Assert.True(errors.IsUniqueViolation(Wrapped(PostgresErrorCodes.UniqueViolation)));
        Assert.True(errors.IsConcurrencyConflict(Wrapped(PostgresErrorCodes.DeadlockDetected)));
        Assert.False(errors.IsConcurrencyConflict(Wrapped(PostgresErrorCodes.UndefinedTable)));
    }
}
