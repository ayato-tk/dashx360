namespace XboxMetroLauncher.Models;

public sealed class SteamConnectionTestResult
{
    public bool Success { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
