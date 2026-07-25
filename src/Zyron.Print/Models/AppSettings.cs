namespace Zyron.Print.Models;

public sealed class AppSettings
{
    public const string DefaultSupabaseUrl = "https://tnyspulodawclzbduonv.supabase.co";
    public const string DefaultSupabasePublishableKey = "sb_publishable_Ll4cYmlQihXl3Hbyyl-RhA_LcMcCN7b";

    public string PrinterName { get; set; } = "";
    public int PaperWidth { get; set; } = 58;
    public bool CutPaper { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public string SupabaseUrl { get; set; } = DefaultSupabaseUrl;
    public string SupabaseAnonKey { get; set; } = DefaultSupabasePublishableKey;
    public int PollIntervalSeconds { get; set; } = 5;
}
