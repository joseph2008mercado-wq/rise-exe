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
    private bool ready, ringing, sessionEnding, preview;
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
        for (int i = 0; i < 7; i++) { dayBoxes[i] = new CheckBox { Content = names[i], FontSize = 10 }; DayChoices.Children.Add(dayBoxes[i]); }
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
        Clock.Time = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture); Clock.InvalidateVisual();
        Visualizer.Bands = Audio.Bands; Visualizer.InvalidateVisual();
        if (now.ToUnixTimeSeconds() == lastSecond) return;
        lastSecond = now.ToUnixTimeSeconds();
        DateText.Text = now.ToString("ddd / dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        bool changed = Engine.Tick(now, TimeZoneInfo.Local);
        if (changed) { RefreshAlarms(); SyncTasks(); }
        if (preview && now >= previewUntil) { preview = false; Audio.Stop(); PreviewButton.Content = "▶ PREVIEW"; }
        if (ringing && !Audio.Playing && !SuppressHardwareAudio && now >= retryAudio)
        { PlayCurrent(); retryAudio = now.AddSeconds(5); }
        if (ringing) NextText.Text = $"!! ACTIVE // {settings.Active.Count} EVENT(S) // VERIFY TO DISMISS";
        else
        {
            var next = settings.Alarms.Select(a => (Alarm: a, Next: Schedule.Next(a, now, TimeZoneInfo.Local))).Where(x => x.Next != null).OrderBy(x => x.Next!.At).FirstOrDefault();
            NextText.Text = next.Next == null ? "No alarms enabled" : $"NEXT  {next.Next.At.ToLocalTime():ddd HH:mm} / {next.Alarm.Label}";
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
        AlarmList.ItemsSource = null; AlarmList.ItemsSource = settings.Alarms.OrderBy(a => a.Hour).ThenBy(a => a.Minute).ToList();
        EmptyText.Visibility = settings.Alarms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyBox.Visibility = EmptyText.Visibility;
        if (!ringing) StatusLed.Text = settings.Alarms.Any(a => a.Enabled) ? "● ALARMS ENABLED" : "● NO ALARMS";
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
        StatusLed.Text = active ? "● ALARM ACTIVE" : settings.Alarms.Any(a => a.Enabled) ? "● ALARMS ENABLED" : "● NO ALARMS";
        StatusLed.Foreground = new SolidColorBrush(active ? colors[2] : colors[0]);
        if (active)
        {
            preview = false; PreviewButton.Content = "▶ PREVIEW";
            Editor.Visibility = Visibility.Collapsed;
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
            sequenceCancel?.Cancel(); Audio.Stop(); WakeScheduler.KeepAwake(false);
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
            PuzzleText.Text = "WATCH THE SEQUENCE..."; RoundText.Text = $"ROUND {Verification.Round:00} / 03";
            await Task.Delay(700, token);
            foreach (int tile in Verification.Sequence)
            {
                tileButtons[tile].Background = new SolidColorBrush(colors[tile]); tileButtons[tile].Foreground = Brushes.Black; tileButtons[tile].Opacity = 1;
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
        tileButtons[tile].BeginAnimation(OpacityProperty, new DoubleAnimation(.3, 1, TimeSpan.FromMilliseconds(180)));
        if (result == PuzzleResult.Complete) { Engine.Dismiss(Verification); return; }
        if (result is PuzzleResult.RoundComplete or PuzzleResult.Retry)
        {
            PuzzleText.Text = result == PuzzleResult.Retry ? "MISMATCH // NEW SEQUENCE. YOU'VE GOT THIS." : "MATCH CONFIRMED // NEXT ROUND";
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
    private void EditClick(object sender, RoutedEventArgs e) => OpenEditor((Alarm)((Button)sender).Tag);
    private void OpenEditor(Alarm? alarm)
    {
        editing = alarm?.Id; EditorTitle.Text = alarm == null ? "NEW ALARM" : "EDIT ALARM";
        TimeInput.Text = alarm?.DisplayTime ?? DateTime.Now.AddMinutes(2).ToString("HH:mm", CultureInfo.InvariantCulture);
        LabelInput.Text = alarm?.Label ?? "Alarm";
        OnceDate.SelectedDate = alarm?.OneTimeDate.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        EditorSound.SelectedIndex = (int)(alarm?.Sound ?? SoundPreset.Industrial);
        for (int i = 0; i < 7; i++) dayBoxes[i].IsChecked = (alarm?.Days & (1 << i)) != 0 && alarm != null;
        EditorError.Text = ""; Editor.Visibility = Visibility.Visible; TimeInput.Focus();
        Editor.BringIntoView();
    }
    private void CancelEditClick(object sender, RoutedEventArgs e) => Editor.Visibility = Visibility.Collapsed;
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!TimeOnly.TryParseExact(TimeInput.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) { EditorError.Text = "Use a 24-hour time, for example 07:30 or 23:45."; return; }
        int days = Enumerable.Range(0, 7).Where(i => dayBoxes[i].IsChecked == true).Sum(i => 1 << i);
        var alarm = settings.Alarms.FirstOrDefault(a => a.Id == editing) ?? new Alarm();
        var date = DateOnly.FromDateTime(OnceDate.SelectedDate ?? DateTime.Today);
        if (days == 0 && Schedule.Resolve(date.ToDateTime(time), TimeZoneInfo.Local) <= DateTimeOffset.Now) { EditorError.Text = "Choose a future date/time, or select repeating weekdays."; return; }
        alarm.Hour = time.Hour; alarm.Minute = time.Minute; alarm.Label = string.IsNullOrWhiteSpace(LabelInput.Text) ? "Alarm" : LabelInput.Text.Trim();
        alarm.Days = days; alarm.OneTimeDate = date; alarm.Sound = (SoundPreset)Math.Max(0, EditorSound.SelectedIndex);
        alarm.Enabled = true; alarm.ArmedAt = DateTimeOffset.UtcNow; alarm.LastOccurrence = null;
        if (!settings.Alarms.Contains(alarm)) settings.Alarms.Add(alarm);
        SaveSettings(); RefreshAlarms(); SyncTasks(); Editor.Visibility = Visibility.Collapsed; lastSecond = -1; Tick();
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        var alarm = (Alarm)((Button)sender).Tag;
        settings.Alarms.Remove(alarm); if (editing == alarm.Id) Editor.Visibility = Visibility.Collapsed;
        SaveSettings(); RefreshAlarms(); SyncTasks(); lastSecond = -1; Tick();
    }
    private void ToggleAlarm(object sender, RoutedEventArgs e)
    {
        var box = (CheckBox)sender; var alarm = (Alarm)box.Tag; alarm.Enabled = box.IsChecked == true;
        if (alarm.Enabled)
        {
            alarm.ArmedAt = DateTimeOffset.UtcNow;
            if (alarm.Days == 0 && Schedule.On(alarm, alarm.OneTimeDate, TimeZoneInfo.Local).At <= DateTimeOffset.Now)
            { alarm.Enabled = false; OpenEditor(alarm); EditorError.Text = "This one-time alarm has passed. Choose a future time and save."; }
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
