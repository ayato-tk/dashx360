using System.Diagnostics;
using System.IO;
using System.Text;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class RunningGameService : IRunningGameService
{
    private static readonly string DebugLogPath = Path.Combine(AppPaths.LogsFolder, "running-game-debug.log");

    private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam",
        "gameoverlayui",
        "steamwebhelper",
        "explorer",
        "xboxmetrolauncher",
        "steamservice",
        "steamerrorreporter",
        "crashhandler",
        "crashpad_handler",
        "conhost",
        "cmd",
        "rundll32"
    };

    private readonly object _syncRoot = new();
    private Process? _process;
    private GameMetadata? _game;
    private DateTimeOffset _launchedAt = DateTimeOffset.MinValue;
    private DateTimeOffset? _localPlaytimeStartedAt;
    private bool _hasPlaytimeUpdate;
    private RunningGameState _state = RunningGameState.None;

    public event EventHandler? StateChanged;

    public bool HasRunningGame
    {
        get
        {
            lock (_syncRoot)
            {
                return _game is not null;
            }
        }
    }

    public bool HasTrackedProcess => GetTrackedProcess() is not null;

    public string RunningGameTitle
    {
        get
        {
            lock (_syncRoot)
            {
                return _game?.Title ?? string.Empty;
            }
        }
    }

    public RunningGameState State
    {
        get
        {
            lock (_syncRoot)
            {
                if (_state == RunningGameState.Tracked && GetTrackedProcess() is null)
                {
                    _state = RunningGameState.ProcessNotDetected;
                }

                return _state;
            }
        }
    }

    public GameMetadata? CurrentGame
    {
        get
        {
            lock (_syncRoot)
            {
                return _game;
            }
        }
    }

    public bool ConsumePlaytimeUpdate()
    {
        lock (_syncRoot)
        {
            if (!_hasPlaytimeUpdate)
            {
                return false;
            }

            _hasPlaytimeUpdate = false;
            return true;
        }
    }

    public void BeginLaunch(GameMetadata game, DateTimeOffset launchedAt)
    {
        ClearInternal();

        lock (_syncRoot)
        {
            _game = game;
            _launchedAt = launchedAt;
            _state = RunningGameState.Launching;
        }

        Log($"launch started | title={game.Title} | launchType={game.LaunchType} | launchTime={launchedAt:O} | steamAppId={game.SteamAppId} | exePath={game.ExecutablePath} | installPath={game.InstallPath}");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Track(GameMetadata game, Process? process)
    {
        lock (_syncRoot)
        {
            _game = game;
        }

        if (process is null || IsIgnoredProcess(process))
        {
            lock (_syncRoot)
            {
                _state = _game is null ? RunningGameState.None : RunningGameState.ProcessNotDetected;
            }

            Log($"process track result | title={game.Title} | matchedProcess=<none>");
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        AttachProcess(process, RunningGameState.Tracked);
        Log($"process track result | title={game.Title} | matchedProcess={process.ProcessName} | pid={process.Id} | path={TryGetProcessPath(process)}");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        ClearInternal();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<RunningGameCloseResult> CloseAsync(bool forceKill, CancellationToken cancellationToken = default)
    {
        GameMetadata? game;
        RunningGameState state;

        lock (_syncRoot)
        {
            game = _game;
            state = _state;
        }

        if (game is null)
        {
            return new RunningGameCloseResult
            {
                Success = false,
                RequiresForceConfirmation = false,
                Message = "No game running."
            };
        }

        var process = GetTrackedProcess();
        if (process is null)
        {
            var allowWait = state == RunningGameState.Launching
                || DateTimeOffset.UtcNow - _launchedAt < TimeSpan.FromSeconds(12);
            var resolved = await TryResolveTrackedProcessAsync(game, allowWait, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                var message = allowWait ? "Finding game process..." : "Game running, but process not detected";
                Log($"close failed | title={game.Title} | reason={message}");
                return new RunningGameCloseResult
                {
                    Success = false,
                    RequiresForceConfirmation = false,
                    Message = message
                };
            }

            process = resolved;
            AttachProcess(process, RunningGameState.Tracked);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        if (IsIgnoredProcess(process))
        {
            Log($"close blocked | title={game.Title} | reason=ignored process {process.ProcessName}");
            ClearInternal();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new RunningGameCloseResult
            {
                Success = false,
                RequiresForceConfirmation = false,
                Message = "Game running, but process not detected"
            };
        }

        try
        {
            var title = game.Title;
            var closeRequested = process.CloseMainWindow();
            Log($"close attempt | title={title} | pid={process.Id} | process={process.ProcessName} | closeMainWindow={closeRequested}");

            if (closeRequested && await WaitForExitAsync(process, TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false))
            {
                Log($"close success | title={title} | pid={process.Id}");
                ClearInternal();
                StateChanged?.Invoke(this, EventArgs.Empty);
                return new RunningGameCloseResult
                {
                    Success = true,
                    RequiresForceConfirmation = false,
                    Message = $"{title} closed."
                };
            }

            if (!forceKill)
            {
                Log($"close needs force | title={title} | pid={process.Id}");
                return new RunningGameCloseResult
                {
                    Success = false,
                    RequiresForceConfirmation = true,
                    Message = $"{title} did not close. Press X again to force close."
                };
            }

            process.Kill(entireProcessTree: true);
            await WaitForExitAsync(process, TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            Log($"force close success | title={title} | pid={process.Id}");
            ClearInternal();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new RunningGameCloseResult
            {
                Success = true,
                RequiresForceConfirmation = false,
                Message = $"{title} closed."
            };
        }
        catch (Exception ex)
        {
            if (process.HasExited)
            {
                Log($"close success after exception | title={game.Title} | reason={ex.Message}");
                ClearInternal();
                StateChanged?.Invoke(this, EventArgs.Empty);
                return new RunningGameCloseResult
                {
                    Success = true,
                    RequiresForceConfirmation = false,
                    Message = "Game closed."
                };
            }

            Log($"close failed | title={game.Title} | reason={ex.Message}");
            return new RunningGameCloseResult
            {
                Success = false,
                RequiresForceConfirmation = false,
                Message = "Unable to close the running game."
            };
        }
    }

    private Process? GetTrackedProcess()
    {
        lock (_syncRoot)
        {
            if (_process is null)
            {
                return null;
            }

            try
            {
                if (_process.HasExited)
                {
                    DetachTrackedProcess();
                    _state = _game is null ? RunningGameState.None : RunningGameState.ProcessNotDetected;
                    return null;
                }

                return _process;
            }
            catch
            {
                DetachTrackedProcess();
                _state = _game is null ? RunningGameState.None : RunningGameState.ProcessNotDetected;
                return null;
            }
        }
    }

    private void AttachProcess(Process process, RunningGameState state)
    {
        lock (_syncRoot)
        {
            DetachTrackedProcess();
            _process = process;
            _state = state;
            _localPlaytimeStartedAt = ShouldTrackLocalPlaytime(_game) ? _launchedAt : null;
        }

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += Process_OnExited;
        }
        catch
        {
        }
    }

    private async Task<Process?> TryResolveTrackedProcessAsync(GameMetadata game, bool allowWait, CancellationToken cancellationToken)
    {
        var rounds = allowWait ? 8 : 2;
        var delay = TimeSpan.FromMilliseconds(500);
        Process? bestCandidate = null;
        var bestScore = int.MinValue;

        for (var round = 0; round < rounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = Process.GetProcesses();
            try
            {
                var foregroundProcessId = TryGetForegroundProcessId();
                foreach (var process in snapshot)
                {
                    try
                    {
                        if (process.HasExited || IsIgnoredProcess(process))
                        {
                            continue;
                        }

                        var processPath = TryGetProcessPath(process);
                        var score = ScoreCandidate(game, process, processPath, foregroundProcessId);
                        if (score <= 0)
                        {
                            continue;
                        }

                        Log($"process match attempt | title={game.Title} | candidate={process.ProcessName} | pid={process.Id} | score={score} | path={processPath}");
                        if (score > bestScore)
                        {
                            bestCandidate?.Dispose();
                            bestCandidate = Process.GetProcessById(process.Id);
                            bestScore = score;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                foreach (var process in snapshot)
                {
                    process.Dispose();
                }
            }

            if (bestCandidate is not null && bestScore >= 120)
            {
                return bestCandidate;
            }

            if (round < rounds - 1)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        return bestCandidate;
    }

    private int ScoreCandidate(GameMetadata game, Process process, string? processPath, int foregroundProcessId)
    {
        if (!StartedAfterLaunch(process))
        {
            return 0;
        }

        var score = 0;
        var installPath = NormalizeFolderPath(game.InstallPath);
        var executablePath = NormalizeFilePath(game.ExecutablePath);

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(executablePath)
            && string.Equals(NormalizeFilePath(processPath), executablePath, StringComparison.OrdinalIgnoreCase))
        {
            score += 180;
        }

        if (!string.IsNullOrWhiteSpace(installPath)
            && !string.IsNullOrWhiteSpace(processPath)
            && NormalizeFilePath(processPath)?.StartsWith(installPath, StringComparison.OrdinalIgnoreCase) == true)
        {
            score += 130;
        }

        if (process.Id == foregroundProcessId)
        {
            score += 120;
        }

        try
        {
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                score += 60;
            }
        }
        catch
        {
        }

        try
        {
            if (process.Responding)
            {
                score += 15;
            }
        }
        catch
        {
        }

        return score;
    }

    private bool StartedAfterLaunch(Process process)
    {
        try
        {
            var startTime = process.StartTime.ToUniversalTime();
            return startTime >= _launchedAt.UtcDateTime.AddSeconds(-2);
        }
        catch
        {
            return false;
        }
    }

    private void Process_OnExited(object? sender, EventArgs e)
    {
        lock (_syncRoot)
        {
            AddLocalPlaytimeIfNeeded(DateTimeOffset.UtcNow);
            DetachTrackedProcess();
            _game = null;
            _state = RunningGameState.None;
            _launchedAt = DateTimeOffset.MinValue;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearInternal()
    {
        lock (_syncRoot)
        {
            AddLocalPlaytimeIfNeeded(DateTimeOffset.UtcNow);
            DetachTrackedProcess();
            _game = null;
            _state = RunningGameState.None;
            _launchedAt = DateTimeOffset.MinValue;
        }
    }

    private void DetachTrackedProcess()
    {
        var process = _process;
        _process = null;

        if (process is null)
        {
            return;
        }

        try
        {
            process.Exited -= Process_OnExited;
        }
        catch
        {
        }

        process.Dispose();
    }

    private void AddLocalPlaytimeIfNeeded(DateTimeOffset endedAt)
    {
        if (!ShouldTrackLocalPlaytime(_game) || _localPlaytimeStartedAt is not { } startedAt)
        {
            _localPlaytimeStartedAt = null;
            return;
        }

        var elapsed = endedAt - startedAt;
        _localPlaytimeStartedAt = null;
        if (elapsed <= TimeSpan.FromSeconds(1))
        {
            return;
        }

        _game!.Playtime += elapsed;
        if (_game.Playtime < TimeSpan.Zero)
        {
            _game.Playtime = TimeSpan.Zero;
        }

        _hasPlaytimeUpdate = true;
        Log($"local playtime updated | title={_game.Title} | added={elapsed} | total={_game.Playtime}");
    }

    private static bool ShouldTrackLocalPlaytime(GameMetadata? game)
        => game is not null
           && !string.Equals(game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase);

    private static bool IsIgnoredProcess(Process process)
    {
        try
        {
            return IgnoredProcessNames.Contains(process.ProcessName);
        }
        catch
        {
            return true;
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
    }

    private static int TryGetForegroundProcessId()
    {
        try
        {
            var handle = NativeMethods.GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return -1;
            }

            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            return unchecked((int)processId);
        }
        catch
        {
            return -1;
        }
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DebugLogPath)!);
            File.AppendAllText(DebugLogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
