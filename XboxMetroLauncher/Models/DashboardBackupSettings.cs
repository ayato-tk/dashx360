namespace XboxMetroLauncher.Models;

public sealed class DashboardBackupSettings
{
	public bool StartFullscreen { get; set; }

	public bool PlayUiSounds { get; set; }

	public bool EnableControllerInput { get; set; }

	public bool LaunchOnWindowsStartup { get; set; }

	public string ThemeName { get; set; } = string.Empty;

	public string BingSearchBaseUrl { get; set; } = string.Empty;

	public string DisplayResolution { get; set; } = "1080p";

	public string OpenTrayGameId { get; set; } = string.Empty;

	public string GameCoverFitMode { get; set; } = "Auto";

	public string DefaultAddDestination { get; set; } = "My Games";
}
