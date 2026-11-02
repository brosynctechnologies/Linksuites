namespace Saas.Application.Care;

/// <summary>
/// The prescription allergy check (Task 7 ruling I3), as one pure function. It is an aid, not a guarantee: it matches drug names only.
/// <list type="bullet">
/// <item>Both sides are normalised: lower case, letters and digits only, "ph" read as "f" (so Sulpha = Sulfa, Co-trimoxazole =
/// Cotrimoxazole). The salt words sulfate and sulphate are dropped (ferrous sulfate is not a sulfonamide).</item>
/// <item>An allergy entry that is wholly a "none" phrase (nil, none, NKDA, no known allergies…) is ignored; "No known allergies except
/// penicillin" is still checked.</item>
/// <item>A name match needs at least 3 characters on the shorter side. From 5 characters plain containment counts (Hydroxychloroquine
/// ~ Chloroquine); under 5 the shorter name must start at a word of the longer one, a hyphen starting a word ("Horse serum" doesn't
/// contain ORS; "Low-ORS" does).</item>
/// <item>A small class table links a class name to its members and brands: an allergen and a drug in the same class match.</item>
/// </list>
/// </summary>
public static class AllergyMatcher
{
    public const int MinimumLength = 3;

    private static readonly string[] SaltWords = ["sulfate", "sulfates"];
    /// <summary>Whole entries (normalised) that mean "no allergies". Only a whole entry counts: "No known allergies except penicillin" is checked.</summary>
    private static readonly HashSet<string> NonePhrases = new(StringComparer.Ordinal)
    {
        "nil", "none", "nkda", "nka", "nkfa", "na", "no", "nilknown", "nonknown", "noallergy", "noallergies", "nodrugallergy", "nodrugallergies",
        "noknownallergy", "noknownallergies", "noknowndrugallergy", "noknowndrugallergies", "noknownfoodordrugallergies", "noknown",
    };

    /// <summary>Below this length the shorter name must start at a word of the longer one (stops ORS inside "Horse serum").</summary>
    public const int WordStartBelow = 5;

    /// <summary>Allergy classes: each lists the class names and its members (generic and Nigerian brand names), stored normalised.</summary>
    private static readonly string[][] Classes =
    [
        Normalised("penicillin", "penicillins", "amoxicillin", "ampicillin", "augmentin", "co-amoxiclav", "amoxil", "cloxacillin", "flucloxacillin",
            "ampiclox", "benzylpenicillin", "phenoxymethylpenicillin"),
        Normalised("sulfa", "sulpha", "sulfonamide", "sulphonamide", "co-trimoxazole", "cotrimoxazole", "septrin", "sulfamethoxazole",
            "sulfadoxine", "fansidar"),
        Normalised("nsaid", "nsaids", "ibuprofen", "diclofenac", "aspirin", "naproxen", "piroxicam"),
    ];

    /// <summary>
    /// The recorded allergies that match any of the drugs, distinct and in the order recorded (as written, trimmed). Empty when none do.
    /// </summary>
    public static IReadOnlyList<string> Matches(IEnumerable<string> drugs, IEnumerable<string> allergies)
    {
        var drugNames = drugs.Select(Parse).Where(d => d.Text.Length > 0).ToList();
        var result = new List<string>();
        foreach (var raw in allergies)
        {
            var allergy = raw?.Trim() ?? "";
            if (allergy.Length == 0 || result.Contains(allergy, StringComparer.OrdinalIgnoreCase)) continue;
            var allergen = Parse(allergy);
            if (allergen.Text.Length == 0 || MeansNone(allergen.Text)) continue;
            if (drugNames.Any(d => NameMatch(d, allergen) || SameClass(d.Text, allergen.Text))) result.Add(allergy);
        }
        return result;
    }

    private sealed record Name(string Text, IReadOnlySet<int> WordStarts);

    /// <summary>
    /// The normalised name, with the offsets where each word starts. Words split on spaces and list punctuation; a hyphen also starts a
    /// word but its parts are joined in the text (Co-trimoxazole reads as cotrimoxazole, with words at "co" and "trimoxazole").
    /// </summary>
    private static Name Parse(string? value)
    {
        var text = "";
        var starts = new HashSet<int>();
        foreach (var word in (value ?? "").Split([' ', '\t', '/', ',', ';', '(', ')', '+', '&', '|', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            var w = Normalise(word);
            if (w.Length == 0 || SaltWords.Contains(w)) continue;
            starts.Add(text.Length);
            text += w;
        }
        return new Name(text, starts);
    }

    /// <summary>Lower case, letters and digits only, and "ph" read as "f".</summary>
    public static string Normalise(string value) =>
        new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()).Replace("ph", "f", StringComparison.Ordinal);

    private static string[] Normalised(params string[] names) => names.Select(Normalise).Distinct().ToArray();

    private static bool MeansNone(string allergen) => NonePhrases.Contains(allergen);

    /// <summary>
    /// The shorter name (3+ characters) appears in the longer one. Under <see cref="WordStartBelow"/> characters it must also start at one
    /// of the longer name's words; from 5 characters plain containment counts (Hydroxychloroquine ~ Chloroquine, Paracetamol-codeine ~ Codeine).
    /// </summary>
    private static bool NameMatch(Name drug, Name allergen)
    {
        var (shorter, longer) = drug.Text.Length <= allergen.Text.Length ? (drug, allergen) : (allergen, drug);
        if (shorter.Text.Length < MinimumLength) return false;
        if (shorter.Text.Length >= WordStartBelow) return longer.Text.Contains(shorter.Text, StringComparison.Ordinal);
        for (var at = longer.Text.IndexOf(shorter.Text, StringComparison.Ordinal); at >= 0;
             at = longer.Text.IndexOf(shorter.Text, at + 1, StringComparison.Ordinal))
            if (longer.WordStarts.Contains(at)) return true;
        return false;
    }

    private static bool SameClass(string drug, string allergen) =>
        Classes.Any(members => members.Any(m => allergen.Contains(m, StringComparison.Ordinal)) && members.Any(m => drug.Contains(m, StringComparison.Ordinal)));
}
