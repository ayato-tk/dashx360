using System.Diagnostics;

namespace XboxMetroLauncher.Services;

public sealed class GameLaunchResult
{
	public Process? TrackedProcess { get; init; }

	public bool IsTracked => TrackedProcess != null;

	public static GameLaunchResult Untracked()
	{
		return new GameLaunchResult();
	}
}
