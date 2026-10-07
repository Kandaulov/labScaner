using LabScaner.Web.Prototype;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabScaner.Web.Pages.Prototype;

/// <summary>
/// Прототип журнала группы (шаг 2.5, ADR-027): проверка, хватает ли Razor Pages + htmx для самого
/// интерактивного экрана. Данные — в памяти, без БД.
/// </summary>
public sealed class JournalModel(JournalPrototypeStore store) : PageModel
{
    public const string AllFilter = "all";

    public JournalPrototypeStore Store => store;

    public string Filter { get; private set; } = AllFilter;

    public IReadOnlyList<ProtoStudent> Rows { get; private set; } = [];

    public void OnGet(string? filter)
    {
        Filter = filter ?? AllFilter;
        Rows = Apply(Filter);
    }

    public IReadOnlyList<ProtoStudent> Apply(string filter) => filter switch
    {
        "review" => [.. store.Students.Where(JournalView.NeedsReview)],
        "debtors" => [.. store.Students.Where(JournalView.IsDebtor)],
        "automat" => [.. store.Students.Where(s => JournalPrototypeStore.Automat(s) == AutomatState.Candidate)],
        "comments" => [.. store.Students.Where(JournalView.HasComments)],
        _ => store.Students,
    };

    public PartialViewResult OnGetQuick(int s, int l) =>
        Partial("_QuickMark", new WorkRef(store.Student(s), l));

    public PartialViewResult OnPostMark(int s, int l, string grade, bool inLetter)
    {
        lock (store.SyncRoot)
        {
            var work = store.Work(s, l);
            work.Status = CellStatus.Accepted;
            work.Grade = grade;
            work.AcceptedAt = JournalPrototypeStore.Today;
            work.InLetter = inLetter;
            work.History.Add(new ProtoComment("teacher", JournalPrototypeStore.Today, $"Быстрая отметка: {(grade == "ОК" ? "принято" : "оценка " + grade)}{(inLetter ? ", в письмо студенту" : "")}"));
        }

        return Partial("_CellUpdate", new WorkRef(store.Student(s), l));
    }

    public PartialViewResult OnGetPanel(int s, int l, string tab = "decision") =>
        Partial("_Panel", new PanelModel(store.Student(s), l, tab));

    public PartialViewResult OnPostDecide(int s, int l, string decision, string? grade, string? remarks, bool notify)
    {
        lock (store.SyncRoot)
        {
            var work = store.Work(s, l);
            work.Remarks = remarks;
            work.InLetter = notify;
            if (decision == "return")
            {
                work.Status = CellStatus.Returned;
                work.Grade = null;
                work.AcceptedAt = null;
                work.History.Add(new ProtoComment("teacher", JournalPrototypeStore.Today, "На доработку" + (string.IsNullOrWhiteSpace(remarks) ? "" : ": " + remarks)));
            }
            else
            {
                work.Status = CellStatus.Accepted;
                work.Grade = string.IsNullOrEmpty(grade) ? "ОК" : grade;
                work.AcceptedAt = JournalPrototypeStore.Today;
                work.History.Add(new ProtoComment("teacher", JournalPrototypeStore.Today, $"Принято, {(work.Grade == "ОК" ? "без оценки" : "оценка " + work.Grade)}" + (string.IsNullOrWhiteSpace(remarks) ? "" : ". " + remarks)));
            }
        }

        return Partial("_Panel", new PanelModel(store.Student(s), l, "decision", Updated: true));
    }

    public PartialViewResult OnPostNote(int s, int l, string? note)
    {
        if (!string.IsNullOrWhiteSpace(note))
        {
            lock (store.SyncRoot)
            {
                store.Work(s, l).History.Add(new ProtoComment("note", JournalPrototypeStore.Today, note.Trim()));
            }
        }

        return Partial("_Panel", new PanelModel(store.Student(s), l, "comments", Updated: true));
    }

    public PartialViewResult OnGetFinal(int s) => Partial("_FinalEdit", store.Student(s));

    public PartialViewResult OnPostFinal(int s, string? mark, DateOnly? date)
    {
        var student = store.Student(s);
        lock (store.SyncRoot)
        {
            student.Exam = string.IsNullOrEmpty(mark) ? null : mark;
            student.ExamDate = date;
            student.ExamByAutomat = false;
        }

        return Partial("_FinalCell", student);
    }

    public PartialViewResult OnPostSelection(int[]? selected) =>
        Partial("_Selection", Selection(selected));

    public PartialViewResult OnPostAutoDialog(int[]? selected) =>
        Partial("_AutoDialog", Selection(selected));

    public IActionResult OnPostAutoApply(int[]? selected)
    {
        lock (store.SyncRoot)
        {
            foreach (var student in Selection(selected).Candidates)
            {
                student.ExamByAutomat = true;
                student.Exam = "5";
                student.ExamDate = DateOnly.FromDateTime(JournalPrototypeStore.Today);
            }
        }

        Response.Headers["HX-Refresh"] = "true";
        return new NoContentResult();
    }

    public IActionResult OnPostReset()
    {
        store.Reset();
        return RedirectToPage();
    }

    private SelectionModel Selection(int[]? ids)
    {
        var selected = store.Students.Where(s => ids?.Contains(s.Id) == true).ToList();
        return new SelectionModel(selected);
    }
}

public sealed record WorkRef(ProtoStudent Student, int Lab)
{
    public ProtoWork Work => Lab == 0 ? Student.Coursework : Student.Labs[Lab - 1];
}

public sealed record PanelModel(ProtoStudent Student, int Lab, string Tab, bool Updated = false)
{
    public ProtoWork Work => Lab == 0 ? Student.Coursework : Student.Labs[Lab - 1];
}

public sealed record SelectionModel(IReadOnlyList<ProtoStudent> Selected)
{
    public IReadOnlyList<ProtoStudent> Candidates { get; } =
        [.. Selected.Where(s => JournalPrototypeStore.Automat(s) == AutomatState.Candidate)];

    public IReadOnlyList<ProtoStudent> Skipped { get; } =
        [.. Selected.Where(s => JournalPrototypeStore.Automat(s) is AutomatState.None or AutomatState.WaitsCoursework)];
}
