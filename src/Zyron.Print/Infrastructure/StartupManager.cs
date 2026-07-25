using Microsoft.Win32;

namespace Zyron.Print.Infrastructure;

public sealed class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ZYRON Print";
    private readonly FileLogger _logger;

    public StartupManager(FileLogger logger) => _logger = logger;

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Application.ExecutablePath}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
        _logger.Info(enabled ? "Inicialização com o Windows ativada." : "Inicialização com o Windows desativada.");
    }
}

