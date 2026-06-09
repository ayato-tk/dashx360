namespace XboxMetroLauncher.Models;

public sealed class SteamGameScanResult
{
	public int Added { get; set; }

	public int Updated { get; set; }

	public int Skipped { get; set; }

	public string Message { get; set; } = string.Empty;
}
