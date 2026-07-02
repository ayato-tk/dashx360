using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class GameLaunchService : IGameLaunchService
{
    private static readonly string DebugLogPath = Path.Combine(AppPaths.LogsFolder, "running-game-debug.log");

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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

    public async Task<GameLaunchResult> LaunchAsync(GameMetadata game, CancellationToken cancellationToken = default)
    {
        if (string.Equals(game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(game.LaunchCommand))
            {
                throw new InvalidOperationException($"'{game.Title}' does not have a Steam launch command.");
            }

            var installPath = NormalizeFolderPath(game.InstallPath);
            var knownProcessIds = CaptureProcessIds();
            Log($"steam launch request | title={game.Title} | steamAppId={game.SteamAppId} | installPath={game.InstallPath} | exePath={game.ExecutablePath}");

            Process.Start(new ProcessStartInfo
            {
                FileName = game.LaunchCommand,
                UseShellExecute = true
            });

            var trackedProcess = await TryFindSteamGameProcessAsync(installPath, knownProcessIds, cancellationToken).ConfigureAwait(false);
            Log(trackedProcess is null
                ? $"steam launch unresolved | title={game.Title}"
                : $"steam launch matched | title={game.Title} | pid={trackedProcess.Id} | process={trackedProcess.ProcessName} | path={TryGetProcessPath(trackedProcess)}");
            return new GameLaunchResult { TrackedProcess = trackedProcess };
        }

        if (string.IsNullOrWhiteSpace(game.ExecutablePath))
        {
            throw new InvalidOperationException($"'{game.Title}' does not have an executable path.");
        }

        var workingDirectory = string.IsNullOrWhiteSpace(game.WorkingDirectory)
            ? Path.GetDirectoryName(game.ExecutablePath)
            : game.WorkingDirectory;

        var startInfo = new ProcessStartInfo
        {
            FileName = game.ExecutablePath,
            Arguments = game.Arguments,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = true
        };

        var process = Process.Start(startInfo);
        Log($"exe launch request | title={game.Title} | pid={process?.Id} | exePath={game.ExecutablePath}");
        return new GameLaunchResult { TrackedProcess = process };
    }

    private static HashSet<int> CaptureProcessIds()
    {
        var knownProcessIds = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                knownProcessIds.Add(process.Id);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return knownProcessIds;
    }

    private static async Task<Process?> TryFindSteamGameProcessAsync(
        string? installPath,
        HashSet<int> knownProcessIds,
        CancellationToken cancellationToken)
    {
        Process? bestCandidate = null;
        var bestScore = int.MinValue;
        var stableBestProcessId = -1;
        var stableBestCount = 0;

        for (var attempt = 0; attempt < 24; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var foregroundProcessId = TryGetForegroundProcessId();
            var candidates = new List<(Process Process, int Score)>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.HasExited || knownProcessIds.Contains(process.Id) || IsIgnoredProcess(process))
                    {
                        process.Dispose();
                        continue;
                    }

                    var processPath = TryGetProcessPath(process);
                    var score = ScoreCandidate(process, processPath, installPath, foregroundProcessId);
                    if (score <= 0)
                    {
                        process.Dispose();
                        continue;
                    }

                    Log($"steam match attempt | candidate={process.ProcessName} | pid={process.Id} | score={score} | path={processPath}");
                    candidates.Add((process, score));
                }
                catch
                {
                    process.Dispose();
                }
            }

            var currentBest = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => GetProcessStartTicks(candidate.Process))
                .FirstOrDefault();

            foreach (var candidate in candidates)
            {
                if (ReferenceEquals(candidate.Process, currentBest.Process))
                {
                    continue;
                }

                candidate.Process.Dispose();
            }

            if (currentBest.Process is not null)
            {
                if (currentBest.Process.Id == stableBestProcessId)
                {
                    stableBestCount++;
                }
                else
                {
                    stableBestProcessId = currentBest.Process.Id;
                    stableBestCount = 1;
                }

                if (currentBest.Score > bestScore)
                {
                    bestCandidate?.Dispose();
                    bestCandidate = currentBest.Process;
                    bestScore = currentBest.Score;
                }
                else if (!ReferenceEquals(bestCandidate, currentBest.Process))
                {
                    currentBest.Process.Dispose();
                }

                if (currentBest.Score >= 120 || stableBestCount >= 2 && currentBest.Score >= 80)
                {
                    return bestCandidate;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken).ConfigureAwait(false);
        }

        return bestCandidate;
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

    private static int ScoreCandidate(Process process, string? processPath, string? installPath, int foregroundProcessId)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(installPath)
            && !string.IsNullOrWhiteSpace(processPath)
            && processPath.StartsWith(installPath, StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        if (process.Id == foregroundProcessId)
        {
            score += 120;
        }

        try
        {
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                score += 70;
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

        try
        {
            var startedRecently = DateTime.Now - process.StartTime.ToLocalTime() < TimeSpan.FromSeconds(15);
            if (startedRecently)
            {
                score += 15;
            }
        }
        catch
        {
        }

        return score;
    }

    private static int TryGetForegroundProcessId()
    {
        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return -1;
            }

            GetWindowThreadProcessId(handle, out var processId);
            return unchecked((int)processId);
        }
        catch
        {
            return -1;
        }
    }

    private static long GetProcessStartTicks(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch
        {
            return 0;
        }
    }

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
}
