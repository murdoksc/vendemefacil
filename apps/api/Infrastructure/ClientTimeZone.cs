namespace VendemeFacil.Api.Infrastructure;

public static class ClientTimeZone
{
    public const string HeaderName = "X-Time-Zone";

    public static TimeZoneInfo From(HttpContext context)
    {
        var requested = context.Request.Headers[HeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(requested))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(requested); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    public static DateOnly Today(TimeZoneInfo zone, DateTimeOffset? utcNow = null) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow ?? DateTimeOffset.UtcNow, zone).DateTime);

    public static DateTimeOffset StartOfDayUtc(DateOnly localDate, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public static DateOnly LocalDate(DateTimeOffset utcValue, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcValue, zone).DateTime);
}
