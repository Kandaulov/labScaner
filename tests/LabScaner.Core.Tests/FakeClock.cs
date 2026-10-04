using LabScaner.Core.Abstractions;

namespace LabScaner.Core.Tests;

/// <summary>Часы с ручным управлением для тестов правил срока.</summary>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = now;

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
