namespace XboxMetroLauncher.Models;

public sealed class SteamGameDetails
{
    public TimeSpan? Playtime { get; init; }
    public string Genre { get; init; } = string.Empty;
    public string Rating { get; init; } = string.Empty;
    public string MultiplayerInfo { get; init; } = string.Empty;
    public string CoOpInfo { get; init; } = string.Empty;
    public string StoreScreenshotPath { get; init; } = string.Empty;
    public double ReviewStarRating { get; init; }
    public int ReviewCount { get; init; }
}
