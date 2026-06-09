namespace XboxMetroLauncher.Models;

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
