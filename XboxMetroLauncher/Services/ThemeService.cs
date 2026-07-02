using System.Text.Json;
using System.Windows.Media.Imaging;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class ThemeService : IThemeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _themesRoot;

    public ThemeService()
    {
        _themesRoot = AppPaths.FindFolder(Path.Combine("Assets", "Custom Files", "Themes"));
        Directory.CreateDirectory(_themesRoot);
    }

    public async Task<IReadOnlyList<DashboardTheme>> LoadThemesAsync(CancellationToken cancellationToken = default)
    {
        var themes = new List<DashboardTheme>
        {
            new()
            {
                Name = DashboardTheme.BuiltInThemeName,
                IsBuiltIn = true
            }
        };

        foreach (var folder in Directory.EnumerateDirectories(_themesRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var theme = await LoadThemeAsync(folder, cancellationToken).ConfigureAwait(false);
                if (theme is not null)
                {
                    themes.Add(theme);
                }
            }
            catch
            {
            }
        }

        return themes
            .GroupBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(theme => theme.IsBuiltIn ? 0 : 1)
            .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<DashboardTheme> CreateThemeAsync(
        string themeName,
        string? homeImagePath,
        string? gamesImagePath,
        string? settingsImagePath,
        string? appsImagePath,
        CancellationToken cancellationToken = default)
    {
        var safeName = string.IsNullOrWhiteSpace(themeName) ? "Custom Theme" : themeName.Trim();
        var folderName = CreateSafeFolderName(safeName);
        var folderPath = Path.Combine(_themesRoot, folderName);
        Directory.CreateDirectory(folderPath);

        var manifest = new DashboardThemeManifest
        {
            Name = safeName
        };

        await SaveThemeImageAsync(homeImagePath, Path.Combine(folderPath, manifest.HomeImage), cancellationToken).ConfigureAwait(false);
        await SaveThemeImageAsync(gamesImagePath, Path.Combine(folderPath, manifest.GamesImage), cancellationToken).ConfigureAwait(false);
        await SaveThemeImageAsync(settingsImagePath, Path.Combine(folderPath, manifest.SettingsImage), cancellationToken).ConfigureAwait(false);
        await SaveThemeImageAsync(appsImagePath, Path.Combine(folderPath, manifest.AppsImage), cancellationToken).ConfigureAwait(false);

        await using (var stream = File.Create(Path.Combine(folderPath, "theme.json")))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        return new DashboardTheme
        {
            Name = safeName,
            FolderPath = folderPath,
            HomeBackgroundPath = GetThemeImagePath(folderPath, manifest.HomeImage),
            GamesBackgroundPath = GetThemeImagePath(folderPath, manifest.GamesImage),
            SettingsBackgroundPath = GetThemeImagePath(folderPath, manifest.SettingsImage),
            AppsBackgroundPath = GetThemeImagePath(folderPath, manifest.AppsImage)
        };
    }

    private static async Task<DashboardTheme?> LoadThemeAsync(string folderPath, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(folderPath, "theme.json");
        DashboardThemeManifest manifest;

        if (File.Exists(manifestPath))
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<DashboardThemeManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? new DashboardThemeManifest();
        }
        else
        {
            manifest = new DashboardThemeManifest
            {
                Name = Path.GetFileName(folderPath)
            };
        }

        var name = string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileName(folderPath) : manifest.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new DashboardTheme
        {
            Name = name,
            FolderPath = folderPath,
            HomeBackgroundPath = GetThemeImagePath(folderPath, manifest.HomeImage),
            GamesBackgroundPath = GetThemeImagePath(folderPath, manifest.GamesImage),
            SettingsBackgroundPath = GetThemeImagePath(folderPath, manifest.SettingsImage),
            AppsBackgroundPath = GetThemeImagePath(folderPath, manifest.AppsImage)
        };
    }

    private static string GetThemeImagePath(string folderPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var absolutePath = Path.Combine(folderPath, fileName);
        if (!File.Exists(absolutePath))
        {
            return string.Empty;
        }

        var relative = Path.GetRelativePath(AppPaths.AppFolder, absolutePath);
        return relative;
    }

    private static async Task SaveThemeImageAsync(string? sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            DeleteIfExists(destinationPath);
            return;
        }

        await Task.Run(() =>
        {
            using var stream = File.OpenRead(sourcePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            if (frame is null)
            {
                throw new InvalidOperationException("Theme image could not be loaded.");
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(frame);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var output = File.Create(destinationPath);
            encoder.Save(output);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string CreateSafeFolderName(string themeName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(themeName
            .Where(ch => !invalid.Contains(ch))
            .Select(ch => char.IsWhiteSpace(ch) ? '_' : ch)
            .ToArray())
            .Trim('_');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Custom_Theme";
        }

        return sanitized;
    }
}
