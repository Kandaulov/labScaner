using System.Globalization;

namespace LabScaner.Web.Prototype;

/// <summary>Статус ячейки журнала (ADR-017). В прототипе — без привязки к БД.</summary>
public enum CellStatus
{
    NotSubmitted,
    Processing,
    AwaitingReview,
    ManualReview,
    Accepted,
    Returned,
}

public sealed class ProtoComment(string author, DateTime at, string text)
{
    /// <summary>sys | ai | teacher | sent | note.</summary>
    public string Author { get; } = author;

    public DateTime At { get; } = at;

    public string Text { get; } = text;
}

public sealed class ProtoWork
{
    public CellStatus Status { get; set; }

    /// <summary>«5», «4», «3» или «ОК» — решение преподавателя.</summary>
    public string? Grade { get; set; }

    /// <summary>Оценка, которую предложил ИИ.</summary>
    public string? AiGrade { get; set; }

    public bool AiEnabled { get; set; } = true;

    public int Version { get; set; } = 1;

    public bool Notified { get; set; }

    public bool InLetter { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public string? Remarks { get; set; }

    public List<ProtoComment> History { get; } = [];
}

public sealed class ProtoStudent
{
    public required int Id { get; init; }

    public required string FullName { get; init; }

    public string ShortName
    {
        get
        {
            var p = FullName.Split(' ');
            return $"{p[0]} {p[1][0]}. {p[2][0]}.";
        }
    }

    /// <summary>«Иванов И.И.» — как в имени файла на Диске (ADR-011).</summary>
    public string FileShortName
    {
        get
        {
            var p = FullName.Split(' ');
            return $"{p[0]} {p[1][0]}.{p[2][0]}.";
        }
    }

    public required ProtoWork[] Labs { get; init; }

    public required ProtoWork Coursework { get; init; }

    public string? Exam { get; set; }

    public DateOnly? ExamDate { get; set; }

    public bool ExamByAutomat { get; set; }
}

/// <summary>
/// Данные прототипа журнала (шаг 2.5): КорпИС, ИСТ-41, 25 студентов, 8 лаб, курсовая, экзамен.
/// Хранятся в памяти, сбрасываются при перезапуске. «Сегодня» — 14.12.2026, как на макетах (Б8).
/// </summary>
public sealed class JournalPrototypeStore
{
    public const int LabCount = 8;

    public static readonly DateTime Today = new(2026, 12, 14, 10, 0, 0);
    public static readonly DateOnly LabDeadline = new(2026, 12, 21);
    public static readonly DateOnly CourseworkDeadline = new(2027, 1, 11);

    private static readonly string[] _names =
    [
        "Абрамов Кирилл Андреевич", "Белова Анна Сергеевна", "Васильев Денис Олегович", "Гордеева Полина Игоревна",
        "Дмитриев Артём Павлович", "Егорова Софья Михайловна", "Жуков Никита Романович", "Зайцева Валерия Андреевна",
        "Иванов Иван Иванович", "Калинин Глеб Викторович", "Лебедева Мария Дмитриевна", "Морозова Дарья Сергеевна",
        "Никитин Егор Владимирович", "Орлова Ксения Евгеньевна", "Павлов Тимофей Ильич", "Романова Алиса Николаевна",
        "Смирнов Даниил Константинович", "Сидорова Анастасия Петровна", "Тарасов Илья Германович", "Устинова Елизавета Олеговна",
        "Фёдоров Матвей Сергеевич", "Харитонова Вероника Андреевна", "Цветков Арсений Денисович", "Шилова Ева Максимовна",
        "Яковлев Лев Артёмович",
    ];

    private readonly Lock _lock = new();
    private List<ProtoStudent> _students = Generate();

    public Lock SyncRoot => _lock;

    public IReadOnlyList<ProtoStudent> Students => _students;

    public ProtoStudent Student(int id) => _students.Single(s => s.Id == id);

    /// <summary>Работа студента: 1…8 — лабы, 0 — курсовая.</summary>
    public ProtoWork Work(int studentId, int lab) =>
        lab == 0 ? Student(studentId).Coursework : Student(studentId).Labs[lab - 1];

    public void Reset()
    {
        lock (_lock)
        {
            _students = Generate();
        }
    }

    public static bool AcceptedOnTime(ProtoWork work, DateOnly deadline) =>
        work.Status == CellStatus.Accepted && work.AcceptedAt is { } at && DateOnly.FromDateTime(at) <= deadline;

    /// <summary>Претендент на автомат (ADR-013): все лабы и курсовая приняты в срок.</summary>
    public static AutomatState Automat(ProtoStudent s)
    {
        if (s.ExamByAutomat)
        {
            return AutomatState.Given;
        }

        var labsOk = s.Labs.All(l => AcceptedOnTime(l, LabDeadline));
        if (!labsOk)
        {
            return AutomatState.None;
        }

        return AcceptedOnTime(s.Coursework, CourseworkDeadline) ? AutomatState.Candidate : AutomatState.WaitsCoursework;
    }

