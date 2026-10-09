using LabScaner.Core.Abstractions;
using LabScaner.Core.Directory;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Pages.Groups;

/// <summary>
/// Справочник групп и студентов (ADR-016): общий для всех преподавателей, изменяет администратор.
/// </summary>
public sealed class IndexModel(LabScanerDbContext db, IClock clock) : PageModel
{
    public sealed record GroupTab(int Id, string Name, int Active);

    public IReadOnlyList<GroupTab> Groups { get; private set; } = [];

    public Group? Current { get; private set; }

    public IReadOnlyList<Student> Students { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public int? Group { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Inactive { get; set; }

    public bool IsAdmin => User.IsInRole(Roles.Admin);

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddGroupAsync(string? name)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var groupName = name is null ? null : GroupListParser.TryGroupName(name);
        if (groupName is null)
        {
            Error = "Название группы — буквы направления и номер, например «ИСТ-41».";
        }
        else if (await db.Groups.AnyAsync(g => g.NameKey == GroupName.Key(groupName)))
        {
            Error = $"Группа {groupName} уже есть.";
        }
        else
        {
            var group = new Group(groupName);
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            Group = group.Id;
            Message = $"Добавлена группа {group.Name}.";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddStudentAsync(string? fullName)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var group = await db.Groups.Include(g => g.Students).SingleOrDefaultAsync(g => g.Id == Group);
        if (group is null)
        {
            return NotFound();
        }

        try
        {
            var name = PersonName.Parse(fullName ?? string.Empty);
            if (group.Students.Any(s => StudentNames.Same(s.Name, name)))
            {
                Error = $"{name.FullName} уже есть в группе {group.Name}.";
            }
            else
            {
                group.AddStudent(name);
                await db.SaveChangesAsync();
                Message = $"Добавлен студент {name.FullName}.";
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            Error = "Введите ФИО: фамилия, имя и (если есть) отчество.";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSetActiveAsync(int id, bool active)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var student = await db.Students.SingleOrDefaultAsync(s => s.Id == id);
        if (student is null)
        {
            return NotFound();
        }

        if (active)
        {
            student.Activate();
        }
        else
        {
            student.Deactivate();
        }

        await db.SaveChangesAsync();
        Group = student.GroupId;
        Message = active ? $"{student.Name.FullName} снова в списке активных." : $"{student.Name.FullName} отмечен(а) как отчисленный(ая). Данные и сданные работы сохраняются.";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddEmailAsync(int id, string? email)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var student = await db.Students.Include(s => s.Emails).SingleOrDefaultAsync(s => s.Id == id);
        if (student is null)
        {
            return NotFound();
        }

        try
        {
            student.AddEmail(email ?? string.Empty, EmailSource.Manual, clock.UtcNow);
            await db.SaveChangesAsync();
            Message = $"E-mail добавлен: {student.Name.WithInitials}.";
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            Error = $"Некорректный e-mail «{email}».";
        }

        Group = student.GroupId;
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveEmailAsync(int id, int emailId)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var email = await db.StudentEmails.SingleOrDefaultAsync(e => e.Id == emailId && e.StudentId == id);
        var student = await db.Students.SingleOrDefaultAsync(s => s.Id == id);
        if (email is null || student is null)
        {
            return NotFound();
        }

        db.StudentEmails.Remove(email);
        await db.SaveChangesAsync();
        Group = student.GroupId;
        Message = $"E-mail {email.Email} удалён.";
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var groups = await db.Groups.AsNoTracking()
            .Select(g => new { g.Id, g.Name, Active = g.Students.Count(s => s.IsActive) })
            .ToListAsync();
        Groups = [.. groups.OrderBy(g => g.Name, StringComparer.Ordinal).Select(g => new GroupTab(g.Id, g.Name, g.Active))];

        var id = Group ?? Groups.FirstOrDefault()?.Id;
        Current = id is null ? null : await db.Groups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id);
        if (Current is null)
        {
            Students = [];
            return;
        }

        Group = Current.Id;
        var query = db.Students.AsNoTracking().Include(s => s.Emails).Where(s => s.GroupId == Current.Id);
        if (!Inactive)
        {
            query = query.Where(s => s.IsActive);
        }

        var students = await query.ToListAsync();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = StudentNames.Normalize(Q);
            students = [.. students.Where(s => StudentNames.Normalize(s.Name.FullName).Contains(q, StringComparison.Ordinal)
                || s.Emails.Any(e => e.Email.Contains(Q.Trim(), StringComparison.OrdinalIgnoreCase)))];
        }

        Students = [.. students.OrderBy(s => StudentNames.Normalize(s.Name.FullName), StringComparer.Ordinal)];
    }
}
