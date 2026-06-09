namespace XboxMetroLauncher.Models;

public sealed class DashboardThemeManifest
{
	public string Name { get; set; } = string.Empty;

	public string HomeImage { get; set; } = "home.png";

	public string GamesImage { get; set; } = "games.png";

	public string SettingsImage { get; set; } = "settings.png";

	public string AppsImage { get; set; } = "apps.png";
}
