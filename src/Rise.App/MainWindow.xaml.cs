using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Rise.Core;

namespace Rise.App;

public partial class MainWindow : Window
{
    public AlarmEngine Engine { get; }
    public Puzzle Verification { get; } = new();
    internal AudioEngine Audio { get; } = new();
    private readonly Settings settings;
    private readonly SettingsStore store;
    private readonly bool isolated;
    private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly WakeScheduler scheduler = new();
    private readonly CheckBox[] dayBoxes = new CheckBox[7];
    private readonly Button[] tileButtons = new Button[4];
    private readonly Color[] colors = [Color.FromRgb(132, 255, 97), Color.FromRgb(88, 213, 255), Color.FromRgb(245, 109, 191), Color.FromRgb(186, 142, 255)];
    private CancellationTokenSource? sequenceCancel;
    private Guid? editing;
    private EntryKind editorKind;
    private bool ready, ringing, sessionEnding, preview, editorLoading;
    private int escapes;
    private long lastSecond = -1;
    private DateTimeOffset previewUntil, retryAudio;
    private string zoneId = TimeZoneInfo.Local.Id;
    internal bool SuppressHardwareAudio { get; set; }
    internal int CloseRejections { get; private set; }
    public MainWindow(Settings settings, SettingsStore store, bool isolated)
    {
        this.settings = settings; this.store = store; this.isolated = isolated;
        InitializeComponent();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ChromeMessages);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height * .94);
        Engine = new AlarmEngine(settings, SaveSettings);
        Engine.Changed += AlarmStateChanged;
        string[] names = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];
        for (int i = 0; i < 7; i++)
        {
            dayBoxes[i] = new CheckBox { Content = names[i], FontSize = 10 };
            dayBoxes[i].Checked += EditorChanged; dayBoxes[i].Unchecked += EditorChanged;
            DayChoices.Children.Add(dayBoxes[i]);
        }
        for (int i = 0; i < 4; i++)
        {
            int tile = i;
            var button = new Button { Content = $"0{i + 1}", FontSize = 26, Margin = new Thickness(3), Foreground = new SolidColorBrush(colors[i]), BorderBrush = new SolidColorBrush(colors[i]), Tag = i };
            button.Click += (_, _) => PressTile(tile);
            System.Windows.Automation.AutomationProperties.SetName(button, $"Tile {i + 1}");
            tileButtons[i] = button; Tiles.Children.Add(button);
        }
        var soundNames = Enum.GetValues<SoundPreset>().Select(SoundNames.For).ToArray();
        PreviewSound.ItemsSource = soundNames; PreviewSound.SelectedIndex = 0;
        EditorSound.ItemsSource = soundNames; EditorSound.SelectedIndex = 0;
        HourInput.ItemsSource = Enumerable.Range(1, 12).ToArray();
        MinuteInput.ItemsSource = Enumerable.Range(0, 60).Select(m => m.ToString("00", CultureInfo.InvariantCulture)).ToArray();
        PeriodInput.ItemsSource = new[] { "AM", "PM" };
        RepeatInput.ItemsSource = new[] { "Once", "Every day", "Weekdays", "Weekends", "Custom days" };
        OnceModeInput.ItemsSource = new[] { "Next occurrence (today or tomorrow)", "Choose a date" };
        VolumeSlider.Value = settings.Volume; IntensitySlider.Value = settings.Intensity;
        WakeToggle.IsChecked = settings.WakeEnabled;
        OnceDate.SelectedDate = DateTime.Today.AddDays(1);
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveSettings(); };
        clockTimer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { ready = true; VolumeText.Text = $"{settings.Volume * 100:0}%"; RefreshAlarms(); SyncTasks(); AlarmStateChanged(); Tick(); clockTimer.Start(); };
        Closed += (_, _) =>
        {
            clockTimer.Stop(); saveTimer.Stop(); sequenceCancel?.Cancel(); Audio.Dispose();
            WakeScheduler.KeepAwake(false); SystemEvents.PowerModeChanged -= PowerChanged;
            Application.Current.SessionEnding -= SessionEnding;
        };
        SystemEvents.PowerModeChanged += PowerChanged;
        Application.Current.SessionEnding += SessionEnding;
    }

    private IntPtr ChromeMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Custom chrome owns the title bar. Route native system-close commands through
        // WPF Close() so Alt+F4/system-menu/taskbar paths share Window.Closing exactly.
        if (message == 0x0112 && (wParam.ToInt64() & 0xFFF0) == 0xF060)
        {
            handled = true;
            Dispatcher.BeginInvoke(Close);
        }
        return IntPtr.Zero;
    }

    private void SessionEnding(object sender, SessionEndingCancelEventArgs e) { sessionEnding = true; SaveSettings(); e.Cancel = false; }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(Wake); }
    private void SaveSettings()
    {
        try { store.Save(settings); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { DiagnosticText.Text = "SAVE FAILED // " + ex.Message + " Changes may not survive restart."; }
    }
    private void Tick()
    {
        var now = DateTimeOffset.Now;
        Clock.Time = now.ToString("hh:mm:ss", CultureInfo.InvariantCulture); Clock.InvalidateVisual();
        AmPmText.Text = now.ToString("tt", CultureInfo.InvariantCulture);
        Visualizer.Bands = Audio.Bands; Visualizer.InvalidateVisual();
        if (now.ToUnixTimeSeconds() == lastSecond) return;
        lastSecond = now.ToUnixTimeSeconds();
        DateText.Text = now.ToString("ddd / dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        bool changed = Engine.Tick(now, TimeZoneInfo.Local);
        if (changed) { RefreshAlarms(); SyncTasks(); }
        foreach (var timer in settings.Alarms.Where(a => a.Kind == EntryKind.Timer && a.Enabled)) timer.RefreshCountdown();
        if (preview && now >= previewUntil) { preview = false; Audio.Stop(); PreviewButton.Content = "▶ PREVIEW"; }
        if (ringing && !Audio.Playing && !SuppressHardwareAudio && now >= retryAudio)
        { PlayCurrent(); retryAudio = now.AddSeconds(5); }
        if (ringing) NextText.Text = $"!! ACTIVE // {settings.Active.Count} EVENT(S) // VERIFY TO DISMISS";
        else
        {
            var next = settings.Alarms.Select(a => (Alarm: a, Next: Schedule.Next(a, now, TimeZoneInfo.Local))).Where(x => x.Next != null).OrderBy(x => x.Next!.At).FirstOrDefault();
            NextText.Text = next.Next == null ? "No alarms or timers running" : $"NEXT  {next.Next.At.ToLocalTime().ToString("ddd h:mm tt", CultureInfo.InvariantCulture)} / {next.Alarm.Label}";
        }
        if (now.Second == 0)
        {
            TimeZoneInfo.ClearCachedData();
            if (zoneId != TimeZoneInfo.Local.Id) { zoneId = TimeZoneInfo.Local.Id; SyncTasks(); }
        }
    }
    public void Wake()
    {
        TimeZoneInfo.ClearCachedData();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate(); Tick(); SyncTasks();
    }
    private void SyncTasks()
    {
        if (isolated) { DiagnosticText.Text = "ISOLATED TEST SESSION // NO WAKE TASKS REGISTERED"; return; }
        DiagnosticText.Text = scheduler.Sync(settings);
    }
    private void RefreshAlarms()
    {
        AlarmList.ItemsSource = null; AlarmList.ItemsSource = settings.Alarms.OrderBy(a => a.Kind).ThenBy(a => a.Hour).ThenBy(a => a.Minute).ToList();
        EmptyText.Visibility = settings.Alarms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyBox.Visibility = Editor.Visibility == Visibility.Visible ? Visibility.Collapsed : EmptyText.Visibility;
        if (!ringing) StatusLed.Text = settings.Alarms.Any(a => a.Enabled) ? "● SCHEDULED" : "● NONE RUNNING";
    }
    private void AlarmStateChanged()
    {
        bool active = Engine.Phase != AlarmPhase.Idle;
        bool reset = active && (!ringing || escapes != settings.EscapeAttempts);
        ringing = active; escapes = settings.EscapeAttempts;
        NormalPanel.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        PuzzlePanel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        PreviewButton.IsEnabled = !active; DemoButton.IsEnabled = !active; PreviewSound.IsEnabled = !active;
        WakeToggle.IsEnabled = !active;
        Topmost = active;
        StatusLed.Text = active ? "● RINGING" : settings.Alarms.Any(a => a.Enabled) ? "● SCHEDULED" : "● NONE RUNNING";
        StatusLed.Foreground = new SolidColorBrush(active ? colors[2] : colors[0]);
        if (active)
        {
            preview = false; PreviewButton.Content = "▶ PREVIEW";
            CloseEditor();
            RingLabel.Text = Engine.Current!.Alarm.Label + " / " + SoundNames.For(Engine.Current.Alarm.Sound);
            PlayCurrent();
            if (!WakeScheduler.KeepAwake(true)) DiagnosticText.Text = "COULD NOT REQUEST AWAKE STATE // Check power settings.";
            if (reset)
            {
                Verification.Reset();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate(); StartSequence();
            }
            WarningBox.Visibility = escapes > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (escapes > 0) WarningBox.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(450)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) });
        }
        else
        {
            sequenceCancel?.Cancel(); Audio.StopAlarm(); WakeScheduler.KeepAwake(false);
            AudioErrorText.Text = ""; RefreshAlarms();
        }
    }
    private void PlayCurrent()
    {
        if (SuppressHardwareAudio || Engine.Current == null) return;
        Audio.Play(Engine.Current.Alarm.Sound, settings.Volume, settings.Intensity, escapes);
        AudioErrorText.Text = Audio.Error ?? "";
    }
    private async void StartSequence()
    {
        sequenceCancel?.Cancel(); sequenceCancel?.Dispose(); sequenceCancel = new CancellationTokenSource();
        var token = sequenceCancel.Token;
        ReplayButton.IsEnabled = false;
        foreach (var b in tileButtons) { b.Background = (Brush)FindResource("ButtonMetal"); b.Opacity = .5; }
        try
        {
            PuzzleText.Text = "WATCH THE SEQUENCE..."; RoundText.Text = $"{Puzzle.SequenceLength} TILES";
            await Task.Delay(700, token);
            foreach (int tile in Verification.Sequence)
            {
                tileButtons[tile].Background = new SolidColorBrush(colors[tile]); tileButtons[tile].Foreground = Brushes.Black; tileButtons[tile].Opacity = 1;
                if (!SuppressHardwareAudio) Audio.Chime(tile);
                await Task.Delay(700, token);
                tileButtons[tile].Background = (Brush)FindResource("ButtonMetal"); tileButtons[tile].Foreground = new SolidColorBrush(colors[tile]); tileButtons[tile].Opacity = .5;
                await Task.Delay(300, token);
            }
            Verification.BeginInput();
            foreach (var b in tileButtons) b.Opacity = 1;
            PuzzleText.Text = $"YOUR TURN // 0 / {Verification.Sequence.Length}"; ReplayButton.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        finally { foreach (var b in tileButtons) b.Foreground = new SolidColorBrush(colors[(int)b.Tag]); }
    }
    private async void PressTile(int tile)
    {
        if (!ringing) return;
        var result = Verification.Press(tile);
        if (result == PuzzleResult.Ignored) return;
        if (!SuppressHardwareAudio) Audio.Chime(tile);
        tileButtons[tile].BeginAnimation(OpacityProperty, new DoubleAnimation(.3, 1, TimeSpan.FromMilliseconds(180)));
        if (result == PuzzleResult.Complete) { Engine.Dismiss(Verification); return; }
        if (result == PuzzleResult.Retry)
        {
            PuzzleText.Text = "INCORRECT // TRY THE NEW SEQUENCE";
            ReplayButton.IsEnabled = false;
            var token = sequenceCancel!.Token;
            try { await Task.Delay(900, token); if (ringing) StartSequence(); } catch (OperationCanceledException) { }
        }
        else PuzzleText.Text = $"YOUR TURN // {Verification.Position} / {Verification.Sequence.Length}";
    }
    private void ReplayClick(object sender, RoutedEventArgs e) { Verification.Replay(); StartSequence(); }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (!ringing || e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None) return;
        int tile = e.Key switch { Key.D1 or Key.NumPad1 or Key.Q => 0, Key.D2 or Key.NumPad2 or Key.W => 1, Key.D3 or Key.NumPad3 or Key.E => 2, Key.D4 or Key.NumPad4 or Key.R => 3, _ => -1 };
        if (tile >= 0) { e.Handled = true; PressTile(tile); }
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (sessionEnding) { SaveSettings(); return; }
        if (!Engine.RequestClose()) { e.Cancel = true; CloseRejections++; return; }
        SaveSettings();
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void AddClick(object sender, RoutedEventArgs e) => OpenEditor(null);
    private void AddTimerClick(object sender, RoutedEventArgs e) => OpenEditor(null, EntryKind.Timer);
    private void EditClick(object sender, RoutedEventArgs e) => OpenEditor((Alarm)((Button)sender).Tag);
    private void OpenEditor(Alarm? alarm, EntryKind kind = EntryKind.Alarm)
    {
        editorLoading = true;
        editing = alarm?.Id; editorKind = kind = alarm?.Kind ?? kind;
        var time = alarm == null ? TimeOnly.FromDateTime(DateTime.Now.AddMinutes(2)) : new TimeOnly(alarm.Hour, alarm.Minute);
        HourInput.SelectedIndex = (time.Hour % 12 == 0 ? 12 : time.Hour % 12) - 1;
        MinuteInput.SelectedIndex = time.Minute; PeriodInput.SelectedIndex = time.Hour >= 12 ? 1 : 0;
        LabelInput.Text = alarm?.Label ?? "";
        OnceDate.SelectedDate = alarm?.OneTimeDate.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        OnceModeInput.SelectedIndex = alarm is { Kind: EntryKind.Alarm, Days: 0 } ? 1 : 0;
        RepeatInput.SelectedIndex = alarm?.Days switch { null or 0 => 0, 127 => 1, 62 => 2, 65 => 3, _ => 4 };
        int seconds = alarm?.TimerDurationSeconds ?? 300;
        TimerHoursInput.Text = (seconds / 3600).ToString(CultureInfo.InvariantCulture);
        TimerMinutesInput.Text = (seconds % 3600 / 60).ToString(CultureInfo.InvariantCulture);
        TimerSecondsInput.Text = (seconds % 60).ToString(CultureInfo.InvariantCulture);
        EditorSound.SelectedIndex = (int)(alarm?.Sound ?? SoundPreset.Industrial);
        for (int i = 0; i < 7; i++) dayBoxes[i].IsChecked = (alarm?.Days & (1 << i)) != 0 && alarm != null;
        editorLoading = false;
        EditorError.Text = ""; SetEditorMode(true); UpdateEditor();
        if (kind == EntryKind.Timer) TimerMinutesInput.Focus(); else HourInput.Focus();
        Editor.BringIntoView();
    }
    private TimeOnly ReadEditorTime() => ClockTime.From12Hour(HourInput.SelectedIndex + 1, MinuteInput.SelectedIndex, PeriodInput.SelectedIndex == 1);
    private int ReadRepeatDays() => RepeatInput.SelectedIndex switch
    {
        1 => 127, 2 => 62, 3 => 65, 4 => Enumerable.Range(0, 7).Where(i => dayBoxes[i].IsChecked == true).Sum(i => 1 << i), _ => 0
    };
    private bool TryReadDuration(out int seconds)
    {
        seconds = 0;
        if (!int.TryParse(TimerHoursInput.Text, out int hours) || !int.TryParse(TimerMinutesInput.Text, out int minutes)
            || !int.TryParse(TimerSecondsInput.Text, out int remainder) || hours is < 0 or > 168 || minutes is < 0 or > 59 || remainder is < 0 or > 59) return false;
        seconds = hours * 3600 + minutes * 60 + remainder;
        return seconds is >= 1 and <= Countdown.MaximumSeconds;
    }
    private void EditorChanged(object sender, RoutedEventArgs e) { if (ready && !editorLoading) UpdateEditor(); }
    private void UpdateEditor()
    {
        bool timer = editorKind == EntryKind.Timer;
        EditorTitle.Text = editing == null ? timer ? "NEW TIMER" : "NEW ALARM" : timer ? "EDIT TIMER" : "EDIT ALARM";
        SaveEntryButton.Content = timer ? editing == null ? "START TIMER" : "RESTART TIMER" : "SAVE ALARM";
        AlarmTimePanel.Visibility = timer ? Visibility.Collapsed : Visibility.Visible;
        TimerDurationPanel.Visibility = timer ? Visibility.Visible : Visibility.Collapsed;
        OnceOptions.Visibility = RepeatInput.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        OnceDate.Visibility = OnceModeInput.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        DayChoices.Visibility = RepeatInput.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        if (timer)
        {
            EditorSummary.Text = TryReadDuration(out int seconds) ? $"{Countdown.Format(seconds)} countdown. Starts when you save." : "Enter a duration from 1 second to 7 days.";
            return;
        }
        if (HourInput.SelectedIndex < 0 || MinuteInput.SelectedIndex < 0) return;
        var time = ReadEditorTime();
        if (RepeatInput.SelectedIndex == 0)
        {
            var date = OnceModeInput.SelectedIndex == 1 ? DateOnly.FromDateTime(OnceDate.SelectedDate ?? DateTime.Today) : ClockTime.NextDate(time, DateTimeOffset.Now, TimeZoneInfo.Local);
            EditorSummary.Text = $"Once: {date.ToString("ddd, MMM d", CultureInfo.InvariantCulture)} at {time.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
        }
        else EditorSummary.Text = $"{RepeatInput.SelectedItem}: {time.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
    }
    private void TimerPresetClick(object sender, RoutedEventArgs e)
    {
        int seconds = int.Parse((string)((Button)sender).Tag, CultureInfo.InvariantCulture);
        editorLoading = true; TimerHoursInput.Text = "0"; TimerMinutesInput.Text = (seconds / 60).ToString(CultureInfo.InvariantCulture); TimerSecondsInput.Text = "0"; editorLoading = false;
        UpdateEditor();
    }
    private void SetEditorMode(bool isEditing)
    {
        Editor.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;
        foreach (var panel in new FrameworkElement[] { BrandPanel, ClockPanel, QueueToolbar, SoundPanel, WakePanel, AlarmList })
            panel.Visibility = isEditing ? Visibility.Collapsed : Visibility.Visible;
        EmptyBox.Visibility = !isEditing && settings.Alarms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void CloseEditor() => SetEditorMode(false);
    private void CancelEditClick(object sender, RoutedEventArgs e) => CloseEditor();
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var alarm = settings.Alarms.FirstOrDefault(a => a.Id == editing) ?? new Alarm();
        bool timer = editorKind == EntryKind.Timer;
        if (timer)
        {
            if (!TryReadDuration(out int seconds)) { EditorError.Text = "Use hours, 0–59 minutes, and 0–59 seconds. Duration: 1 second to 7 days."; return; }
            Countdown.Start(alarm, seconds, DateTimeOffset.UtcNow);
        }
        else
        {
            if (HourInput.SelectedIndex < 0 || MinuteInput.SelectedIndex < 0 || PeriodInput.SelectedIndex < 0) { EditorError.Text = "Choose an hour, minute, and AM or PM."; return; }
            var time = ReadEditorTime(); int days = ReadRepeatDays();
            if (RepeatInput.SelectedIndex == 4 && days == 0) { EditorError.Text = "Select at least one repeat day, or choose Once."; return; }
            var date = OnceModeInput.SelectedIndex == 1 ? DateOnly.FromDateTime(OnceDate.SelectedDate ?? DateTime.Today) : ClockTime.NextDate(time, DateTimeOffset.Now, TimeZoneInfo.Local);
            if (days == 0 && Schedule.Resolve(date.ToDateTime(time), TimeZoneInfo.Local) <= DateTimeOffset.Now) { EditorError.Text = "Choose a future date, or use Next occurrence."; return; }
            alarm.Kind = EntryKind.Alarm; alarm.Hour = time.Hour; alarm.Minute = time.Minute;
            alarm.Days = days; alarm.OneTimeDate = date;
            alarm.Enabled = true; alarm.ArmedAt = DateTimeOffset.UtcNow; alarm.LastOccurrence = null;
        }
        alarm.Label = string.IsNullOrWhiteSpace(LabelInput.Text) ? timer ? "Timer" : "Alarm" : LabelInput.Text.Trim();
        alarm.Sound = (SoundPreset)Math.Max(0, EditorSound.SelectedIndex);
        if (!settings.Alarms.Contains(alarm)) settings.Alarms.Add(alarm);
        SaveSettings(); CloseEditor(); RefreshAlarms(); SyncTasks(); lastSecond = -1; Tick();
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        var alarm = (Alarm)((Button)sender).Tag;
        settings.Alarms.Remove(alarm); if (editing == alarm.Id) CloseEditor();
        SaveSettings(); RefreshAlarms(); SyncTasks(); lastSecond = -1; Tick();
    }
    private void ToggleAlarm(object sender, RoutedEventArgs e)
    {
        var alarm = (Alarm)((Button)sender).Tag;
        if (alarm.Kind == EntryKind.Timer)
        {
            if (alarm.Enabled) Countdown.Pause(alarm, DateTimeOffset.UtcNow); else Countdown.Resume(alarm, DateTimeOffset.UtcNow);
        }
        else
        {
            alarm.Enabled = !alarm.Enabled;
            if (alarm.Enabled)
            {
                alarm.ArmedAt = DateTimeOffset.UtcNow;
                if (alarm.Days == 0 && Schedule.On(alarm, alarm.OneTimeDate, TimeZoneInfo.Local).At <= DateTimeOffset.Now)
                { alarm.Enabled = false; OpenEditor(alarm); OnceModeInput.SelectedIndex = 0; EditorError.Text = "This alarm has passed. Save to use the next occurrence."; }
            }
        }
        SaveSettings(); RefreshAlarms(); SyncTasks(); lastSecond = -1; Tick();
    }
    private void PreviewClick(object sender, RoutedEventArgs e)
    {
        if (preview) { preview = false; Audio.Stop(); PreviewButton.Content = "▶ PREVIEW"; return; }
        Audio.Play((SoundPreset)PreviewSound.SelectedIndex, settings.Volume, settings.Intensity, 0);
        AudioErrorText.Text = Audio.Error ?? ""; preview = Audio.Playing;
        previewUntil = DateTimeOffset.Now.AddSeconds(10); PreviewButton.Content = preview ? "■ STOP" : "▶ PREVIEW";
    }
    private void DemoClick(object sender, RoutedEventArgs e) => Engine.RingDemo((SoundPreset)PreviewSound.SelectedIndex);
    private void AudioChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready) return;
        settings.Volume = VolumeSlider.Value; settings.Intensity = IntensitySlider.Value;
        VolumeText.Text = $"{settings.Volume * 100:0}%";
        Audio.Configure(settings.Volume, settings.Intensity, escapes); saveTimer.Stop(); saveTimer.Start();
    }
    private void WakeToggleClick(object sender, RoutedEventArgs e) { settings.WakeEnabled = WakeToggle.IsChecked == true; SaveSettings(); SyncTasks(); }
}
