using Saas.Domain.School;

namespace Saas.Application.School;

/// <summary>The Nigerian defaults a school starts from. Every value is editable once applied.</summary>
public static class SchoolTemplates
{
    public sealed record LevelTemplate(string Name, Stage Stage, bool HasStudentLogins, AssessmentMode AssessmentMode);
    public sealed record SubjectTemplate(string Name, string Code);
    public sealed record ComponentTemplate(string Name, decimal MaxScore);
    public sealed record BandTemplate(decimal Min, decimal Max, string Grade, string Remark);

    /// <summary>Creche, two nursery years, six primary years, three JSS and three SSS. Primary, JSS and SSS students sign in themselves. Creche and Nursery use developmental cards.</summary>
    public static IReadOnlyList<LevelTemplate> Levels { get; } =
    [
        new("Creche", Stage.Creche, false, AssessmentMode.Developmental),
        new("Nursery 1", Stage.Nursery, false, AssessmentMode.Developmental), new("Nursery 2", Stage.Nursery, false, AssessmentMode.Developmental),
        new("Primary 1", Stage.Primary, true, AssessmentMode.Numeric), new("Primary 2", Stage.Primary, true, AssessmentMode.Numeric), new("Primary 3", Stage.Primary, true, AssessmentMode.Numeric),
        new("Primary 4", Stage.Primary, true, AssessmentMode.Numeric), new("Primary 5", Stage.Primary, true, AssessmentMode.Numeric), new("Primary 6", Stage.Primary, true, AssessmentMode.Numeric),
        new("JSS 1", Stage.Jss, true, AssessmentMode.Numeric), new("JSS 2", Stage.Jss, true, AssessmentMode.Numeric), new("JSS 3", Stage.Jss, true, AssessmentMode.Numeric),
        new("SSS 1", Stage.Sss, true, AssessmentMode.Numeric), new("SSS 2", Stage.Sss, true, AssessmentMode.Numeric), new("SSS 3", Stage.Sss, true, AssessmentMode.Numeric),
    ];

    public static IReadOnlyList<SubjectTemplate> Subjects { get; } =
    [
        new("English Language", "ENG"), new("Mathematics", "MTH"), new("Basic Science", "BSC"), new("Basic Technology", "BTE"),
        new("Social Studies", "SOS"), new("Civic Education", "CVE"), new("Christian Religious Studies", "CRS"),
        new("Islamic Religious Studies", "IRS"), new("Agricultural Science", "AGR"), new("Computer Studies", "CMP"),
        new("French", "FRN"), new("Yoruba", "YOR"), new("Igbo", "IGB"), new("Hausa", "HAU"),
        new("Cultural and Creative Arts", "CCA"), new("Physical and Health Education", "PHE"), new("Home Economics", "HEC"),
        new("Business Studies", "BUS"), new("Physics", "PHY"), new("Chemistry", "CHM"), new("Biology", "BIO"),
        new("Further Mathematics", "FMT"), new("Economics", "ECO"), new("Government", "GOV"), new("Literature in English", "LIT"),
        new("Geography", "GEO"), new("Commerce", "COM"), new("Financial Accounting", "ACC"), new("Technical Drawing", "TDR"),
    ];

    public static IReadOnlyList<ComponentTemplate> Components { get; } = [new("CA1", 20), new("CA2", 20), new("Exam", 60)];

    public static IReadOnlyList<BandTemplate> Bands { get; } =
    [
        new(75, 100, "A1", "Excellent"), new(70, 74, "B2", "Very good"), new(65, 69, "B3", "Good"),
        new(60, 64, "C4", "Credit"), new(55, 59, "C5", "Credit"), new(50, 54, "C6", "Credit"),
        new(45, 49, "D7", "Pass"), new(40, 44, "E8", "Pass"), new(0, 39, "F9", "Fail"),
    ];

    /// <summary>School-default psychomotor items, in the order a class teacher rates them.</summary>
    public static IReadOnlyList<string> Psychomotor { get; } =
    [
        "Handwriting", "Drawing", "Hand coordination", "Use of tools", "Physical coordination", "Practical tasks",
    ];

    /// <summary>School-default affective items, in the order a class teacher rates them.</summary>
    public static IReadOnlyList<string> Affective { get; } =
    [
        "Appearance / neatness", "Punctuality", "Honesty", "Cooperation", "Conduct in class",
    ];

    /// <summary>School-default developmental areas, in the order a class teacher rates them.</summary>
    public static IReadOnlyList<string> DevelopmentalAreas { get; } =
    [
        "Language", "Early numeracy", "Social", "Self-care", "Creativity", "Motor", "Participation", "Emotional",
    ];

    public sealed record ScalePoint(string Code, string Label);

    /// <summary>School-default developmental scale. The label is also the comment-bank match key.</summary>
    public static IReadOnlyList<ScalePoint> DevelopmentalScale { get; } =
    [
        new("excellent", "Excellent"),
        new("very-good", "Very Good"),
        new("good", "Good"),
        new("developing", "Developing"),
        new("needs-support", "Needs Support"),
    ];

    public sealed record CommentTemplate(CommentScope Scope, string MatchKey, string Text);

    /// <summary>A few overall lines to start a school. Subject lines come from <see cref="Bands"/> (the grade is the match key, the remark is the text).</summary>
    public static IReadOnlyList<CommentTemplate> OverallComments { get; } =
    [
        new(CommentScope.TeacherOverall, "A1", "A strong term. Keep working with the same care."),
        new(CommentScope.TeacherOverall, "B2", "A good term. A little more practice will lift the next one."),
        new(CommentScope.TeacherOverall, "C6", "Steady effort this term. More time on the weaker subjects will help."),
        new(CommentScope.TeacherOverall, "F9", "This term was difficult. Extra practice at home will help next term."),
        new(CommentScope.PrincipalOverall, "A1", "The school is pleased with this term's work. Continue in the same way."),
        new(CommentScope.PrincipalOverall, "B3", "A good effort this term. Keep building on it."),
        new(CommentScope.PrincipalOverall, "F9", "Please give more attention to school work next term."),
    ];

    /// <summary>One line per developmental scale label. Suggest matches the label the child was given this term.</summary>
    public static IReadOnlyList<CommentTemplate> DevelopmentalComments { get; } =
    [
        new(CommentScope.Developmental, "Excellent", "Excellent progress this term. Keep offering the same encouragement."),
        new(CommentScope.Developmental, "Very Good", "Very good progress this term. A little more practice will help."),
        new(CommentScope.Developmental, "Good", "Good progress this term. Steady support at home will help."),
        new(CommentScope.Developmental, "Developing", "Still developing in some areas. More practice at home will help."),
        new(CommentScope.Developmental, "Needs Support", "Needs more support in some areas. Extra practice at home will help next term."),
    ];
}
