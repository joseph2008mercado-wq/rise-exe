namespace Rise.Core;

// All mutations run on the UI thread. Persist each transition before announcing it to audio/UI.
public sealed class AlarmEngine(Settings settings, Action save)
{
    public Settings Settings => settings;
    public AlarmPhase Phase => settings.Active.Count == 0 ? AlarmPhase.Idle : settings.EscapeAttempts > 0 ? AlarmPhase.Embarrassment : AlarmPhase.Ringing;
    public ActiveAlarm? Current => settings.Active.FirstOrDefault();
    public event Action? Changed;

    public bool Tick(DateTimeOffset now, TimeZoneInfo zone)
    {
        bool changed = false;
        foreach (var alarm in settings.Alarms.Where(a => a.Enabled))
        {
            var due = Schedule.Due(alarm, now, zone);
            if (due != null)
            {
                if (!settings.Active.Any(a => a.Alarm.Id == alarm.Id && a.Occurrence == due.Key))
                    settings.Active.Add(new ActiveAlarm(alarm with { }, due.Key, now));
                alarm.LastOccurrence = due.Key;
                if (alarm.Days == 0) alarm.Enabled = false;
                if (alarm.Kind == EntryKind.Timer) alarm.TimerRemainingSeconds = 0;
                changed = true;
            }
            else if ((alarm.Kind == EntryKind.Timer && alarm.TimerEndsAt < now.AddHours(-24))
                || (alarm.Kind == EntryKind.Alarm && alarm.Days == 0 && Schedule.On(alarm, alarm.OneTimeDate, zone).At < now.AddHours(-24)))
            {
                alarm.Enabled = false;
                if (alarm.Kind == EntryKind.Timer) alarm.TimerRemainingSeconds = 0;
                changed = true;
            }
        }
        if (changed) Commit();
        return changed;
    }

    public void RingDemo(SoundPreset sound)
    {
        if (Phase != AlarmPhase.Idle) return;
        settings.Active.Add(new ActiveAlarm(new Alarm { Label = "Test alarm", Sound = sound }, "demo", DateTimeOffset.UtcNow));
        Commit();
    }

    public bool RequestClose()
    {
        if (Phase == AlarmPhase.Idle) return true;
        settings.EscapeAttempts = Math.Min(settings.EscapeAttempts + 1, 99);
        Commit();
        return false;
    }

    public void Dismiss(Puzzle puzzle)
    {
        if (!puzzle.Completed || Phase == AlarmPhase.Idle) return;
        // One verification acknowledges every alarm already in the current ringing session.
        settings.Active.Clear();
        settings.EscapeAttempts = 0;
        Commit();
    }

    public void Commit() { save(); Changed?.Invoke(); }
}
