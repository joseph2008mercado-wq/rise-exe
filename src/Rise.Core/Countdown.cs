namespace Rise.Core;

public static class Countdown
{
    public const int MaximumSeconds = 7 * 24 * 60 * 60;
    public static void Start(Alarm timer, int seconds, DateTimeOffset now)
    {
        if (seconds is < 1 or > MaximumSeconds) throw new ArgumentOutOfRangeException(nameof(seconds));
        timer.Kind = EntryKind.Timer; timer.Days = 0; timer.Enabled = true;
        timer.TimerDurationSeconds = seconds; timer.TimerRemainingSeconds = seconds;
        timer.TimerEndsAt = now.AddSeconds(seconds); timer.ArmedAt = now; timer.LastOccurrence = null;
    }
    public static int Remaining(Alarm timer, DateTimeOffset now) => timer.Enabled && timer.TimerEndsAt is { } end
        ? (int)Math.Clamp(Math.Ceiling((end - now).TotalSeconds), 0, MaximumSeconds) : timer.TimerRemainingSeconds;
    public static void Pause(Alarm timer, DateTimeOffset now)
    {
        timer.TimerRemainingSeconds = Remaining(timer, now); timer.Enabled = false; timer.TimerEndsAt = null;
    }
    public static void Resume(Alarm timer, DateTimeOffset now)
    {
        int remaining = timer.TimerRemainingSeconds > 0 ? timer.TimerRemainingSeconds : timer.TimerDurationSeconds;
        timer.TimerEndsAt = now.AddSeconds(remaining); timer.ArmedAt = now;
        timer.Enabled = true; timer.LastOccurrence = null;
    }
    public static string Format(int seconds) => seconds >= 3600 ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
    public static string Key(DateTimeOffset at) => "timer/" + at.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
