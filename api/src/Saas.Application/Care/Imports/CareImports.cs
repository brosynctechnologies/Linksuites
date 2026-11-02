using Saas.Application.School.Imports;

namespace Saas.Application.Care.Imports;

/// <summary>
/// What the CareLink imports share. Imports never take clinical data: every care importer reads only its declared columns
/// (<see cref="IImportDeclaredColumnsOnly"/>), so any other column, clinical or not, is dropped before the rows are held and never stored.
/// A dropped heading that looks clinical gets one extra preview note.
/// </summary>
public static class CareImports
{
    public const string ClinicalIgnored =
        "Clinical details are never imported ({0}). Doctors and nurses record allergies, conditions and notes on the patient's record.";

    /// <summary>Heading words that mark a clinical column (singular; a plural heading folds to these).</summary>
    private static readonly HashSet<string> ClinicalWords = new(StringComparer.Ordinal)
    {
        "allergy", "allergies", "condition", "diagnosis", "diagnoses", "medication", "drug", "note", "prescription", "history", "complaint",
        "treatment", "result", "symptom", "vital", "blood", "genotype", "dose", "dosage", "examination", "plan", "clinical", "medical", "disease",
        "illness", "problem", "test", "lab",
    };

    /// <summary>True when a heading (as the reader keys it: lower case, underscores) has a clinical word.</summary>
    public static bool IsClinical(string heading) =>
        heading.Split(['_', ' '], StringSplitOptions.RemoveEmptyEntries).Any(word =>
            ClinicalWords.Contains(word) || (word.EndsWith('s') && ClinicalWords.Contains(word[..^1])));

    /// <summary>The note for the dropped headings that look clinical, or none.</summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<string> dropped)
    {
        var clinical = dropped.Where(IsClinical).ToList();
        if (clinical.Count == 0) return [];
        var names = clinical.Count == 1 ? clinical[0] : $"{string.Join(", ", clinical.Take(clinical.Count - 1))} and {clinical[^1]}";
        return [string.Format(System.Globalization.CultureInfo.InvariantCulture, ClinicalIgnored, names)];
    }

    /// <summary>Spaces trimmed and runs of spaces as one; lower case for matching.</summary>
    public static string Key(string? value) => string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
