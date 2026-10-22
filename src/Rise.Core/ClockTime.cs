namespace Rise.Core;

public static class ClockTime
{
    public static TimeOnly From12Hour(int hour, int minute, bool pm)
    {
        if (hour is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(hour));
        return new TimeOnly(hour % 12 + (pm ? 12 : 0), minute);
    }
    public static DateOnly NextDate(TimeOnly time, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return Schedule.Resolve(today.ToDateTime(time), zone) > now ? today : today.AddDays(1);
    }
}
