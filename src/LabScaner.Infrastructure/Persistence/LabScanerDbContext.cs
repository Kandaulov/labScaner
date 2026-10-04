using LabScaner.Core.Directory;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Infrastructure.Persistence;

public sealed class LabScanerDbContext(DbContextOptions<LabScanerDbContext> options) : DbContext(options)
{
    public DbSet<Group> Groups => Set<Group>();

    public DbSet<Student> Students => Set<Student>();

    public DbSet<StudentEmail> StudentEmails => Set<StudentEmail>();

    public DbSet<Term> Terms => Set<Term>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LabScanerDbContext).Assembly);
        SnakeCaseNaming.Apply(modelBuilder);
    }
}
