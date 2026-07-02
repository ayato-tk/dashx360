using System.Diagnostics;

namespace XboxMetroLauncher.Services;

public sealed class GameLaunchResult
{
    public static GameLaunchResult Untracked() => new();

    public Process? TrackedProcess { get; init; }

    public bool IsTracked => TrackedProcess is not null;
}
