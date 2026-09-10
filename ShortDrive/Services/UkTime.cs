namespace ShortDrive.Services;

/// <summary>
/// Centralised UK (Europe/London) time handling so cover start times are interpreted and
/// displayed in UK local time regardless of the server's timezone. Cover times are entered
/// and shown in UK time; they are persisted as UTC.
/// </summary>
public static class UkTime
{
    private static readonly TimeZoneInfo Tz = Resolve();

    private static TimeZoneInfo Resolve()
    {
        // "Europe/London" (IANA, Linux/macOS) and "GMT Standard Time" (Windows) both map to UK time.
        foreach (var id in new[] { "Europe/London", "GMT Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>Current wall-clock time in the UK.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);

    /// <summary>Convert a UK wall-clock time to UTC for storage.</summary>
    public static DateTime ToUtc(DateTime ukLocal)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(ukLocal, DateTimeKind.Unspecified), Tz);

    /// <summary>Convert a stored UTC time back to UK wall-clock time for display.</summary>
    public static DateTime FromUtc(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz);
}
