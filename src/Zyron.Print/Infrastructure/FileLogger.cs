namespace Zyron.Print.Infrastructure;

public sealed class FileLogger
{
    private readonly string _directory;
    private readonly object _gate = new();

    public FileLogger(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        DeleteOldLogs();
    }

    public string DirectoryPath => _directory;

    public void Info(string message) => Write("INFO", message);
    public void Warning(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        var safeMessage = message.Replace("\r", " ").Replace("\n", " ");
        var line = $"{DateTimeOffset.Now:O} [{level}] {safeMessage}{Environment.NewLine}";
        var path = Path.Combine(_directory, $"zyron-print-{DateTime.Now:yyyyMMdd}.log");
        lock (_gate)
        {
            File.AppendAllText(path, line);
        }
    }

    private void DeleteOldLogs()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "zyron-print-*.log"))
            {
                if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Falhas de limpeza não impedem o aplicativo.
        }
    }
}

