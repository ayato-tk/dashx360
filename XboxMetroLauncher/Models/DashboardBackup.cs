using System.Reflection;

namespace XboxMetroLauncher.Models;

public sealed class DashboardBackup
{
    public string ExportVersion { get; set; } = "1";
    public string AppVersion { get; set; } = ResolveAppVersion();
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
    public DashboardBackupSettings Settings { get; set; } = new();
    public DashboardBackupProfile Profile { get; set; } = new();
    public GameLibrary Library { get; set; } = new();
    public List<DashboardBackupTheme> CustomThemes { get; set; } = [];

    private static string ResolveAppVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "v1.0.0-public";
}

public sealed class DashboardBackupSettings
{
    public bool StartFullscreen { get; set; }
    public bool PlayUiSounds { get; set; }
    public bool EnableControllerInput { get; set; }
    public bool LaunchOnWindowsStartup { get; set; }
    public bool MinimizeOnGameLaunch { get; set; } = true;
    public string ThemeName { get; set; } = string.Empty;
    public string BingSearchBaseUrl { get; set; } = string.Empty;
    public string DisplayResolution { get; set; } = "1080p";
    public string OpenTrayGameId { get; set; } = string.Empty;
    public string GameCoverFitMode { get; set; } = "Auto";
    public string DefaultAddDestination { get; set; } = "My Games";
}

public sealed class DashboardBackupProfile
{
    public string Gamertag { get; set; } = string.Empty;
    public string GamerPicturePath { get; set; } = string.Empty;
    public int Gamerscore { get; set; }
    public string OnlineStatus { get; set; } = string.Empty;
    public string Motto { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string GamerPictureFileName { get; set; } = string.Empty;
    public string GamerPictureBase64 { get; set; } = string.Empty;
}

public sealed class DashboardBackupTheme
{
    public string Name { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string HomeImageFileName { get; set; } = "home.png";
    public string HomeImageBase64 { get; set; } = string.Empty;
    public string GamesImageFileName { get; set; } = "games.png";
    public string GamesImageBase64 { get; set; } = string.Empty;
    public string SettingsImageFileName { get; set; } = "settings.png";
    public string SettingsImageBase64 { get; set; } = string.Empty;
    public string AppsImageFileName { get; set; } = "apps.png";
    public string AppsImageBase64 { get; set; } = string.Empty;
}

public sealed class DashboardImportResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? SafetyBackupPath { get; init; }
}
