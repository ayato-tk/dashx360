namespace XboxMetroLauncher.Models;

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
