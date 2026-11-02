using System.Globalization;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// What the four fee imports (fee items, fee structure, payments, expenses) share: who may use them, money cells in naira and yes or no
/// cells. Fees follow the API's fee policy (<c>Policies.Reports</c>): the proprietor (Owner), the principal (Admin) and the bursar
/// (Accountant). A teacher is refused before anything is looked up, as the fee routes refuse them.
/// </summary>
public static class FeeImports
{
    public const string NotForTeachers = "Only the proprietor, principal and bursar manage fees.";
    public const string AmountHelp = "In naira, with up to two decimal places, for example 12500 or 12,500.50.";
    public const string NotAnAmount = "Enter the amount in naira, such as 12500 or 12,500.50.";
    /// <summary>Also what a number cell carrying a float artefact (0.30000000000000004 from a formula) gets: round the cell in Excel.</summary>
    public const string TooManyDecimals = "Enter the amount with at most two decimal places, for example 12500.50.";
    public const string AboveZero = "Enter an amount above zero.";
    public const string BadDate = "Enter the date as 2026-10-26 or 26/10/2026.";

    /// <summary>The largest amount a cell may hold: ₦100 trillion, far above anything a school records, and safely inside a long in kobo.</summary>
    private const int MaxNairaDigits = 14;

    /// <summary>The fee policy's roles: Owner, Admin and Accountant. Anyone else (a teacher) gets 403.</summary>
    public static void EnsureFeeRole(ICurrentUser user)
    {
        if (user.Role is not (MemberRole.Owner or MemberRole.Admin or MemberRole.Accountant)) throw new ForbiddenException(NotForTeachers);
    }

    /// <summary>
    /// A naira cell as kobo, exactly (no floating point): digits with an optional thousands separator in groups of three and up to two
    /// decimals, an optional ₦ in front. "12,500.50" is 1250050. Null with the reason when the cell is not such an amount (text, a minus
    /// sign, more than two decimals, "1,25"). Zero parses; the caller decides whether zero is allowed.
    /// </summary>
    public static long? ParseNaira(string? raw, out string? error)
    {
        error = null;
        var s = (raw ?? "").Trim();
        if (s.StartsWith('₦')) s = s[1..].TrimStart();
        if (s.Length == 0 || s.StartsWith('-') || s.StartsWith('+'))
        {
            error = s.StartsWith('-') ? AboveZero : NotAnAmount;
            return null;
        }

        var point = s.IndexOf('.');
        var whole = point < 0 ? s : s[..point];
        var fraction = point < 0 ? "" : s[(point + 1)..];
        if (point >= 0 && fraction.Length == 0) fraction = "0";

        if (whole.Contains(','))
        {
            var groups = whole.Split(',');
            // "0,500" is refused: a leading zero before a comma is a decimal comma (half a naira), never five hundred.
            if (groups[0].Length is < 1 or > 3 || groups[0].StartsWith('0') || groups.Skip(1).Any(g => g.Length != 3))
            {
                error = NotAnAmount;
                return null;
            }
            whole = string.Concat(groups);
        }
        if (whole.Length == 0) whole = "0";
        if (!whole.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit))
        {
            error = NotAnAmount;
            return null;
        }
        if (fraction.Length > 2)
        {
            error = TooManyDecimals;
            return null;
        }
        whole = whole.TrimStart('0');
        if (whole.Length > MaxNairaDigits)
        {
            error = NotAnAmount;
            return null;
        }
        var naira = whole.Length == 0 ? 0 : long.Parse(whole, NumberStyles.None, CultureInfo.InvariantCulture);
        var kobo = fraction.Length == 0 ? 0 : int.Parse(fraction.PadRight(2, '0'), NumberStyles.None, CultureInfo.InvariantCulture);
        return naira * 100 + kobo;
    }

    /// <summary>Kobo as a naira cell the parser reads back: 1250050 → "12500.50", 5000000 → "50000".</summary>
    public static string FormatNaira(long kobo)
    {
        var naira = (kobo / 100).ToString(CultureInfo.InvariantCulture);
        var rest = kobo % 100;
        return rest == 0 ? naira : $"{naira}.{rest.ToString("D2", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Yes or no (also y, n, true, false), ignoring case. Null when blank; false with <paramref name="ok"/> false when it is neither.</summary>
    public static bool? ParseYesNo(string? raw, out bool ok)
    {
        ok = true;
        switch ((raw ?? "").Trim().ToLowerInvariant())
        {
            case "": return null;
            case "yes" or "y" or "true": return true;
            case "no" or "n" or "false": return false;
            default:
                ok = false;
                return false;
        }
    }

    /// <summary>A date cell: ISO, day/month/year or an Excel serial, as the student import reads them.</summary>
    public static DateOnly? ParseDate(string? raw, out string? error)
    {
        error = null;
        if (!StudentImport.TryParseDate(raw, out var date))
        {
            error = BadDate;
            return null;
        }
        return date;
    }

    /// <summary>Lower case, runs of spaces as one: "School  bus" and "school bus" name the same fee item.</summary>
    public static string NameKey(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
