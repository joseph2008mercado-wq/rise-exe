using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Rise.Core;

namespace Rise.App;

public sealed class WakeScheduler
{
    private const string Namespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static string Escape(string text) => SecurityElement.Escape(text)!;
    public static string FolderName => "RISE-" + SingleInstance.UserKey;

    public static string Xml(Alarm alarm, string executable, string sid, DateTimeOffset now, TimeZoneInfo zone)
    {
        var next = Schedule.Next(alarm, now, zone) ?? throw new InvalidOperationException("Alarm has no future occurrence.");
        var local = TimeZoneInfo.ConvertTime(next.At, zone).DateTime;
        if (alarm.Kind == EntryKind.Alarm && alarm.Days != 0) local = local.Date.AddHours(alarm.Hour).AddMinutes(alarm.Minute);
        string boundary = alarm.Kind == EntryKind.Timer ? next.At.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        string[] days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
        string trigger = alarm.Kind == EntryKind.Timer || alarm.Days == 0
            ? $"<TimeTrigger><StartBoundary>{boundary}</StartBoundary><Enabled>true</Enabled></TimeTrigger>"
            : $"<CalendarTrigger><StartBoundary>{boundary}</StartBoundary><Enabled>true</Enabled><ScheduleByWeek><WeeksInterval>1</WeeksInterval><DaysOfWeek>{string.Concat(Enumerable.Range(0, 7).Where(d => (alarm.Days & (1 << d)) != 0).Select(d => $"<{days[d]}/>"))}</DaysOfWeek></ScheduleByWeek></CalendarTrigger>";
        // Explicit spring-gap triggers wake at the first valid minute even if Windows skips
        // a weekly trigger whose wall-clock minute does not exist. Precompute five years
        // so this also works when the app stays closed across DST changes.
        if (alarm.Kind == EntryKind.Alarm && alarm.Days != 0)
        {
            var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
            for (int i = 0; i < 366 * 5; i++)
            {
                var date = first.AddDays(i);
                var wall = date.ToDateTime(new TimeOnly(alarm.Hour, alarm.Minute));
                if ((alarm.Days & (1 << (int)date.DayOfWeek)) == 0 || !zone.IsInvalidTime(wall)) continue;
                var shifted = TimeZoneInfo.ConvertTime(Schedule.Resolve(wall, zone), zone).DateTime;
                if (Schedule.Resolve(wall, zone) <= now) continue;
                trigger += $"<TimeTrigger><StartBoundary>{shifted:yyyy-MM-dd'T'HH:mm:ss}</StartBoundary><Enabled>true</Enabled></TimeTrigger>";
            }
        }
        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="{Namespace}">
          <RegistrationInfo><Description>RISE alarm: {Escape(alarm.Label)}</Description></RegistrationInfo>
          <Triggers>{trigger}</Triggers>
          <Principals><Principal id="User"><UserId>{sid}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
          <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><WakeToRun>true</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>
          <Actions Context="User"><Exec><Command>{Escape(executable)}</Command><Arguments>--wake {alarm.Id:D}</Arguments></Exec></Actions>
        </Task>
        """;
    }

    public string Sync(Settings settings, string? folderOverride = null)
    {
        dynamic? service = null, root = null, folder = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            service!.Connect(); root = service.GetFolder(@"\");
            string folderName = folderOverride ?? FolderName;
            try { folder = root.GetFolder(folderName); }
            catch (Exception ex) when (ex is COMException or FileNotFoundException) { folder = root.CreateFolder(folderName); }
            var now = DateTimeOffset.Now;
            var wanted = settings.WakeEnabled ? settings.Alarms.Where(a => a.Enabled && Schedule.Next(a, now, TimeZoneInfo.Local) != null).ToList() : [];
            var names = wanted.Select(a => a.Id.ToString("D")).ToHashSet();
            dynamic tasks = folder.GetTasks(0);
            try
            {
                var stale = new List<string>();
                foreach (dynamic task in tasks)
                {
                    try { if (!names.Contains((string)task.Name)) stale.Add((string)task.Name); }
                    finally { Marshal.FinalReleaseComObject(task); }
                }
                foreach (string name in stale) folder.DeleteTask(name, 0);
            }
            finally { Marshal.FinalReleaseComObject(tasks); }
            string exe = Environment.ProcessPath!;
            string sid = WindowsIdentity.GetCurrent().User!.Value;
            foreach (var alarm in wanted)
            {
                dynamic registered = folder.RegisterTask(alarm.Id.ToString("D"), Xml(alarm, exe, sid, now, TimeZoneInfo.Local), 6, sid, null, 3, null);
                Marshal.FinalReleaseComObject(registered);
            }
            return settings.WakeEnabled ? $"WAKE TASKS READY // {wanted.Count} ARMED" : "WAKE TASKS OFF // KEEP RISE OPEN";
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { return "WAKE UNAVAILABLE // " + ex.Message + " Keep RISE open and Windows awake."; }
        finally
        {
            if (folder != null) Marshal.FinalReleaseComObject(folder);
            if (root != null) Marshal.FinalReleaseComObject(root);
            if (service != null) Marshal.FinalReleaseComObject(service);
        }
    }

    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    public static bool KeepAwake(bool active) => SetThreadExecutionState(active ? 0x80000003u : 0x80000000u) != 0;
}
