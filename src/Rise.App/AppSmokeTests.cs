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
            ((Button)window.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((TextBox)window.FindName("TimeInput")).Text = DateTime.Now.AddHours(1).ToString("HH:mm");
            ((TextBox)window.FindName("LabelInput")).Text = "AUTOMATED UI TEST";
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
            FindButton(editor, "SAVE EVENT ↗").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.Engine.Settings.Alarms.Count == 1 && window.Engine.Settings.Alarms[0].Label == "AUTOMATED UI TEST", "actual editor creates and saves alarm");
            window.UpdateLayout();
            var list = (ItemsControl)window.FindName("AlarmList");
            FindButton(list, "EDIT").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((TextBox)window.FindName("LabelInput")).Text = "EDITED UI TEST";
            FindButton(editor, "SAVE EVENT ↗").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.Engine.Settings.Alarms.Single().Label == "EDITED UI TEST", "actual editor modifies alarm");
            Assert(new SettingsStore(Path.Combine(dataDir, "settings.json")).Load().Alarms.Single().Label == "EDITED UI TEST", "UI edits survive settings reload");
            window.UpdateLayout();
            FindButton(list, "×").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.Engine.Settings.Alarms.Count == 0, "actual delete button removes alarm");
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
                    settings.Alarms.Clear(); scheduler.Sync(settings, folder);
                    dynamic tasks = taskFolder.GetTasks(0); Assert((int)tasks.Count == 0, "deleted alarm cleans scheduled task"); Marshal.FinalReleaseComObject(tasks);
                }
                finally { Marshal.FinalReleaseComObject(taskFolder); root.DeleteFolder(folder, 0); Marshal.FinalReleaseComObject(root); Marshal.FinalReleaseComObject(service); }
            }
            catch (Exception ex) { throw new Exception("Task Scheduler integration failed", ex); }
            window.Engine.RingDemo(SoundPreset.Industrial);
            Assert(window.Engine.Phase == AlarmPhase.Ringing, "real WPF window enters ringing");
            ((Button)FindButton(window, "×")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.IsVisible && window.CloseRejections == 1 && window.Engine.Phase == AlarmPhase.Embarrassment, "Window.Closing cancels close and activates embarrassment");
            Assert(window.Verification.Round == 1 && window.Verification.Position == 0, "escape resets unfinished puzzle");
            Capture(window, Path.Combine(output, "embarrassment.png"), 1.5);
            var restored = new SettingsStore(Path.Combine(dataDir, "settings.json")).Load();
            Assert(restored.Active.Count == 1 && restored.EscapeAttempts == 1, "ringing + escape state persisted by WPF window");
            SendMessage(new WindowInteropHelper(window).Handle, 0x0112, new IntPtr(0xF060), IntPtr.Zero);
            for (int wait = 0; wait < 100 && window.CloseRejections < 2; wait++) await Task.Delay(20);
            Assert(window.IsVisible && window.CloseRejections >= 2, "native system close request remains rejected");
            for (int r = 0; r < 3; r++)
            {
                window.Verification.BeginInput();
                foreach (int t in window.Verification.Sequence.ToArray()) window.Verification.Press(t);
            }
            window.Engine.Dismiss(window.Verification);
            Assert(window.Engine.Phase == AlarmPhase.Idle && !window.Audio.Playing && !window.Topmost, "verified puzzle stops audio and restores normal state");
            foreach (SoundPreset preset in Enum.GetValues<SoundPreset>())
            {
                var provider = new SynthProvider { Preset = preset }; float[] buffer = new float[4096];
                int reads = (int)(Synthesizer.Duration(preset) * Synthesizer.SampleRate / buffer.Length) + 3;
                bool full = true; for (int i = 0; i < reads; i++) full &= provider.Read(buffer, 0, buffer.Length) == buffer.Length;
                Assert(full && buffer.Any(x => Math.Abs(x) > .01), preset + " provider streams past loop without EOF");
            }
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
