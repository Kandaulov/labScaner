using LabScaner.Core.Abstractions;
using LabScaner.Core.Directory;
using LabScaner.Core.Teaching;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Pages.Journal;

/// <summary>
/// Журнал группы (ADR-028): студенты × работы предмета в семестре. Пока без сдач — ячейки пустые;
/// темы курсовых правятся прямо в журнале (ADR-025).
/// </summary>
public sealed class IndexModel(LabScanerDbContext db, IClock clock) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "st")]
    public int? SubjectTermId { get; set; }

    [BindProperty(SupportsGet = true, Name = "group")]
    public int? GroupId { get; set; }

    /// <summary>Все предметы в семестрах преподавателя — для переключателя.</summary>
    public IReadOnlyList<SubjectTerm> SubjectTerms { get; private set; } = [];

    public SubjectTerm? Current { get; private set; }

    public Group? Group { get; private set; }

    public IReadOnlyList<Student> Students { get; private set; } = [];

    /// <summary>Лабораторные по номеру, затем курсовая.</summary>
    public IReadOnlyList<Assignment> Works { get; private set; } = [];

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, Navigation.AppTime.Zone).DateTime);

    public static string Label(SubjectTerm st) => $"{st.Subject!.Code} · {st.Term!.Title} · {st.StudySemester} сем";

    public async Task OnGetAsync()
    {
        var all = await db.SubjectTerms.AsNoTracking()
            .Include(s => s.Subject).Include(s => s.Term).Include(s => s.Groups)
            .AsSplitQuery()
            .ToListAsync();
        SubjectTerms = [.. all
            .OrderByDescending(s => s.Term!.PeriodStart)
            .ThenByDescending(s => s.Subject!.IsActive)
            .ThenBy(s => s.Subject!.Code, StringComparer.CurrentCulture)];
        var picked = SubjectTerms.FirstOrDefault(s => s.Id == SubjectTermId)
            ?? SubjectTerms.FirstOrDefault(s => s.Term!.StateOn(Today) == TermState.Current && s.Subject!.IsActive)
            ?? (SubjectTerms.Count > 0 ? SubjectTerms[0] : null);
        if (picked is null)
        {
            return;
        }

        Current = await db.SubjectTerms.AsNoTracking()
            .Include(s => s.Subject).Include(s => s.Term).Include(s => s.Groups)
            .Include(s => s.Assignments).Include(s => s.Topics)
            .AsSplitQuery()
            .SingleAsync(s => s.Id == picked.Id);
        SubjectTermId = Current.Id;
        Works = Current.Coursework is { } coursework ? [.. Current.Labs, coursework] : [.. Current.Labs];

        var groups = Current.Groups.OrderBy(g => g.Name, StringComparer.Ordinal).ToList();
        Group = groups.FirstOrDefault(g => g.Id == GroupId) ?? (groups.Count > 0 ? groups[0] : null);
        GroupId = Group?.Id;
        if (Group is null)
        {
            return;
        }

        var students = await db.Students.AsNoTracking().Where(s => s.GroupId == Group.Id && s.IsActive).ToListAsync();
        Students = [.. students
            .OrderBy(s => s.LastName, StringComparer.CurrentCulture)
            .ThenBy(s => s.FirstName, StringComparer.CurrentCulture)
            .ThenBy(s => s.MiddleName, StringComparer.CurrentCulture)];
    }

    /// <summary>Форма правки темы курсовой в ячейке (htmx).</summary>
    public async Task<IActionResult> OnGetTopicAsync(int student)
    {
        var (st, s) = await FindAsync(student);
        return st is null || s is null ? NotFound() : Partial("_TopicEdit", TopicCell.Of(st, s));
    }

    /// <summary>Отмена правки — ячейка как была.</summary>
    public async Task<IActionResult> OnGetTopicCellAsync(int student)
    {
        var (st, s) = await FindAsync(student);
        return st is null || s is null ? NotFound() : Partial("_TopicCell", TopicCell.Of(st, s));
    }

    public async Task<IActionResult> OnPostTopicAsync(int student, string? topic)
    {
        var (st, s) = await FindAsync(student);
        if (st is null || s is null || st.Coursework is null)
        {
            return NotFound();
        }

        try
        {
            st.SetTopic(s.Id, topic);
            await db.SaveChangesAsync();
        }
        catch (ArgumentException ex)
        {
            var error = ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", "", StringComparison.Ordinal);
            return Partial("_TopicEdit", TopicCell.Of(st, s) with { Topic = topic, Error = error });
        }

        return Partial("_TopicCell", TopicCell.Of(st, s));
    }

    /// <summary>Предмет в семестре преподавателя и студент из его группы — иначе ничего.</summary>
    private async Task<(SubjectTerm? SubjectTerm, Student? Student)> FindAsync(int studentId)
    {
        var st = await db.SubjectTerms
            .Include(s => s.Groups).Include(s => s.Assignments).Include(s => s.Topics)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == SubjectTermId);
        var student = st is null ? null : await db.Students.SingleOrDefaultAsync(s => s.Id == studentId);
        return student is not null && st!.Groups.Any(g => g.Id == student.GroupId) ? (st, student) : (st, null);
    }
}

/// <summary>Ячейка «Тема курсовой» в строке студента.</summary>
public sealed record TopicCell(int SubjectTermId, int StudentId, string StudentName, string? Topic, string? Error = null)
{
    public static TopicCell Of(SubjectTerm st, Student s) => new(st.Id, s.Id, s.Name.WithInitials, st.TopicOf(s.Id));
}
