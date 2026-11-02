using Saas.Application.Common;

namespace Saas.Application.Care;

/// <summary>
/// Clinical free text at rest (notes, histories, examinations, plans, nursing notes, discharge summaries, result text, allergies and
/// conditions): protected with the platform's data-protection key through <see cref="ISecretProtector"/>. Services call it explicitly on
/// write and read (no EF value converter), so the encryption is visible where the clinical data is handled.
/// Blank text is stored as "" and reads back as null.
/// </summary>
public sealed class ClinicalText(ISecretProtector protector)
{
    /// <summary>The stored form of <paramref name="plain"/>; "" for null or blank text.</summary>
    public string Protect(string? plain) => string.IsNullOrWhiteSpace(plain) ? "" : protector.Protect(plain);

    /// <summary>The plain text of a stored value; null for null or "".</summary>
    public string? Read(string? stored) => string.IsNullOrEmpty(stored) ? null : protector.Unprotect(stored);

    /// <summary>Protects each entry of a list of clinical text (allergies, conditions); blank entries are dropped.</summary>
    public List<string> ProtectAll(IEnumerable<string> plain) =>
        plain.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Protect).ToList();

    /// <summary>Reads back each entry of a protected list.</summary>
    public List<string> ReadAll(IEnumerable<string> stored) =>
        stored.Select(Read).OfType<string>().ToList();
}
