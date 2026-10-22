using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Xml.Linq;
using Rise.Core;

namespace Rise.App;

internal static class AppSmokeTests
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    public static async Task<int> Run(MainWindow window, string dataDir)
    {
        var results = new List<string>();
        string output = Environment.GetEnvironmentVariable("RISE_TEST_OUTPUT") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke");
        Directory.CreateDirectory(output);
        void Assert(bool condition, string name) { if (!condition) throw new Exception(name); results.Add("PASS " + name); }
        try
        {
            // Let the initial Show()/Loaded stack unwind before exercising native messages.
            await Task.Delay(100);
            window.SuppressHardwareAudio = true;
            AudioRoutingTests.Run(Assert);
            HeadphoneKeepAliveTests.Run(Assert);
            var keepAliveToggle = (CheckBox)window.FindName("KeepAliveToggle");
            var keepAliveInterval = (ComboBox)window.FindName("KeepAliveInterval");
            keepAliveToggle.IsChecked = false; keepAliveToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            keepAliveInterval.SelectedIndex = 1;
            var keepAliveSaved = new SettingsStore(Path.Combine(dataDir, "settings.json")).Load();
            Assert(!keepAliveSaved.HeadphoneKeepAlive && keepAliveSaved.KeepAliveMinutes == 10, "keep-alive toggle and ten-minute UI selection persist");
            keepAliveToggle.IsChecked = true; keepAliveToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            keepAliveInterval.SelectedIndex = 0;
            Assert(window.Engine.Settings.HeadphoneKeepAlive && window.Engine.Settings.KeepAliveMinutes == 5, "keep-alive UI restores enabled five-minute default");
            using (var devices = new WindowsAudioDevices())
            {
                var endpoints = devices.Read();
                Assert(endpoints.Endpoints.All(e => e.Active && e.Volume is >= 0 and <= 1), "native Core Audio enumerates output types and volume without changing Windows settings");
                if (endpoints.DefaultId != null)
                {
                    // Exercise native WASAPI initialization/resampling with literal
                    // zero samples. Never invoke fallback or change a real volume.
                    using var silent = devices.Open(endpoints.DefaultId, new SilenceProvider());
                    silent.Play(); await Task.Delay(150);
                    Assert(silent.PlaybackState == NAudio.Wave.PlaybackState.Playing, "native WASAPI streams silence on the default endpoint");
                    silent.Stop();
                }
                else results.Add("INFO Native WASAPI playback unavailable: runner has no default output endpoint.");
            }
            ((Button)window.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((ComboBox)window.FindName("HourInput")).SelectedIndex = 6;
            ((ComboBox)window.FindName("MinuteInput")).SelectedIndex = 30;
            ((ComboBox)window.FindName("PeriodInput")).SelectedIndex = 1;
            ((TextBox)window.FindName("LabelInput")).Text = "AUTOMATED UI TEST";
            ((ComboBox)window.FindName("OnceModeInput")).SelectedIndex = 1;
            ((DatePicker)window.FindName("OnceDate")).SelectedDate = DateTime.Today.AddDays(1);
            // Invoke the actual editor button to exercise parsing, saving and list refresh.
            var editor = (Border)window.FindName("Editor");
            Button FindButton(DependencyObject parent, string content)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is Button b && (string?)b.Content == content) return b;
                    try { return FindButton(child, content); } catch (InvalidOperationException) { }
                }
                throw new InvalidOperationException("Button not found: " + content);
            }
            window.UpdateLayout(); Capture(window, Path.Combine(output, "editor-alarm.png"), 1.5);
            ((Button)window.FindName("SaveEntryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.Engine.Settings.Alarms.Count == 1 && window.Engine.Settings.Alarms[0].Label == "AUTOMATED UI TEST", "actual editor creates and saves alarm");
            Assert(window.Engine.Settings.Alarms[0].Hour == 19 && window.Engine.Settings.Alarms[0].Minute == 30 && window.Engine.Settings.Alarms[0].DisplayTime == "7:30 PM", "AM/PM editor saves and displays the correct time");
            window.UpdateLayout();
            var list = (ItemsControl)window.FindName("AlarmList");
            FindButton(list, "EDIT").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((TextBox)window.FindName("LabelInput")).Text = "EDITED UI TEST";
            ((Button)window.FindName("SaveEntryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.Engine.Settings.Alarms.Single().Label == "EDITED UI TEST", "actual editor modifies alarm");
            Assert(new SettingsStore(Path.Combine(dataDir, "settings.json")).Load().Alarms.Single().Label == "EDITED UI TEST", "UI edits survive settings reload");
            window.UpdateLayout();
            FindButton(list, "×").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.Engine.Settings.Alarms.Count == 0, "actual delete button removes alarm");
            ((Button)window.FindName("AddTimerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((TextBox)window.FindName("TimerMinutesInput")).Text = "10";
            ((TextBox)window.FindName("TimerSecondsInput")).Text = "20";
            ((TextBox)window.FindName("LabelInput")).Text = "TIMER UI TEST";
            window.UpdateLayout(); Capture(window, Path.Combine(output, "editor-timer.png"), 1.5);
            ((Button)window.FindName("SaveEntryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var timer = window.Engine.Settings.Alarms.Single(); Guid timerId = timer.Id;
            Assert(timer.Kind == EntryKind.Timer && timer.TimerDurationSeconds == 620 && timer.Enabled && timer.TimerEndsAt > DateTimeOffset.UtcNow, "timer editor starts a saved countdown");
            window.UpdateLayout(); FindButton(list, "Pause").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(!timer.Enabled && timer.TimerEndsAt == null && timer.TimerRemainingSeconds > 0, "timer pause preserves remaining duration");
            window.UpdateLayout(); FindButton(list, "Start").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(timer.Enabled && timer.TimerEndsAt > DateTimeOffset.UtcNow, "timer resumes from paused duration");
            window.UpdateLayout(); FindButton(list, "EDIT").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((TextBox)window.FindName("TimerMinutesInput")).Text = "2";
            ((TextBox)window.FindName("TimerSecondsInput")).Text = "0";
            ((Button)window.FindName("SaveEntryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.Engine.Settings.Alarms.Single().Id == timerId && timer.TimerDurationSeconds == 120, "editing existing timer restarts it without creating duplicates");
            var savedTimer = new SettingsStore(Path.Combine(dataDir, "settings.json")).Load().Alarms.Single();
            Assert(savedTimer.Kind == EntryKind.Timer && savedTimer.TimerEndsAt == timer.TimerEndsAt, "running timer survives settings reload with exact deadline");
            window.UpdateLayout(); FindButton(list, "×").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.Engine.Settings.Alarms.Count == 0, "delete button removes timer");
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                window.UpdateLayout();
                Assert(window.ActualWidth >= 500 && window.ActualHeight >= 560, $"WPF layout at {scale * 100}% render scale");
                Capture(window, Path.Combine(output, $"idle-{scale:0.00}.png"), scale);
            }
            var now = DateTimeOffset.Now;
            var a = new Alarm { Hour = (now.Hour + 1) % 24, Days = 127, Label = "TEST & <XML>" };
            string xml = WakeScheduler.Xml(a, @"C:\Program Files\RISE\RISE.exe", "S-1-5-21-123", now, TimeZoneInfo.Local);
            var doc = XDocument.Parse(xml); XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            Assert(doc.Descendants(ns + "WakeToRun").Single().Value == "true", "WakeToRun serialized");
            Assert(doc.Descendants(ns + "DaysOfWeek").Single().Elements().Count() == 7, "weekly task trigger serialized");
            Assert(doc.Descendants(ns + "Command").Single().Value.Contains("Program Files"), "task executable path preserved");
            a.Days = 0; a.OneTimeDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            Assert(XDocument.Parse(WakeScheduler.Xml(a, "RISE.exe", "S-1-5-21-123", now, TimeZoneInfo.Local)).Descendants(ns + "TimeTrigger").Any(), "one-time task trigger serialized");
            var wakeTimer = new Alarm(); Countdown.Start(wakeTimer, 120, now);
            var timerXml = XDocument.Parse(WakeScheduler.Xml(wakeTimer, "RISE.exe", "S-1-5-21-123", now, TimeZoneInfo.Local));
            Assert(timerXml.Descendants(ns + "TimeTrigger").Single().Element(ns + "StartBoundary")!.Value.EndsWith("Z") && !timerXml.Descendants(ns + "CalendarTrigger").Any(), "timer wake task uses an absolute UTC deadline");
            var gapAlarm = new Alarm { Hour = 2, Minute = 30, Days = 127 };
            var gapDoc = XDocument.Parse(WakeScheduler.Xml(gapAlarm, "RISE.exe", "S-1-5-21-123", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")));
            Assert(gapDoc.Descendants(ns + "TimeTrigger").Any(t => t.Element(ns + "StartBoundary")!.Value == "2026-03-08T03:00:00"), "spring-gap task explicitly wakes at first valid minute");
            Assert(gapDoc.Descendants(ns + "CalendarTrigger").Single().Element(ns + "StartBoundary")!.Value.EndsWith("T02:30:00"), "weekly task preserves original wall-clock time");
            string folder = "RISE-TEST-" + Guid.NewGuid().ToString("N");
            try
            {
                var settings = new Settings { Alarms = [a] };
                var scheduler = new WakeScheduler();
                string result = scheduler.Sync(settings, folder);
                Assert(result.Contains("1 ARMED"), "actual Task Scheduler registration: " + result);
                dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!; service.Connect();
                dynamic root = service.GetFolder(@"\"); dynamic taskFolder = root.GetFolder(folder);
                try
                {
                    dynamic task = taskFolder.GetTask(a.Id.ToString("D"));
                    Assert(((string)task.Xml).Contains("<WakeToRun>true</WakeToRun>"), "registered task has wake flag");
                    Marshal.FinalReleaseComObject(task);
                    settings.Alarms.Add(wakeTimer);
                    Assert(scheduler.Sync(settings, folder).Contains("2 ARMED"), "actual Task Scheduler registers running timer deadline");
                    Countdown.Pause(wakeTimer, DateTimeOffset.UtcNow);
                    Assert(scheduler.Sync(settings, folder).Contains("1 ARMED"), "pausing timer removes its scheduled wake task");
                    settings.Alarms.Clear(); scheduler.Sync(settings, folder);
                    dynamic tasks = taskFolder.GetTasks(0); Assert((int)tasks.Count == 0, "deleted alarm cleans scheduled task"); Marshal.FinalReleaseComObject(tasks);
                }
                finally { Marshal.FinalReleaseComObject(taskFolder); root.DeleteFolder(folder, 0); Marshal.FinalReleaseComObject(root); Marshal.FinalReleaseComObject(service); }
            }
            catch (Exception ex) { throw new Exception("Task Scheduler integration failed", ex); }
            string uninstallFolder = "RISE-TEST-" + Guid.NewGuid().ToString("N");
            var uninstallScheduler = new WakeScheduler();
            try
            {
                Assert(uninstallScheduler.Sync(new Settings { Alarms = [a] }, uninstallFolder).Contains("1 ARMED"), "uninstaller cleanup fixture registers an isolated wake task");
                Assert(uninstallScheduler.RemoveAll(uninstallFolder), "uninstaller removes owned wake tasks and their folder");
                Assert(uninstallScheduler.RemoveAll(uninstallFolder), "uninstaller safely handles an already removed task folder");
            }
            finally { uninstallScheduler.RemoveAll(uninstallFolder); }
            window.Engine.RingDemo(SoundPreset.Industrial);
            Assert(window.Engine.Phase == AlarmPhase.Ringing, "real WPF window enters ringing");
            ((Button)FindButton(window, "×")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.IsVisible && window.CloseRejections == 1 && window.Engine.Phase == AlarmPhase.Embarrassment, "Window.Closing cancels close and activates embarrassment");
            Assert(window.Verification.Sequence.Length == 5 && window.Verification.Position == 0 && !window.Verification.Completed, "escape resets unfinished puzzle");
            Capture(window, Path.Combine(output, "embarrassment.png"), 1.5);
            var restored = new SettingsStore(Path.Combine(dataDir, "settings.json")).Load();
            Assert(restored.Active.Count == 1 && restored.EscapeAttempts == 1, "ringing + escape state persisted by WPF window");
            SendMessage(new WindowInteropHelper(window).Handle, 0x0112, new IntPtr(0xF060), IntPtr.Zero);
            for (int wait = 0; wait < 100 && window.CloseRejections < 2; wait++) await Task.Delay(20);
            Assert(window.IsVisible && window.CloseRejections >= 2, "native system close request remains rejected");
            window.Verification.BeginInput();
            foreach (int t in window.Verification.Sequence.ToArray()) window.Verification.Press(t);
            window.Engine.Dismiss(window.Verification);
            Assert(window.Engine.Phase == AlarmPhase.Idle && !window.Audio.AlarmPlaying && !window.Topmost, "single-sequence puzzle stops alarm and restores normal state");
            foreach (SoundPreset preset in Enum.GetValues<SoundPreset>())
            {
                var provider = new SynthProvider { Preset = preset }; float[] buffer = new float[4096];
                int reads = (int)(Synthesizer.Duration(preset) * Synthesizer.SampleRate / buffer.Length) + 3;
                bool full = true; for (int i = 0; i < reads; i++) full &= provider.Read(buffer, 0, buffer.Length) == buffer.Length;
                Assert(full && buffer.Any(x => Math.Abs(x) > .01), preset + " provider streams past loop without EOF");
                var baseline = new SynthProvider { Preset = preset, Volume = 1 };
                var withChime = new SynthProvider { Preset = preset, Volume = 1 };
                float[] plain = new float[4096], mixed = new float[4096];
                withChime.Chime(2); baseline.Read(plain, 0, plain.Length); withChime.Read(mixed, 0, mixed.Length);
                Assert(mixed.Zip(plain).Average(x => Math.Abs(x.First - x.Second)) > .1 && mixed.All(x => float.IsFinite(x) && Math.Abs(x) <= .940001), preset + " tile chime is audible in the same limited stream");
            }
            var tail = new SynthProvider { AlarmEnabled = false, Volume = 1 };
            tail.Chime(3); float[] ending = new float[4096];
            Assert(tail.Read(ending, 0, ending.Length) == ending.Length && ending.Any(x => Math.Abs(x) > .1), "final tile chime remains audible with alarm disabled");
            int tailReads = 0;
            while (tail.Read(ending, 0, ending.Length) > 0 && tailReads++ < 20) { }
            Assert(tailReads < 20 && tail.Read(ending, 0, ending.Length) == 0, "chime tail ends without restarting alarm");
            await Task.Delay(50);
            window.Close(); Assert(!window.IsVisible, "normal close succeeds after dismissal");
            File.WriteAllLines(Path.Combine(output, "results.txt"), results);
            return 0;
        }
        catch (Exception ex)
        {
            results.Add("FAIL " + ex);
            File.WriteAllLines(Path.Combine(output, "results.txt"), results);
            return 1;
        }
        finally { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); }
    }
    private static void Capture(Window window, string path, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * scale), (int)(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
