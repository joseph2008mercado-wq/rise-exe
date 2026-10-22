using Rise.Core;

int passed = 0, failed = 0;
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
var utc = TimeZoneInfo.Utc;
var now = new DateTimeOffset(2026, 10, 20, 7, 0, 0, TimeSpan.Zero);
Alarm NewAlarm(int days = 0) => new() { Hour = 7, Minute = 0, OneTimeDate = new(2026, 10, 20), Days = days, ArmedAt = now.AddHours(-8) };

Check("one-time exact trigger", () => Assert(Schedule.Due(NewAlarm(), now, utc)?.At == now));
Check("no early trigger", () => Assert(Schedule.Due(NewAlarm(), now.AddSeconds(-1), utc) == null));
Check("missed alarm within 24 hours", () => Assert(Schedule.Due(NewAlarm(), now.AddHours(23), utc) != null));
Check("stale missed alarm skipped", () => Assert(Schedule.Due(NewAlarm(), now.AddHours(25), utc) == null));
Check("new alarm does not retroactively trigger", () => { var a = NewAlarm(); a.ArmedAt = now.AddMinutes(1); Assert(Schedule.Due(a, now.AddMinutes(2), utc) == null); });
Check("weekday schedule", () => { var a = NewAlarm(1 << (int)DayOfWeek.Monday); Assert(Schedule.Next(a, now, utc)!.At == now.AddDays(6)); });
Check("disabled alarm ignored", () => { var a = NewAlarm(); a.Enabled = false; Assert(Schedule.Due(a, now, utc) == null && Schedule.Next(a, now.AddHours(-1), utc) == null); });
Check("dedup by local occurrence", () => { var a = NewAlarm(127); a.LastOccurrence = Schedule.On(a, a.OneTimeDate, utc).Key; Assert(Schedule.Due(a, now.AddHours(1), utc) == null); });
Check("daily next day", () => Assert(Schedule.Next(NewAlarm(127), now, utc)!.At == now.AddDays(1)));
Check("handled daily occurrence never falls back to yesterday", () =>
{
    var a = NewAlarm(127); a.ArmedAt = now.AddDays(-3);
    var s = new Settings { Alarms = [a] }; var e = new AlarmEngine(s, () => { });
    e.Tick(now, utc); e.Tick(now, utc); Assert(s.Active.Count == 1);
});
Check("recurring missed previous day", () => Assert(Schedule.Due(NewAlarm(127), now.AddHours(23), utc)?.At == now));
var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
Check("spring gap advances to first valid minute", () =>
{
    var at = Schedule.Resolve(new DateTime(2026, 3, 8, 2, 30, 0), eastern);
    Assert(at == new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero));
});
Check("fall repeated hour uses first occurrence", () =>
{
    var at = Schedule.Resolve(new DateTime(2026, 11, 1, 1, 30, 0), eastern);
    Assert(at == new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero));
});
Check("fall-back does not ring twice", () =>
{
    var a = NewAlarm(127); a.Hour = 1; a.Minute = 30; a.ArmedAt = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
    var s = new Settings { Alarms = [a] }; var e = new AlarmEngine(s, () => { });
    e.Tick(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), eastern);
    e.Tick(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), eastern);
    Assert(s.Active.Count == 1);
});
Check("simultaneous alarms merge without duplicate occurrences", () =>
{
    var s = new Settings { Alarms = [NewAlarm(), NewAlarm()] }; int saves = 0;
    var e = new AlarmEngine(s, () => saves++); e.Tick(now, utc); e.Tick(now, utc);
    Assert(s.Active.Count == 2 && saves == 1 && s.Alarms.All(a => !a.Enabled));
});
Check("escape bounded and normal close restored", () =>
{
    var s = new Settings(); var e = new AlarmEngine(s, () => { }); Assert(e.RequestClose()); e.RingDemo(SoundPreset.Industrial);
    for (int i = 0; i < 200; i++) Assert(!e.RequestClose());
    Assert(s.EscapeAttempts == 99 && e.Phase == AlarmPhase.Embarrassment);
    var p = new Puzzle(() => 0); while (!p.Completed) { p.BeginInput(); foreach (int tile in p.Sequence.ToArray()) p.Press(tile); }
    e.Dismiss(p); Assert(e.RequestClose() && e.Phase == AlarmPhase.Idle && s.EscapeAttempts == 0);
});
Check("unfinished puzzle cannot dismiss", () => { var e = new AlarmEngine(new Settings(), () => { }); e.RingDemo(SoundPreset.Industrial); e.Dismiss(new Puzzle()); Assert(e.Phase == AlarmPhase.Ringing); });
Check("puzzle ignores input during playback", () => { var p = new Puzzle(); Assert(p.Press(p.Sequence[0]) == PuzzleResult.Ignored && p.Position == 0); });
Check("puzzle wrong answer resets current round", () => { var p = new Puzzle(() => 0); p.BeginInput(); p.Press(0); Assert(p.Press(1) == PuzzleResult.Retry && p.Position == 0 && p.Round == 1 && !p.Accepting); });
Check("puzzle exactly three manageable rounds", () =>
{
    var p = new Puzzle(() => 2); int inputs = 0;
    for (int r = 1; r <= 3; r++) { Assert(p.Sequence.Length == r + 3); p.BeginInput(); foreach (int tile in p.Sequence.ToArray()) { p.Press(tile); inputs++; } }
    Assert(p.Completed && inputs == 15);
});
Check("escape reset discards progress", () => { var p = new Puzzle(() => 1); p.BeginInput(); foreach (int t in p.Sequence.ToArray()) p.Press(t); p.Reset(); Assert(p.Round == 1 && p.Position == 0 && !p.Completed && !p.Accepting); });
Check("random puzzle produces valid varied sequences", () => { var sequences = Enumerable.Range(0, 50).Select(_ => new Puzzle().Sequence).ToList(); Assert(sequences.All(a => a.All(t => t is >= 0 and <= 3))); Assert(sequences.Select(a => string.Join(',', a)).Distinct().Count() > 10); });
string dir = Path.Combine(Path.GetTempPath(), "rise-core-tests-" + Guid.NewGuid());
Directory.CreateDirectory(dir);
try
{
    Check("create/edit/delete and settings roundtrip", () =>
    {
        string path = Path.Combine(dir, "roundtrip.json"); var store = new SettingsStore(path); var s = new Settings();
        s.Alarms.Add(NewAlarm(62)); s.Alarms[0].Label = "UP & <AWAY>"; s.Volume = .6; s.Intensity = .9; store.Save(s);
        var loaded = store.Load(); Assert(loaded.Alarms[0].Label == "UP & <AWAY>" && loaded.Alarms[0].Days == 62 && loaded.Volume == .6);
        loaded.Alarms.Clear(); store.Save(loaded); Assert(store.Load().Alarms.Count == 0);
    });
    Check("active occurrence and embarrassment survive restart", () =>
    {
        var store = new SettingsStore(Path.Combine(dir, "active.json")); var s = new Settings { Alarms = [NewAlarm()] };
        var e = new AlarmEngine(s, () => store.Save(s)); e.Tick(now, utc); e.RequestClose();
        var restored = store.Load(); var resumed = new AlarmEngine(restored, () => { });
        Assert(resumed.Phase == AlarmPhase.Embarrassment && restored.Active.Count == 1 && restored.EscapeAttempts == 1);
        resumed.Tick(now.AddMinutes(1), utc); Assert(restored.Active.Count == 1);
    });
    Check("damaged primary recovers backup", () =>
    {
        string path = Path.Combine(dir, "recovery.json"); var store = new SettingsStore(path); var s = new Settings { Volume = .4 }; store.Save(s); s.Volume = .8; store.Save(s);
        File.WriteAllText(path, "{damaged"); var recovered = store.Load(); Assert(recovered.Volume == .4 && store.Diagnostic != null);
        store.Save(recovered); File.WriteAllText(path, "{again"); Assert(store.Load().Volume == .4);
    });
    Check("unreadable settings fail without overwriting", () => { string path = Path.Combine(dir, "bad.json"); File.WriteAllText(path, "bad"); bool threw = false; try { new SettingsStore(path).Load(); } catch (InvalidDataException) { threw = true; } Assert(threw && File.ReadAllText(path) == "bad"); });
    Check("out of range settings rejected", () => { string path = Path.Combine(dir, "invalid.json"); File.WriteAllText(path, "{\"Alarms\":[{\"Hour\":99}]}"); bool threw = false; try { new SettingsStore(path).Load(); } catch (InvalidDataException) { threw = true; } Assert(threw); });
}
finally { Directory.Delete(dir, true); }
foreach (SoundPreset preset in Enum.GetValues<SoundPreset>())
{
    Check(preset + " composition is non-silent and peak-limited", () =>
    {
        double sum = 0, peak = 0; int count = (int)(Synthesizer.Duration(preset) * Synthesizer.SampleRate);
        for (int i = 0; i < count; i += 3)
        {
            float sample = Synthesizer.Sample(preset, i, 1, 99);
            Assert(float.IsFinite(sample)); sum += sample * sample; peak = Math.Max(peak, Math.Abs(sample));
        }
        Assert(peak <= .940001 && Math.Sqrt(sum / (count / 3)) > .07);
    });
    Check(preset + " loop boundary has no abrupt jump", () =>
    {
        long wrap = (long)(Synthesizer.Duration(preset) * Synthesizer.SampleRate);
        Assert(Math.Abs(Synthesizer.Sample(preset, wrap - 1, 1) - Synthesizer.Sample(preset, wrap + 1, 1)) < .02);
    });
}
Check("three presets have different audio fingerprints", () =>
{
    var signatures = Enum.GetValues<SoundPreset>().Select(p => Enumerable.Range(1, 1000).Select(i => Synthesizer.Sample(p, i * 43, .65)).ToArray()).ToArray();
    for (int i = 0; i < 3; i++) for (int j = i + 1; j < 3; j++) Assert(signatures[i].Zip(signatures[j]).Average(x => Math.Abs(x.First - x.Second)) > .1);
});
Check("embarrassment changes arrangement without unbounded gain", () =>
{
    double difference = Enumerable.Range(1, 1000).Average(i => Math.Abs(Synthesizer.Sample(SoundPreset.Industrial, i * 43, 1, 0) - Synthesizer.Sample(SoundPreset.Industrial, i * 43, 1, 1)));
    Assert(difference > .05);
});
Check("industrial is densely saturated at maximum intensity", () =>
{
    double rms = Math.Sqrt(Enumerable.Range(1, 44100).Average(i => Math.Pow(Synthesizer.Sample(SoundPreset.Industrial, i, 1), 2)));
    Assert(rms > .55, "Industrial RMS too low: " + rms);
});
foreach (SoundPreset preset in Enum.GetValues<SoundPreset>())
{
    Check(preset + " has dedicated measurable sub-bass", () =>
    {
        double frequency = preset == SoundPreset.Industrial ? 48 : preset == SoundPreset.DigitalHell ? 55 : 440 * Math.Pow(2, (36 - 69) / 12.0);
        double re = 0, im = 0; int count = Synthesizer.SampleRate * 2;
        for (int i = 0; i < count; i++)
        {
            double sample = Synthesizer.Sample(preset, i, 1);
            double phase = 2 * Math.PI * frequency * i / Synthesizer.SampleRate;
            re += sample * Math.Cos(phase); im += sample * Math.Sin(phase);
        }
        double amplitude = 2 * Math.Sqrt(re * re + im * im) / count;
        Assert(amplitude > .45, "Sub-bass amplitude too low: " + amplitude);
        Console.WriteLine($"     {frequency:0.0} Hz fundamental amplitude: {amplitude:0.000}");
    });
}
Console.WriteLine($"\n{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
