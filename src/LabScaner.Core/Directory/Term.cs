namespace LabScaner.Core.Directory;

public enum TermSeason
{
    /// <summary>Осенний семестр.</summary>
    Autumn,

    /// <summary>Весенний семестр.</summary>
    Spring,
}

/// <summary>
/// Учебный семестр с календарём. Даты задают дедлайны по умолчанию (ADR-013):
/// лабораторные — начало зачётной недели, курсовая — начало сессии.
/// </summary>
public sealed class Term
{
    private Term()
    {
        AcademicYear = string.Empty;
    }

    public Term(int academicYearStart, TermSeason season, DateOnly creditWeekStart, DateOnly sessionStart)
    {
        if (academicYearStart is < 2000 or > 2100)
        {
            throw new ArgumentOutOfRangeException(nameof(academicYearStart), academicYearStart, "Год начала учебного года вне диапазона 2000–2100.");
        }

        AcademicYear = $"{academicYearStart}-{academicYearStart + 1}";
        Season = season;
        SetCalendar(creditWeekStart, sessionStart);
    }

    public int Id { get; private set; }

    /// <summary>«2026-2027».</summary>
    public string AcademicYear { get; private set; }

    public TermSeason Season { get; private set; }

    /// <summary>Начало зачётной недели — дедлайн лабораторных по умолчанию.</summary>
    public DateOnly CreditWeekStart { get; private set; }

    /// <summary>Начало сессии — дедлайн курсовой по умолчанию.</summary>
    public DateOnly SessionStart { get; private set; }

    /// <summary>«2026-2027, осенний».</summary>
    public string Title => $"{AcademicYear}, {(Season == TermSeason.Autumn ? "осенний" : "весенний")}";

    public void SetCalendar(DateOnly creditWeekStart, DateOnly sessionStart)
    {
        if (sessionStart < creditWeekStart)
        {
            throw new ArgumentException("Сессия не может начинаться раньше зачётной недели.", nameof(sessionStart));
        }

        CreditWeekStart = creditWeekStart;
        SessionStart = sessionStart;
    }
}
