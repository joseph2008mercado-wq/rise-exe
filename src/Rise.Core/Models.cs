using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Rise.Core;

public enum SoundPreset { Industrial, DigitalHell, CursedMidi }
public enum AlarmPhase { Idle, Ringing, Embarrassment }
public enum EntryKind { Alarm, Timer }

public sealed record Alarm : INotifyPropertyChanged
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
    public EntryKind Kind { get; set; }
    public int TimerDurationSeconds { get; set; } = 300;
    public int TimerRemainingSeconds { get; set; } = 300;
    public DateTimeOffset? TimerEndsAt { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    [JsonIgnore] public string DisplayTime => Kind == EntryKind.Timer ? Countdown.Format(Countdown.Remaining(this, DateTimeOffset.UtcNow)) : new TimeOnly(Hour, Minute).ToString("h:mm tt", CultureInfo.InvariantCulture);
    [JsonIgnore] public string DisplayDays => Kind == EntryKind.Timer ? Enabled ? "Timer / running" : LastOccurrence != null ? "Timer / finished" : "Timer / paused" : Days switch
    {
        0 => "Once / " + OneTimeDate.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture),
        127 => "Every day", 62 => "Weekdays", 65 => "Weekends",
        _ => string.Join(" ", Enumerable.Range(0, 7).Where(d => (Days & (1 << d)) != 0).Select(d => new[] { "SU", "MO", "TU", "WE", "TH", "FR", "SA" }[d]))
    };
    [JsonIgnore] public string ToggleText => Kind == EntryKind.Timer ? Enabled ? "Pause" : "Start" : Enabled ? "On" : "Off";
    [JsonIgnore] public string DisplaySound => SoundNames.For(Sound);
    public void RefreshCountdown() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTime)));
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
    public bool HeadphoneKeepAlive { get; set; } = true;
    public int KeepAliveMinutes { get; set; } = 5;
}
