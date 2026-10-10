using LabScaner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Navigation;

/// <summary>Состояние почтового ящика преподавателя для боковой панели и сводки.</summary>
public sealed record MailStatus(bool Configured, bool? Ok, string? Address)
{
    public static MailStatus None { get; } = new(false, null, null);

    public bool Failed => Configured && Ok == false;

    public string Badge => Failed ? "ошибка" : "выкл.";

    public string Text => !Configured
        ? "Почта не подключена"
        : Failed ? $"Ящик {Address} не отвечает" : $"Ящик {Address} подключён · приём писем — этап 4";

    public string LinkText => !Configured ? "настроить" : Failed ? "проверить" : "настройки";
}

/// <summary>Один запрос на страницу: строка подключения текущего преподавателя (глобальный фильтр по teacher_id).</summary>
public sealed class MailStatusQuery(LabScanerDbContext db)
{
    private MailStatus? _cached;

    public async Task<MailStatus> GetAsync()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var row = await db.MailConnections.AsNoTracking()
            .Select(c => new { c.Address, c.LastCheckOk })
            .FirstOrDefaultAsync();
        _cached = row is null ? MailStatus.None : new MailStatus(true, row.LastCheckOk, row.Address);
        return _cached;
    }
}
