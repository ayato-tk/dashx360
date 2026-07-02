namespace XboxMetroLauncher.Models;

public sealed class DashboardTheme
{
    public const string BuiltInThemeName = "Xbox 360";

    public string Name { get; set; } = BuiltInThemeName;
    public string FolderPath { get; set; } = string.Empty;
    public string HomeBackgroundPath { get; set; } = string.Empty;
    public string GamesBackgroundPath { get; set; } = string.Empty;
    public string SettingsBackgroundPath { get; set; } = string.Empty;
    public string AppsBackgroundPath { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }

    public string GetBackgroundPath(string sectionKey)
        => sectionKey switch
        {
            "home" => HomeBackgroundPath,
            "games" => GamesBackgroundPath,
            "settings" => SettingsBackgroundPath,
            "apps" => AppsBackgroundPath,
            _ => string.Empty
        };
}
