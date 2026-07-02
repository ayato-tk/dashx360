namespace XboxMetroLauncher.Models;

public sealed class SteamAchievementItem
{
    public string ApiName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool Achieved { get; set; }

    public long UnlockTimeUnix { get; set; }
}