    private static List<ProtoStudent> Generate()
    {
        var strong = new HashSet<int> { 1, 5, 11, 16 };
        var result = new List<ProtoStudent>();
        for (var i = 0; i < _names.Length; i++)
        {
            var labs = new ProtoWork[LabCount];
            for (var j = 0; j < LabCount; j++)
            {
                labs[j] = MakeWork(i, j, Kind(i, j, strong));
            }

            var hc = i * 23 % 10;
            var cwKind = strong.Contains(i) ? (i == 11 ? "ai" : "ok") : hc < 2 ? "ok" : hc < 4 ? "ai" : hc < 5 ? "back" : "none";
            var student = new ProtoStudent
            {
                Id = i + 1,
                FullName = _names[i],
                Labs = labs,
                Coursework = MakeWork(i, 9, cwKind),
            };
            if (i == 5)
            {
                student.ExamByAutomat = true;
                student.Exam = "5";
                student.ExamDate = new DateOnly(2026, 12, 12);
            }

            result.Add(student);
        }

        return result;
    }

    private static string Kind(int i, int j, HashSet<int> strong)
    {
        if (i == 3 && j == 3)
        {
            return "ai";
        }

        if (strong.Contains(i))
        {
            return i == 11 && j == LabCount - 1 ? "ai" : "ok";
        }

        if (i == 8 && j == 2)
        {
            return "resub";
        }

        if (i == 17 && j == 1)
        {
            return "manual";
        }

        var h = ((i * 17) + (j * 31) + (i * j * 7)) % 100;
        return j switch
        {
            <= 2 => h < 74 ? "ok" : h < 82 ? "oklate" : h < 90 ? "back" : "resub",
            3 => h < 40 ? "ok" : h < 68 ? "ai" : h < 76 ? "processing" : h < 86 ? "back" : "none",
            4 => h < 22 ? "ok" : h < 46 ? "ai" : h < 50 ? "manual" : "none",
            5 => h < 10 ? "ok" : h < 22 ? "ai" : "none",
            _ => "none",
        };
    }

    private static ProtoWork MakeWork(int i, int j, string kind)
    {
        var h = ((i * 13) + (j * 29)) % 100;
        var grade = h % 3 == 1 ? "4" : "5";
        var submitted = new DateTime(2026, 9, 15, 10, 0, 0).AddDays((j == 9 ? 80 : j * 11) + (i % 6) + 3).AddMinutes(h * 7);
        var work = new ProtoWork { SubmittedAt = submitted };
        var file = j == 9 ? "курсовой" : $"Лаб №{j + 1}";
        work.History.Add(new ProtoComment("sys", submitted, $"Получено письмо, файл {file} сохранён на Диск"));

        switch (kind)
        {
            case "none":
                work.Status = CellStatus.NotSubmitted;
                work.SubmittedAt = null;
                work.History.Clear();
                break;
            case "processing":
                work.Status = CellStatus.Processing;
                break;
            case "manual":
                work.Status = CellStatus.ManualReview;
                work.History.Add(new ProtoComment("sys", submitted.AddMinutes(2), "Текст не распознан даже OCR — нужна ручная проверка"));
                break;
            case "ai":
                work.Status = CellStatus.AwaitingReview;
                work.AiGrade = h % 2 == 1 ? "4" : "5";
                work.History.Add(new ProtoComment("ai", submitted.AddMinutes(2), $"4 из 5 пунктов чек-листа. Предложено: {work.AiGrade}"));
                break;
            case "resub":
                work.Status = CellStatus.AwaitingReview;
                work.Version = 2;
                work.AiGrade = "4";
                work.History.Insert(0, new ProtoComment("teacher", submitted.AddDays(-12), "На доработку: не хватает выводов и модели TO-BE."));
                work.History.Insert(1, new ProtoComment("sent", submitted.AddDays(-12).AddMinutes(1), "Письмо студенту: на доработку, замечания…"));
                work.History.Add(new ProtoComment("ai", submitted.AddMinutes(2), "Версия v2: 4 из 5 пунктов. Предложено: 4"));
                break;
            case "back":
                work.Status = CellStatus.Returned;
                work.Notified = h % 4 != 0;
                work.Remarks = "Не хватает выводов, расчёты в таблице 2 не сходятся с текстом.";
                work.History.Add(new ProtoComment("teacher", submitted.AddDays(4), "На доработку: " + work.Remarks));
                break;
            default:
                work.Status = CellStatus.Accepted;
                work.Grade = grade;
                work.AcceptedAt = kind == "oklate" ? new DateTime(2026, 12, 22, 12, 0, 0) : submitted.AddDays(3);
                work.Notified = h % 4 != 0;
                work.History.Add(new ProtoComment("teacher", work.AcceptedAt.Value, $"Принято, оценка {grade}"));
                break;
        }

        return work;
    }
}

public enum AutomatState
{
    None,
    WaitsCoursework,
    Candidate,
    Given,
}

internal static class ProtoFormat
{
    private static readonly CultureInfo _ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Day(DateTime? at) => at?.ToString("dd.MM", _ru) ?? string.Empty;

    public static string DayTime(DateTime at) => at.ToString("dd.MM HH:mm", _ru);

    public static string Day(DateOnly at) => at.ToString("dd.MM", _ru);
}
