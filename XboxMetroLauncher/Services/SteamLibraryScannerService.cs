using System.IO;
using System.Net.Http;
using System.Text;
using Microsoft.Win32;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class SteamLibraryScannerService : ISteamLibraryScannerService
{
    private static readonly HttpClient HttpClient = new();
    private static readonly string SteamArtworkDebugLogPath = Path.Combine(AppPaths.LogsFolder, "steam-art-debug.log");
    private static readonly string[] FallbackSteamPaths =
    [
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam"
    ];

    public async Task<SteamGameScanResult> ScanAsync(GameLibrary library, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var steamPath = FindSteamPath();
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
        {
            return new SteamGameScanResult
            {
                Message = "Steam does not appear to be installed."
            };
        }

        var libraryPaths = GetSteamLibraryPaths(steamPath);
        var manifests = libraryPaths
            .Select(path => Path.Combine(path, "steamapps"))
            .Where(Directory.Exists)
            .SelectMany(steamAppsPath => SafeEnumerateFiles(steamAppsPath, "appmanifest_*.acf"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (manifests.Count == 0)
        {
            return new SteamGameScanResult
            {
                Message = "No installed Steam games were found."
            };
        }

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var debugReport = new StringBuilder();
        debugReport.AppendLine($"[STEAM ART] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        debugReport.AppendLine($"steam path: {steamPath}");

        foreach (var manifestPath in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SteamManifestEntry? entry;
            try
            {
                entry = ParseManifest(manifestPath);
            }
            catch
            {
                skipped++;
                continue;
            }

            if (entry is null
                || string.IsNullOrWhiteSpace(entry.AppId)
                || string.IsNullOrWhiteSpace(entry.Name)
                || string.IsNullOrWhiteSpace(entry.InstallDir))
            {
                skipped++;
                continue;
            }

            var installPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "common", entry.InstallDir);
            var artwork = await ResolveArtworkAsync(steamPath, entry.AppId, cancellationToken);
            var existingSteam = library.Games.FirstOrDefault(game =>
                string.Equals(game.SteamAppId, entry.AppId, StringComparison.OrdinalIgnoreCase));

            if (existingSteam is not null)
            {
                existingSteam.Title = entry.Name;
                existingSteam.Platform = "Steam";
                existingSteam.Genre = "Imported";
                existingSteam.SteamAppId = entry.AppId;
                existingSteam.InstallPath = installPath;
                existingSteam.LaunchType = "Steam";
                existingSteam.LaunchCommand = $"steam://rungameid/{entry.AppId}";
                existingSteam.WorkingDirectory = installPath;
                ApplySteamArtwork(existingSteam, artwork);
                AppendArtworkDebug(debugReport, entry.AppId, entry.Name, artwork, existingSteam.CoverArtPath);
                updated++;
                continue;
            }

            var conflictingManualGame = library.Games.FirstOrDefault(game =>
                !string.Equals(game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(game.Title)
                && string.Equals(game.Title, entry.Name, StringComparison.OrdinalIgnoreCase));

            if (conflictingManualGame is not null)
            {
                skipped++;
                continue;
            }

            var importedGame = new GameMetadata
            {
                Title = entry.Name,
                Platform = "Steam",
                Genre = "Imported",
                LaunchType = "Steam",
                SteamAppId = entry.AppId,
                InstallPath = installPath,
                LaunchCommand = $"steam://rungameid/{entry.AppId}",
                WorkingDirectory = installPath
            };
            ApplySteamArtwork(importedGame, artwork);
            library.Games.Add(importedGame);
            AppendArtworkDebug(debugReport, entry.AppId, entry.Name, artwork, importedGame.CoverArtPath);
            added++;
        }

        WriteArtworkDebugReport(debugReport);

        var message = added == 0 && updated == 0
            ? "No Steam games were imported."
            : $"Steam scan complete. Added: {added}, Updated: {updated}, Skipped: {skipped}.";

        return new SteamGameScanResult
        {
            Added = added,
            Updated = updated,
            Skipped = skipped,
            Message = message
        };
    }

    private static string? FindSteamPath()
    {
        var registryPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrWhiteSpace(registryPath) && Directory.Exists(registryPath))
        {
            return NormalizeDirectoryPath(registryPath);
        }

        return FallbackSteamPaths.FirstOrDefault(Directory.Exists);
    }

    private static IReadOnlyList<string> GetSteamLibraryPaths(string steamPath)
    {
        var libraryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeDirectoryPath(steamPath)
        };

        var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
        {
            return libraryPaths.ToList();
        }

        foreach (var line in File.ReadLines(libraryFoldersPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.Contains("\"path\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tokens = ExtractQuotedTokens(trimmed);
            if (tokens.Count < 2)
            {
                continue;
            }

            var candidate = tokens[^1].Replace(@"\\", @"\");
            if (Directory.Exists(candidate))
            {
                libraryPaths.Add(NormalizeDirectoryPath(candidate));
            }
        }

        return libraryPaths.ToList();
    }

    private static SteamManifestEntry? ParseManifest(string manifestPath)
    {
        string? appId = null;
        string? name = null;
        string? installDir = null;

        foreach (var line in File.ReadLines(manifestPath))
        {
            var tokens = ExtractQuotedTokens(line);
            if (tokens.Count < 2)
            {
                continue;
            }

            var key = tokens[0];
            var value = tokens[1];
            if (key.Equals("appid", StringComparison.OrdinalIgnoreCase))
            {
                appId = value;
            }
            else if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                name = value;
            }
            else if (key.Equals("installdir", StringComparison.OrdinalIgnoreCase))
            {
                installDir = value;
            }
        }

        return appId is null && name is null && installDir is null
            ? null
            : new SteamManifestEntry(appId ?? string.Empty, name ?? string.Empty, installDir ?? string.Empty);
    }

    private static List<string> ExtractQuotedTokens(string line)
    {
        var tokens = new List<string>();
        var inQuote = false;
        var start = 0;

        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != '"')
            {
                continue;
            }

            if (!inQuote)
            {
                inQuote = true;
                start = index + 1;
            }
            else
            {
                tokens.Add(line[start..index]);
                inQuote = false;
            }
        }

        return tokens;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string folderPath, string searchPattern)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath, searchPattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeDirectoryPath(string path)
        => Path.GetFullPath(path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static async Task<SteamArtworkResult> ResolveArtworkAsync(string steamPath, string appId, CancellationToken cancellationToken)
    {
        var localCacheRoot = Path.Combine(steamPath, "appcache", "librarycache");
        var appCacheRoot = Path.Combine(AppPaths.AppFolder, "Assets", "GameArt", "Steam", appId);
        Directory.CreateDirectory(appCacheRoot);

        var cover = await ResolveArtworkFileAsync(
            localCacheRoot,
            appCacheRoot,
            appId,
            "cover",
            [$"{appId}_library_600x900.jpg", $"{appId}_library_600x900.png"],
            [
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.png"
            ],
            cancellationToken);

        var header = await ResolveArtworkFileAsync(
            localCacheRoot,
            appCacheRoot,
            appId,
            "header",
            [$"{appId}_header.jpg", $"{appId}_header.png"],
            [$"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg"],
            cancellationToken);

        var hero = await ResolveArtworkFileAsync(
            localCacheRoot,
            appCacheRoot,
            appId,
            "hero",
            [$"{appId}_library_hero.jpg", $"{appId}_library_hero.png"],
            [$"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_hero.jpg"],
            cancellationToken);

        var logo = await ResolveArtworkFileAsync(
            localCacheRoot,
            appCacheRoot,
            appId,
            "logo",
            [$"{appId}_logo.png"],
            [$"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/logo.png"],
            cancellationToken);

        return new SteamArtworkResult(cover, header, hero, logo);
    }

    private static async Task<SteamArtworkFileResult> ResolveArtworkFileAsync(
        string localCacheRoot,
        string appCacheRoot,
        string appId,
        string destinationName,
        IReadOnlyList<string> localFileNames,
        IReadOnlyList<string> downloadUrls,
        CancellationToken cancellationToken)
    {
        string? localSourcePath = null;
        string? downloadedPath = null;

        if (Directory.Exists(localCacheRoot))
        {
            localSourcePath = localFileNames
                .Select(fileName => Path.Combine(localCacheRoot, fileName))
                .FirstOrDefault(IsValidArtworkFile);
        }

        if (localSourcePath is not null)
        {
            var localDestination = Path.Combine(appCacheRoot, $"{destinationName}{Path.GetExtension(localSourcePath)}");
            if (!IsValidArtworkFile(localDestination))
            {
                File.Copy(localSourcePath, localDestination, true);
            }

            return new SteamArtworkFileResult(
                MakePortableAssetPath(appId, Path.GetFileName(localDestination)),
                localSourcePath,
                null);
        }

        foreach (var downloadUrl in downloadUrls)
        {
            var extension = Path.GetExtension(new Uri(downloadUrl).AbsolutePath);
            var destinationPath = Path.Combine(appCacheRoot, $"{destinationName}{extension}");
            if (IsValidArtworkFile(destinationPath))
            {
                return new SteamArtworkFileResult(
                    MakePortableAssetPath(appId, Path.GetFileName(destinationPath)),
                    null,
                    null);
            }

            try
            {
                using var response = await HttpClient.GetAsync(downloadUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrWhiteSpace(contentType)
                    && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length < 128)
                {
                    continue;
                }

                await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);
                if (!IsValidArtworkFile(destinationPath))
                {
                    File.Delete(destinationPath);
                    continue;
                }

                downloadedPath = destinationPath;
                return new SteamArtworkFileResult(
                    MakePortableAssetPath(appId, Path.GetFileName(destinationPath)),
                    null,
                    downloadedPath);
            }
            catch
            {
            }
        }

        return new SteamArtworkFileResult(null, null, null);
    }

    private static void ApplySteamArtwork(GameMetadata game, SteamArtworkResult artwork)
    {
        var appId = string.IsNullOrWhiteSpace(game.SteamAppId) ? "unknown" : game.SteamAppId;
        if (ShouldAssignManagedSteamArtwork(game.CoverArtPath, appId))
        {
            game.CoverArtPath = artwork.Cover.FinalAssetPath ?? string.Empty;
        }

        if (ShouldAssignManagedSteamArtwork(game.HeaderImagePath, appId) && !string.IsNullOrWhiteSpace(artwork.Header.FinalAssetPath))
        {
            game.HeaderImagePath = artwork.Header.FinalAssetPath;
        }

        var backgroundPath = !string.IsNullOrWhiteSpace(artwork.Hero.FinalAssetPath)
            ? artwork.Hero.FinalAssetPath
            : artwork.Header.FinalAssetPath;
        if (ShouldAssignManagedSteamArtwork(game.BackgroundArtPath, appId) && !string.IsNullOrWhiteSpace(backgroundPath))
        {
            game.BackgroundArtPath = backgroundPath;
        }

        if (ShouldAssignManagedSteamArtwork(game.LogoImagePath, appId) && !string.IsNullOrWhiteSpace(artwork.Logo.FinalAssetPath))
        {
            game.LogoImagePath = artwork.Logo.FinalAssetPath;
        }
    }

    private static bool ShouldAssignManagedSteamArtwork(string existingPath, string appId)
    {
        if (string.IsNullOrWhiteSpace(existingPath))
        {
            return true;
        }

        var managedRelativePath = Path.Combine("Assets", "GameArt", "Steam", appId)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        if (!Path.IsPathRooted(existingPath))
        {
            var normalizedExisting = existingPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return normalizedExisting.StartsWith(managedRelativePath, StringComparison.OrdinalIgnoreCase);
        }

        var managedAbsolutePath = Path.Combine(AppPaths.AppFolder, managedRelativePath);
        return Path.GetFullPath(existingPath)
            .StartsWith(Path.GetFullPath(managedAbsolutePath), StringComparison.OrdinalIgnoreCase);
    }

    private static string MakePortableAssetPath(string appId, string fileName)
        => Path.Combine("Assets", "GameArt", "Steam", appId, fileName);

    private static bool IsValidArtworkFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            return new FileInfo(path).Length >= 128;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendArtworkDebug(
        StringBuilder builder,
        string appId,
        string title,
        SteamArtworkResult artwork,
        string finalCoverArtPath)
    {
        builder.AppendLine($"appid: {appId}");
        builder.AppendLine($"title: {title}");
        builder.AppendLine($"found local cover path: {artwork.Cover.LocalSourcePath ?? "<none>"}");
        builder.AppendLine($"downloaded cover path: {artwork.Cover.DownloadedPath ?? "<none>"}");
        builder.AppendLine($"final CoverImagePath value: {finalCoverArtPath}");
        builder.AppendLine();
    }

    private static void WriteArtworkDebugReport(StringBuilder builder)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SteamArtworkDebugLogPath)!);
            File.WriteAllText(SteamArtworkDebugLogPath, builder.ToString());
        }
        catch
        {
        }
    }

    private sealed record SteamManifestEntry(string AppId, string Name, string InstallDir);
    private sealed record SteamArtworkResult(
        SteamArtworkFileResult Cover,
        SteamArtworkFileResult Header,
        SteamArtworkFileResult Hero,
        SteamArtworkFileResult Logo);
    private sealed record SteamArtworkFileResult(string? FinalAssetPath, string? LocalSourcePath, string? DownloadedPath);
}
