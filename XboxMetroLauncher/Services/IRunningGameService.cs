using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface IRunningGameService
{
	bool HasRunningGame { get; }

	bool HasTrackedProcess { get; }

	string RunningGameTitle { get; }

	RunningGameState State { get; }

	event EventHandler? StateChanged;

	void BeginLaunch(GameMetadata game, DateTimeOffset launchedAt);

	void Track(GameMetadata game, Process? process);

	void Clear();

	Task<RunningGameCloseResult> CloseAsync(bool forceKill, CancellationToken cancellationToken = default(CancellationToken));
}
