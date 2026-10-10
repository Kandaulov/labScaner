namespace LabScaner.Web.Navigation;

/// <summary>Часовой пояс интерфейса. Вуз в Ульяновске — UTC+4 (Europe/Ulyanovsk).</summary>
public static class AppTime
{
    public static TimeZoneInfo Zone { get; } = Resolve();

    /// <summary>Момент времени в часовом поясе интерфейса.</summary>
    public static DateTimeOffset Local(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, Zone);

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Europe/Ulyanovsk", "Ulyanovsk Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("UTC+4", TimeSpan.FromHours(4), "UTC+4", "UTC+4");
    }
}
