using LabScaner.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LabScaner.Integration.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_ApplyToEmptyDatabase()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var db = postgres.CreateDbContext(connectionString);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var tables = await ReadTablesAsync(connectionString);
        Assert.Superset(new HashSet<string> { "groups", "students", "student_emails", "terms" }, tables);
    }

    [Fact]
    public async Task Model_HasNoChangesWithoutMigration()
    {
        await using var db = postgres.CreateDbContext();

        // Падает, если модель изменили, а миграцию не создали.
        Assert.False(db.Database.HasPendingModelChanges(), "Модель изменена без миграции: выполните dotnet ef migrations add.");
    }

    private static async Task<HashSet<string>> ReadTablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new HashSet<string>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
