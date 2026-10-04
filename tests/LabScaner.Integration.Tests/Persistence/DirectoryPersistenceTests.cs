using LabScaner.Core.Directory;
using LabScaner.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Integration.Tests.Persistence;

[Collection(PostgresTests.Name)]
public sealed class DirectoryPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

    private static int UniqueNumber(int min, int range) => min + (int)((uint)Guid.NewGuid().GetHashCode() % (uint)range);

    private static string UniqueGroupName() => $"ТСТ-{UniqueNumber(10_000, 90_000)}";

    [Fact]
    public async Task Group_WithStudentsAndEmails_RoundTrips()
    {
        var groupName = UniqueGroupName();
        await using (var db = postgres.CreateDbContext())
        {
            var group = new Group(groupName, admissionYear: 2023);
            var ivanov = group.AddStudent(PersonName.Parse("ИВАНОВ ИВАН ИВАНОВИЧ"));
            ivanov.AddEmail("Ivanov@Mail.ru", EmailSource.Auto, _now);
            group.AddStudent(PersonName.Parse("Петрова Анна"));
            db.Groups.Add(group);
            await db.SaveChangesAsync();
        }

        await using (var db = postgres.CreateDbContext())
        {
            var loaded = await db.Groups
                .Include(g => g.Students).ThenInclude(s => s.Emails)
                .SingleAsync(g => g.NameKey == GroupName.Key(groupName));

            Assert.Equal("ТСТ", loaded.Direction);
            Assert.Equal(2023, loaded.AdmissionYear);
            Assert.Equal(2, loaded.Students.Count);

            var ivanov = loaded.Students.Single(s => s.LastName == "Иванов");
            Assert.Equal("Иванов И.И.", ivanov.Name.WithInitials);
            var email = Assert.Single(ivanov.Emails);
            Assert.Equal("ivanov@mail.ru", email.Email);
            Assert.Equal(EmailSource.Auto, email.Source);
            Assert.Equal(_now, email.CreatedAt);

            Assert.Null(loaded.Students.Single(s => s.LastName == "Петрова").MiddleName);
        }
    }

    [Fact]
    public async Task Group_DuplicateNameIgnoringCase_Rejected()
    {
        var groupName = UniqueGroupName();
        await using var db = postgres.CreateDbContext();
        db.Groups.Add(new Group(groupName));
        await db.SaveChangesAsync();

        db.Groups.Add(new Group(groupName.ToLowerInvariant().Replace("-", "", StringComparison.Ordinal)));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Term_RoundTripsCalendar()
    {
        var year = UniqueNumber(2030, 70);
        await using (var db = postgres.CreateDbContext())
        {
            db.Terms.Add(new Term(year, TermSeason.Spring, new DateOnly(year + 1, 6, 1), new DateOnly(year + 1, 6, 15)));
            await db.SaveChangesAsync();
        }

        await using (var db = postgres.CreateDbContext())
        {
            var term = await db.Terms.SingleAsync(t => t.AcademicYear == $"{year}-{year + 1}" && t.Season == TermSeason.Spring);
            Assert.Equal(new DateOnly(year + 1, 6, 1), term.CreditWeekStart);
            Assert.Equal(new DateOnly(year + 1, 6, 15), term.SessionStart);
        }
    }
}
