namespace Pos.Infrastructure;

/// <summary>Convierte fechas operativas usando la zona global configurada para la tienda.</summary>
public static class StoreTimeZone
{
    public const string DefaultId = "America/Mexico_City";

    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        var requestedId = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultId : timeZoneId.Trim();
        if (TryResolve(requestedId, out var timeZone)) return timeZone;

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(requestedId, out var windowsId) && TryResolve(windowsId, out timeZone))
            return timeZone;

        if (TryResolve(DefaultId, out timeZone)) return timeZone;
        return TimeZoneInfo.Local;
    }

    public static DateTimeOffset ToStoreTime(DateTimeOffset value, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(value, timeZone);

    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) DayRangeUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var end = start.AddDays(1);
        return (
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, timeZone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(end, timeZone)));
    }

    private static bool TryResolve(string id, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        timeZone = null!;
        return false;
    }
}
