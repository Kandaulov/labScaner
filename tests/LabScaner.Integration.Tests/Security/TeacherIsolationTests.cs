using LabScaner.Core.Abstractions;
using LabScaner.Core.Subjects;
using LabScaner.Infrastructure.Identity;
using LabScaner.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Integration.Tests.Security;

/// <summary>ADR-016: преподаватель видит и меняет только свои данные.</summary>
[Collection(PostgresTests.Name)]
public sealed class TeacherIsolationTests(PostgresFixture postgres)
{
    private sealed class Teacher(int? id) : ICurrentTeacher
    {
        public int? TeacherId { get; } = id;
    }

    private async Task<int> CreateUserAsync()
    {
        await using var db = postgres.CreateDbContext();
        var name = $"iso{Guid.NewGuid():N}"[..20];
        var user = new AppUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), DisplayName = name };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Teacher_SeesOnlyOwnSubjects()
    {
        var a = await CreateUserAsync();
        var b = await CreateUserAsync();
        await using (var db = postgres.CreateDbContext(new Teacher(a)))
        {
            db.Subjects.Add(new Subject("Корпоративные информационные системы", "КорпИС"));
            await db.SaveChangesAsync();
        }

        await using (var db = postgres.CreateDbContext(new Teacher(b)))
        {
            db.Subjects.Add(new Subject("Операционные системы", "ОС"));
            await db.SaveChangesAsync();
        }

        await using (var db = postgres.CreateDbContext(new Teacher(a)))
        {
            var codes = await db.Subjects.Select(s => s.Code).ToListAsync();
            Assert.Equal(["КорпИС"], codes);
        }

        await using (var db = postgres.CreateDbContext(new Teacher(b)))
        {
            var subject = Assert.Single(await db.Subjects.ToListAsync());
            Assert.Equal("ОС", subject.Code);
            Assert.Equal(b, subject.TeacherId);
        }
    }

    [Fact]
    public async Task NoTeacher_SeesNothing_AndCannotSave()
    {
        var a = await CreateUserAsync();
        await using (var db = postgres.CreateDbContext(new Teacher(a)))
        {
            db.Subjects.Add(new Subject("Системное программирование", "СПП"));
            await db.SaveChangesAsync();
        }

        await using var anonymous = postgres.CreateDbContext();
        Assert.False(await anonymous.Subjects.AnyAsync(s => s.TeacherId == a));

        anonymous.Subjects.Add(new Subject("Без владельца", "X"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => anonymous.SaveChangesAsync());
    }

    [Fact]
    public async Task Teacher_CannotModifyForeignSubject()
    {
        var a = await CreateUserAsync();
        var b = await CreateUserAsync();
        await using (var db = postgres.CreateDbContext(new Teacher(a)))
        {
            db.Subjects.Add(new Subject("Корпоративные информационные системы", "КорпИС"));
            await db.SaveChangesAsync();
        }

        await using var intruder = postgres.CreateDbContext(new Teacher(b));
        var foreign = await intruder.Subjects.IgnoreQueryFilters().SingleAsync(s => s.TeacherId == a);
        intruder.Subjects.Remove(foreign);

        await Assert.ThrowsAsync<InvalidOperationException>(() => intruder.SaveChangesAsync());
    }
}
