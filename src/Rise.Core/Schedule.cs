namespace Rise.Core;

public sealed record Occurrence(DateTimeOffset At, string Key);

public static class Schedule
{
    public static DateTimeOffset Resolve(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // Spring-forward: ring at the first valid minute. Fall-back: first occurrence only.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    public static Occurrence On(Alarm alarm, DateOnly date, TimeZoneInfo zone) =>
        new(Resolve(date.ToDateTime(new TimeOnly(alarm.Hour, alarm.Minute)), zone), $"{date:yyyy-MM-dd}/{alarm.Hour:00}:{alarm.Minute:00}");

    public static Occurrence? Next(Alarm alarm, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!alarm.Enabled) return null;
        if (alarm.Kind == EntryKind.Timer)
            return alarm.TimerEndsAt is { } end && end > now && alarm.LastOccurrence != Countdown.Key(end) ? new Occurrence(end, Countdown.Key(end)) : null;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        for (int i = 0; i < 9; i++)
        {
            var date = alarm.Days == 0 ? alarm.OneTimeDate : today.AddDays(i);
            if (alarm.Days != 0 && (alarm.Days & (1 << (int)date.DayOfWeek)) == 0) continue;
            var occurrence = On(alarm, date, zone);
            if (occurrence.At > now && occurrence.Key != alarm.LastOccurrence) return occurrence;
            if (alarm.Days == 0) break;
        }
        return null;
    }

    public static Occurrence? Due(Alarm alarm, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!alarm.Enabled) return null;
        if (alarm.Kind == EntryKind.Timer)
            return alarm.TimerEndsAt is { } end && end <= now && end >= alarm.ArmedAt && now - end <= TimeSpan.FromHours(24)
                && alarm.LastOccurrence != Countdown.Key(end) ? new Occurrence(end, Countdown.Key(end)) : null;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        for (int i = 0; i < 2; i++)
        {
            var date = alarm.Days == 0 ? alarm.OneTimeDate : today.AddDays(-i);
            if (alarm.Days != 0 && (alarm.Days & (1 << (int)date.DayOfWeek)) == 0) continue;
            var occurrence = On(alarm, date, zone);
            if (occurrence.At <= now)
                return occurrence.At >= alarm.ArmedAt && now - occurrence.At <= TimeSpan.FromHours(24)
                    && occurrence.Key != alarm.LastOccurrence ? occurrence : null;
            if (alarm.Days == 0) break;
        }
        return null;
    }
}
