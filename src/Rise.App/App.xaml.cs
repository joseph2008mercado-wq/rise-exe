using System.IO;
using System.Windows;
using Rise.Core;

namespace Rise.App;

public partial class App : Application
{
    private SingleInstance? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool selfTest = e.Args.Contains("--self-test");
        string? isolated = null;
        int dataIndex = Array.IndexOf(e.Args, "--data-dir");
        if (dataIndex >= 0 && e.Args.Length > dataIndex + 1) isolated = Path.GetFullPath(e.Args[dataIndex + 1]);
        if (!selfTest)
        {
            instance = new SingleInstance(isolated);
            if (!instance.IsPrimary) { instance.Signal(); Shutdown(); return; }
        }
        string dataDir = isolated ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RISE");
        if (selfTest) dataDir = Path.Combine(Path.GetTempPath(), "RISE-test-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(Path.Combine(dataDir, "settings.json"));
            var settings = store.Load();
            var window = new MainWindow(settings, store, isolated != null || selfTest);
            MainWindow = window;
            instance?.Listen(() => Dispatcher.InvokeAsync(window.Wake));
            if (selfTest)
            {
                window.Loaded += async (_, _) =>
                {
                    int result = await AppSmokeTests.Run(window, dataDir);
                    Shutdown(result);
                };
            }
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "RISE // STARTUP ERROR", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
