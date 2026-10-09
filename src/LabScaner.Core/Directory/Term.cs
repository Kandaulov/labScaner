namespace LabScaner.Core.Directory;

/// <summary>Положение семестра относительно сегодняшней даты.</summary>
public enum TermState
{
    Past,
    Current,
    Future,
}

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

    /// <summary>Год начала учебного года: для «2026-2027» — 2026.</summary>
    public int StartYear => int.Parse(AcademicYear.AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Границы семестра вместе с сессией: осенний — 1 сентября … 31 января, весенний — 1 февраля … 31 августа.
    /// </summary>
    public DateOnly PeriodStart => Season == TermSeason.Autumn ? new DateOnly(StartYear, 9, 1) : new DateOnly(StartYear + 1, 2, 1);

    public DateOnly PeriodEnd => Season == TermSeason.Autumn ? new DateOnly(StartYear + 1, 1, 31) : new DateOnly(StartYear + 1, 8, 31);

    public TermState StateOn(DateOnly today) =>
        today < PeriodStart ? TermState.Future : today > PeriodEnd ? TermState.Past : TermState.Current;

    /// <summary>«2026-2027, осенний».</summary>
    public string Title => $"{AcademicYear}, {(Season == TermSeason.Autumn ? "осенний" : "весенний")}";

    public void SetCalendar(DateOnly creditWeekStart, DateOnly sessionStart)
    {
        if (sessionStart < creditWeekStart)
        {
            throw new ArgumentException("Сессия не может начинаться раньше зачётной недели.", nameof(sessionStart));
        }

        if (AcademicYear.Length > 0 && (creditWeekStart < PeriodStart || sessionStart > PeriodEnd))
        {
            throw new ArgumentException(
                $"Даты должны попадать в семестр: с {PeriodStart:dd.MM.yyyy} по {PeriodEnd:dd.MM.yyyy}.", nameof(creditWeekStart));
        }

        CreditWeekStart = creditWeekStart;
        SessionStart = sessionStart;
    }
}
