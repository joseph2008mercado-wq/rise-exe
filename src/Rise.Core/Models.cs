namespace Rise.Core;

public enum SoundPreset { Industrial, DigitalHell, CursedMidi }
public enum AlarmPhase { Idle, Ringing, Embarrassment }

public sealed record Alarm
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Label { get; set; } = "Alarm";
    public int Hour { get; set; } = 7;
    public int Minute { get; set; }
    // Sunday = bit 0; zero means a one-time alarm on OneTimeDate.
    public int Days { get; set; }
    public DateOnly OneTimeDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
    public bool Enabled { get; set; } = true;
    public SoundPreset Sound { get; set; }
    public DateTimeOffset ArmedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastOccurrence { get; set; }
    public string DisplayTime => $"{Hour:00}:{Minute:00}";
    public string DisplayDays => Days == 0 ? OneTimeDate.ToString("MMM dd, yyyy") :
        string.Join(" ", Enumerable.Range(0, 7).Where(d => (Days & (1 << d)) != 0).Select(d => new[] { "SU", "MO", "TU", "WE", "TH", "FR", "SA" }[d]));
    public string DisplaySound => SoundNames.For(Sound);
}

public static class SoundNames
{
    public static string For(SoundPreset preset) => preset switch
    {
        SoundPreset.Industrial => "INDUSTRIAL.EXE",
        SoundPreset.DigitalHell => "DIGITALHELL.WAV",
        _ => "CURSEDMIDI.MID"
    };
}

public sealed record ActiveAlarm(Alarm Alarm, string Occurrence, DateTimeOffset StartedAt);
public sealed class Settings
{
    public int Version { get; set; } = 1;
    public List<Alarm> Alarms { get; set; } = [];
    public List<ActiveAlarm> Active { get; set; } = [];
    public int EscapeAttempts { get; set; }
    public double Volume { get; set; } = 0.65;
    public double Intensity { get; set; } = 0.65;
    public bool WakeEnabled { get; set; } = true;
}
