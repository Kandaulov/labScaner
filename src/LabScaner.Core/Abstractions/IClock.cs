namespace LabScaner.Core.Abstractions;

/// <summary>
/// Источник текущего времени. Домен не обращается к <see cref="DateTimeOffset.UtcNow"/> напрямую:
/// правила «в срок» и напоминания о дедлайнах тестируются с подменённым временем.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
