namespace LabScaner.Web.Prototype;

/// <summary>Ячейка журнала для отрисовки: класс статуса, содержимое, подсказка, пометки (макет «Статусы ячейки»).</summary>
public sealed record CellView(
    int StudentId,
    int Lab,
    string Css,
    string Main,
    string Icon,
    string Title,
    string Aria,
    bool Notified,
    int Comments,
    bool Resubmitted,
    bool Oob = false);

public static class JournalView
{
    public static string WorkName(int lab) => lab == 0 ? "Курсовая" : $"Лаб №{lab}";

    public static DateOnly Deadline(int lab) => lab == 0 ? JournalPrototypeStore.CourseworkDeadline : JournalPrototypeStore.LabDeadline;

    public static CellView Cell(ProtoStudent s, int lab)
    {
        var w = lab == 0 ? s.Coursework : s.Labs[lab - 1];
        var deadline = Deadline(lab);
        var (css, main, icon, label) = w.Status switch
        {
            CellStatus.NotSubmitted => ("none", "—", "", "не сдано"),
            CellStatus.Processing => ("processing", "", "proc", "в обработке"),
            CellStatus.ManualReview => ("manual", "", "warn", "нужна ручная проверка"),
            CellStatus.Returned => ("back", "", "back", "на доработку"),
            CellStatus.AwaitingReview when !w.AiEnabled => ("ai", "•", "", "ждёт проверки, ИИ выкл."),
            CellStatus.AwaitingReview => ("ai", w.AiGrade ?? "", "ai", $"ждёт проверки · ИИ предложил {w.AiGrade}"),
            CellStatus.Accepted when JournalPrototypeStore.AcceptedOnTime(w, deadline) => ("ok", w.Grade == "ОК" ? "✓" : w.Grade ?? "✓", "", "принято в срок"),
            CellStatus.Accepted => ("late", w.Grade == "ОК" ? "✓" : w.Grade ?? "✓", "clock", "принято после срока"),
            _ => ("none", "—", "", ""),
        };

        var when = w.Status switch
        {
            CellStatus.NotSubmitted => "",
            CellStatus.Accepted => " · принято " + ProtoFormat.Day(w.AcceptedAt),
            _ => " · сдано " + ProtoFormat.Day(w.SubmittedAt),
        };
        var version = w.Version > 1 ? $" · версия v{w.Version}" : "";
        var comments = w.History.Count(IsComment);
        return new CellView(
            s.Id,
            lab,
            css,
            main,
            icon,
            label + when + version,
            $"{s.ShortName}, {WorkName(lab)}: {label}",
            w.Notified,
            comments,
            w.Version > 1);
    }

    public static string Totals(IEnumerable<ProtoStudent> students, int lab)
    {
        var works = students.Select(s => lab == 0 ? s.Coursework : s.Labs[lab - 1]).ToList();
        var submitted = works.Count(w => w.Status != CellStatus.NotSubmitted);
        var accepted = works.Count(w => w.Status == CellStatus.Accepted);
        return $"{accepted} / {submitted}";
    }

    public static bool NeedsReview(ProtoStudent s) =>
        s.Labs.Append(s.Coursework).Any(w => w.Status is CellStatus.AwaitingReview or CellStatus.ManualReview);

    public static bool IsDebtor(ProtoStudent s) =>
        s.Labs.Any(w => w.Status is CellStatus.NotSubmitted or CellStatus.Returned);

    public static bool HasComments(ProtoStudent s) =>
        s.Labs.Append(s.Coursework).Any(w => w.History.Any(IsComment));

    /// <summary>Комментарий — замечание при возврате или заметка для себя; отметки о принятии не считаются.</summary>
    public static bool IsComment(ProtoComment c) =>
        c.Author == "note" || (c.Author == "teacher" && c.Text.StartsWith("На доработку", StringComparison.Ordinal));

    public static string FinalText(ProtoStudent s) => s.Exam switch
    {
        null => "—",
        _ when s.ExamByAutomat => $"{s.Exam} · авт.",
        _ => $"{(s.ExamDate is { } d ? ProtoFormat.Day(d) : "")} · {s.Exam}",
    };
}
