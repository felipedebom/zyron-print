using System.Text.Json;
using Zyron.Print.Infrastructure;
using Zyron.Print.Models;

namespace Zyron.Print.Configuration;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly AppPaths _paths;
    private readonly FileLogger _logger;
    private readonly object _gate = new();
    private AppSettings _current;

    public SettingsStore(AppPaths paths, FileLogger logger)
    {
        _paths = paths;
        _logger = logger;
        _current = Load();
    }

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return Clone(_current);
            }
        }
    }

    public void Save(AppSettings settings)
    {
        settings.PaperWidth = settings.PaperWidth == 80 ? 80 : 58;
        settings.PollIntervalSeconds = Math.Clamp(settings.PollIntervalSeconds, 2, 60);
        lock (_gate)
        {
            _current = Clone(settings);
            File.WriteAllText(_paths.SettingsFile, JsonSerializer.Serialize(_current, JsonOptions));
        }
        _logger.Info("Configurações salvas.");
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_paths.SettingsFile))
            {
                return new AppSettings();
            }
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_paths.SettingsFile)) ?? new AppSettings();
        }
        catch (Exception exception)
        {
            _logger.Error("Não foi possível carregar as configurações.", exception);
            return new AppSettings();
        }
    }

    private static AppSettings Clone(AppSettings value) => new()
    {
        PrinterName = value.PrinterName,
        PaperWidth = value.PaperWidth,
        CutPaper = value.CutPaper,
        StartWithWindows = value.StartWithWindows,
        SupabaseUrl = value.SupabaseUrl,
        SupabaseAnonKey = value.SupabaseAnonKey,
        PollIntervalSeconds = value.PollIntervalSeconds
    };
}

