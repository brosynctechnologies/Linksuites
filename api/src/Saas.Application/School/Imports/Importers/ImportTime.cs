using System.Globalization;

namespace Saas.Application.School.Imports.Importers;

/// <summary>Times of day in a file: "09:00", "9:00", "09:00:00", or an Excel time saved as a number (0.375 is 09:00).</summary>
internal static class ImportTime
{
    private static readonly string[] Formats = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"];

    /// <summary>The cell as "HH:mm" when it is a time, else the cell as it is (so the screen's own check words the error).</summary>
    public static string Normalise(string? cell)
    {
        var text = (cell ?? "").Trim();
        if (TimeOnly.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var serial) && serial is > 0 and < 1)
        {
            var minutes = (int)Math.Round(serial * 24 * 60, MidpointRounding.AwayFromZero);
            if (minutes < 24 * 60) return new TimeOnly(minutes / 60, minutes % 60).ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        return text;
    }
}
