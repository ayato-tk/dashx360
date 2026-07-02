using System.Diagnostics;
using System.IO;

namespace XboxMetroLauncher.Utilities;

internal static class AppPaths
{
    private static readonly string[] LegacyUserDataFolders =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XboxMetroLauncher"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XboxMetroLauncher"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XboxMetroLauncherData")
    ];

    public static string AppFolder
    {
        get
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                try
                {
                    processPath = Process.GetCurrentProcess().MainModule?.FileName;
                }
                catch
                {
                    processPath = null;
                }
            }

            var folder = string.IsNullOrWhiteSpace(processPath)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(processPath);

            return Path.TrimEndingDirectorySeparator(string.IsNullOrWhiteSpace(folder)
                ? AppContext.BaseDirectory
                : folder);
        }
    }

    public static string UserDataFolder => EnsureFolder(Path.Combine(AppFolder, "UserData"));

    public static string LogsFolder => EnsureFolder(Path.Combine(AppFolder, "Logs"));

    public static IReadOnlyList<string> LegacyDataRoots()
        => LegacyUserDataFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> CandidateRoots()
    {
        var roots = new List<string>();
        AddRoot(roots, AppFolder);
        AddRoot(roots, Environment.CurrentDirectory);
        AddRoot(roots, AppContext.BaseDirectory);
        return roots;
    }

    public static string FindFolder(string relativePath, Func<string, bool>? predicate = null)
    {
        foreach (var root in CandidateRoots())
        {
            var candidate = Path.Combine(root, relativePath);
            if (Directory.Exists(candidate) && (predicate is null || predicate(candidate)))
            {
                return candidate;
            }
        }

        return Path.Combine(AppFolder, relativePath);
    }

    public static string FindFile(string relativePath)
    {
        foreach (var root in CandidateRoots())
        {
            var candidate = Path.Combine(root, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(AppFolder, relativePath);
    }

    public static string ResolvePath(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(AppFolder, path);

    private static void AddRoot(List<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!roots.Any(root => string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            roots.Add(fullPath);
        }
    }

    private static string EnsureFolder(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
