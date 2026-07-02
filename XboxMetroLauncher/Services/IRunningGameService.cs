using System.Diagnostics;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface IRunningGameService
{
    event EventHandler? StateChanged;

    bool HasRunningGame { get; }

    bool HasTrackedProcess { get; }

    string RunningGameTitle { get; }

    RunningGameState State { get; }

    GameMetadata? CurrentGame { get; }

    bool ConsumePlaytimeUpdate();

    void BeginLaunch(GameMetadata game, DateTimeOffset launchedAt);

    void Track(GameMetadata game, Process? process);

    void Clear();

    Task<RunningGameCloseResult> CloseAsync(bool forceKill, CancellationToken cancellationToken = default);
}
