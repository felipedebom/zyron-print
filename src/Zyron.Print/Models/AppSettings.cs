namespace Zyron.Print.Models;

public sealed class AppSettings
{
    public string PrinterName { get; set; } = "";
    public int PaperWidth { get; set; } = 58;
    public bool CutPaper { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public string SupabaseUrl { get; set; } = "";
    public string SupabaseAnonKey { get; set; } = "";
    public int PollIntervalSeconds { get; set; } = 5;
}

