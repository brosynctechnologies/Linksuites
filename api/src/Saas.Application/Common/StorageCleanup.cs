using Microsoft.Extensions.Logging;

namespace Saas.Application.Common;

public static class StorageCleanup
{
    /// <summary>
    /// Deletes a file that the database no longer points at. A storage failure only leaves an orphaned file behind, so it is
    /// logged rather than turned into an error for a save that has already succeeded.
    /// </summary>
    public static async Task DeleteQuietlyAsync(this IFileStorage storage, string? key, ILogger logger, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(key)) return;
        try
        {
            await storage.DeleteAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not delete the replaced file {Key}; it is left orphaned", key);
        }
    }
}
