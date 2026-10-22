using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rise.Core;

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string? Diagnostic { get; private set; }
    public Settings Load()
    {
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return new Settings();
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(candidate), Options) ?? throw new JsonException("Empty settings");
                if (s.Version != 1 || s.Alarms == null || s.Active == null || s.Alarms.Any(a => a.Hour is < 0 or > 23 || a.Minute is < 0 or > 59 || a.Days is < 0 or > 127 || !Enum.IsDefined(a.Sound) || !Enum.IsDefined(a.Kind)
                    || a.Kind == EntryKind.Timer && (a.TimerDurationSeconds is < 1 or > Countdown.MaximumSeconds || a.TimerRemainingSeconds is < 0 or > Countdown.MaximumSeconds || a.Enabled && a.TimerEndsAt == null))
                    || s.Active.Any(a => a.Alarm == null || !Enum.IsDefined(a.Alarm.Sound))) throw new JsonException("Invalid alarm settings");
                s.Volume = double.IsFinite(s.Volume) ? Math.Clamp(s.Volume, 0.05, 1) : .35;
                s.Intensity = double.IsFinite(s.Intensity) ? Math.Clamp(s.Intensity, 0, 1) : .65;
                s.EscapeAttempts = Math.Clamp(s.EscapeAttempts, 0, 99);
                if (candidate != path) Diagnostic = "Recovered settings from backup. Check your alarm times.";
                return s;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { Diagnostic = ex.Message; }
        }
        throw new InvalidDataException("Settings and backup could not be read. Files were preserved at " + path);
    }

    public void Save(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(stream, settings, Options); stream.Flush(true); }
        // Keep the last valid backup if Load recovered from a damaged primary file.
        if (File.Exists(path) && Diagnostic == null) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path, true);
        Diagnostic = null;
    }
}
