namespace LabScaner.Core.Subjects;

/// <summary>Итоговая аттестация по предмету (определяет графу журнала и вид автомата, ADR-013).</summary>
public enum FinalAssessment
{
    /// <summary>Экзамен — автомат «5 (отлично)».</summary>
    Exam,

    /// <summary>Зачёт — автомат «зачтено».</summary>
    Pass,

    /// <summary>Дифференцированный зачёт — автомат «5».</summary>
    GradedPass,
}

public static class FinalAssessments
{
    public static string Title(this FinalAssessment value) => value switch
    {
        FinalAssessment.Exam => "Экзамен",
        FinalAssessment.Pass => "Зачёт",
        FinalAssessment.GradedPass => "Дифференцированный зачёт",
        _ => value.ToString(),
    };

    /// <summary>Что ставится автоматом.</summary>
    public static string AutomatMark(this FinalAssessment value) => value == FinalAssessment.Pass ? "зачтено" : "5";
}
