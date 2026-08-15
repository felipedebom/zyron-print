using Zyron.Print.Configuration;
using Zyron.Print.Infrastructure;
using Zyron.Print.Printing;
using Zyron.Print.Services;
using Velopack;

namespace Zyron.Print;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        using var singleInstance = new Mutex(true, @"Local\ZYRON.Print", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("O ZYRON Print já está em execução.", "ZYRON Print",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        var paths = AppPaths.Create();
        var logger = new FileLogger(paths.LogDirectory);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => logger.Error("Falha inesperada na interface.", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            logger.Error("Falha inesperada no aplicativo.", eventArgs.ExceptionObject as Exception);

        var settingsStore = new SettingsStore(paths, logger);
        var credentialStore = new CredentialStore(paths, logger);
        var printer = new RawPrinterService(logger);
        var api = new SupabaseDeviceClient(settingsStore, credentialStore, logger);
        var worker = new PrintQueueWorker(settingsStore, credentialStore, api, printer, logger);
        var autoUpdater = new AutoUpdateService(logger);
        var startMinimized = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        Application.Run(new MainForm(
            settingsStore,
            credentialStore,
            printer,
            api,
            worker,
            autoUpdater,
            new StartupManager(logger),
            logger,
            startMinimized));
    }
}
