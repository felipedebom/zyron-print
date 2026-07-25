namespace Zyron.Print.Configuration;

public sealed record AppPaths(string DataDirectory, string LogDirectory, string SettingsFile, string CredentialFile)
{
    public static AppPaths Create()
    {
        var data = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZYRON",
            "Print");
        var logs = Path.Combine(data, "logs");
        Directory.CreateDirectory(logs);
        return new AppPaths(
            data,
            logs,
            Path.Combine(data, "settings.json"),
            Path.Combine(data, "device.dat"));
    }
}

