using System.Reflection;
using LabScaner.Core.Abstractions;
using LabScaner.Core.Directory;
using LabScaner.Core.Subjects;
using LabScaner.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Infrastructure.Persistence;

public sealed class LabScanerDbContext(DbContextOptions<LabScanerDbContext> options, ICurrentTeacher currentTeacher)
    : IdentityDbContext<AppUser, IdentityRole<int>, int>(options)
{
    private static readonly MethodInfo _applyTeacherFilter =
        typeof(LabScanerDbContext).GetMethod(nameof(ApplyTeacherFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<Student> Students => Set<Student>();

    public DbSet<StudentEmail> StudentEmails => Set<StudentEmail>();

    public DbSet<Term> Terms => Set<Term>();

    public DbSet<Subject> Subjects => Set<Subject>();

    /// <summary>Подставляется в глобальный фильтр при каждом запросе (EF параметризует члены контекста).</summary>
    private int? CurrentTeacherId => currentTeacher.TeacherId;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        IdentityTables.Rename(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(LabScanerDbContext).Assembly);

        foreach (var entity in builder.Model.GetEntityTypes().Where(e => typeof(ITeacherOwned).IsAssignableFrom(e.ClrType)).ToList())
        {
            _applyTeacherFilter.MakeGenericMethod(entity.ClrType).Invoke(this, [builder]);
        }

        SnakeCaseNaming.Apply(builder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTeacherOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTeacherOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // ADR-016: преподаватель видит только своё. Администратор тоже — общие справочники не фильтруются.
    private void ApplyTeacherFilter<T>(ModelBuilder builder)
        where T : class, ITeacherOwned =>
        builder.Entity<T>().HasQueryFilter(e => e.TeacherId == CurrentTeacherId);

    /// <summary>Новые записи получают владельца; чужие записи изменить или удалить нельзя.</summary>
    private void ApplyTeacherOwnership()
    {
        foreach (var entry in ChangeTracker.Entries<ITeacherOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity.TeacherId == 0:
                    entry.Entity.AssignTeacher(CurrentTeacherId
                        ?? throw new InvalidOperationException("Не определён преподаватель — запись не может быть сохранена."));
                    break;
                case EntityState.Added:
                case EntityState.Modified:
                case EntityState.Deleted:
                    if (entry.Entity.TeacherId != CurrentTeacherId)
                    {
                        throw new InvalidOperationException("Запись принадлежит другому преподавателю.");
                    }

                    break;
                case EntityState.Detached:
                case EntityState.Unchanged:
                default:
                    break;
            }
        }
    }
}
