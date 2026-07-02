using System.IO;
using System.Text.Json;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class ImportExportService : IImportExportService
{
    private const string BackupFilePrefix = "DashX360_Backup";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IGameLibraryService _libraryService;
    private readonly IProfileService _profileService;
    private readonly ISettingsService _settingsService;
    private readonly string _dataRoot;
    private readonly string _themesRoot;

    public ImportExportService(
        IGameLibraryService libraryService,
        IProfileService profileService,
        ISettingsService settingsService,
        string dataRoot)
    {
        _libraryService = libraryService;
        _profileService = profileService;
        _settingsService = settingsService;
        _dataRoot = dataRoot;
        _themesRoot = AppPaths.FindFolder(Path.Combine("Assets", "Custom Files", "Themes"));
        Directory.CreateDirectory(_themesRoot);
    }

    public async Task ExportAsync(GameLibrary library, Profile profile, AppSettings settings, string filePath, CancellationToken cancellationToken = default)
    {
        var backup = new DashboardBackup
        {
            Settings = BuildSettingsBackup(settings),
            Profile = await BuildProfileBackupAsync(profile, cancellationToken).ConfigureAwait(false),
            Library = CloneLibrary(library),
            CustomThemes = await BuildThemesBackupAsync(cancellationToken).ConfigureAwait(false)
        };

        await WriteBackupAsync(backup, filePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DashboardImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var backup = await ReadAndValidateBackupAsync(filePath, cancellationToken).ConfigureAwait(false);

            var currentLibrary = await _libraryService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var currentProfile = await _profileService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var currentSettings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);

            var safetyBackupPath = await CreateSafetyBackupAsync(currentLibrary, currentProfile, currentSettings, cancellationToken).ConfigureAwait(false);

            var updatedSettings = MergeSettings(currentSettings, backup.Settings);
            var updatedProfile = await MergeProfileAsync(currentProfile, backup.Profile, cancellationToken).ConfigureAwait(false);
            var updatedLibrary = NormalizeLibrary(backup.Library);
            await RestoreThemesAsync(backup.CustomThemes, cancellationToken).ConfigureAwait(false);

            await _settingsService.SaveAsync(updatedSettings, cancellationToken).ConfigureAwait(false);
            await _profileService.SaveAsync(updatedProfile, cancellationToken).ConfigureAwait(false);
            await _libraryService.SaveAsync(updatedLibrary, cancellationToken).ConfigureAwait(false);

            return new DashboardImportResult
            {
                Success = true,
                Message = "Dashboard data imported successfully.",
                SafetyBackupPath = safetyBackupPath
            };
        }
        catch (JsonException)
        {
            return new DashboardImportResult
            {
                Success = false,
                Message = "The selected backup file is not valid JSON."
            };
        }
        catch (InvalidDataException ex)
        {
            return new DashboardImportResult
            {
                Success = false,
                Message = ex.Message
            };
        }
        catch (Exception ex)
        {
            App.LogException(ex, "ImportExportService.ImportAsync");
            return new DashboardImportResult
            {
                Success = false,
                Message = $"Import failed: {ex.Message}"
            };
        }
    }

    private async Task<string> CreateSafetyBackupAsync(GameLibrary library, Profile profile, AppSettings settings, CancellationToken cancellationToken)
    {
        var backupFolder = Path.Combine(_dataRoot, "Backups");
        Directory.CreateDirectory(backupFolder);

        var fileName = $"{BackupFilePrefix}_PreImport_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var backupPath = Path.Combine(backupFolder, fileName);
        await ExportAsync(library, profile, settings, backupPath, cancellationToken).ConfigureAwait(false);
        return backupPath;
    }

    private static DashboardBackupSettings BuildSettingsBackup(AppSettings settings)
        => new()
        {
            StartFullscreen = settings.StartFullscreen,
            PlayUiSounds = settings.PlayUiSounds,
            EnableControllerInput = settings.EnableControllerInput,
            LaunchOnWindowsStartup = settings.LaunchOnWindowsStartup,
            MinimizeOnGameLaunch = settings.MinimizeOnGameLaunch,
            ThemeName = settings.ThemeName,
            BingSearchBaseUrl = settings.BingSearchBaseUrl,
            DisplayResolution = settings.DisplayResolution,
            OpenTrayGameId = settings.OpenTrayGameId,
            GameCoverFitMode = settings.GameCoverFitMode,
            DefaultAddDestination = settings.DefaultAddDestination
        };

    private static AppSettings MergeSettings(AppSettings current, DashboardBackupSettings imported)
    {
        current.StartFullscreen = imported.StartFullscreen;
        current.PlayUiSounds = imported.PlayUiSounds;
        current.EnableControllerInput = imported.EnableControllerInput;
        current.LaunchOnWindowsStartup = imported.LaunchOnWindowsStartup;
        current.MinimizeOnGameLaunch = imported.MinimizeOnGameLaunch;
        current.ThemeName = string.IsNullOrWhiteSpace(imported.ThemeName) ? current.ThemeName : imported.ThemeName;
        current.BingSearchBaseUrl = string.IsNullOrWhiteSpace(imported.BingSearchBaseUrl) ? current.BingSearchBaseUrl : imported.BingSearchBaseUrl;
        current.DisplayResolution = string.IsNullOrWhiteSpace(imported.DisplayResolution) ? current.DisplayResolution : imported.DisplayResolution;
        current.OpenTrayGameId = imported.OpenTrayGameId ?? string.Empty;
        current.GameCoverFitMode = string.IsNullOrWhiteSpace(imported.GameCoverFitMode) ? current.GameCoverFitMode : imported.GameCoverFitMode;
        current.DefaultAddDestination = string.IsNullOrWhiteSpace(imported.DefaultAddDestination) ? current.DefaultAddDestination : imported.DefaultAddDestination;
        return current;
    }

    private static async Task<DashboardBackupProfile> BuildProfileBackupAsync(Profile profile, CancellationToken cancellationToken)
    {
        var backup = new DashboardBackupProfile
        {
            Gamertag = profile.Gamertag,
            GamerPicturePath = profile.GamerPicturePath,
            Gamerscore = profile.Gamerscore,
            OnlineStatus = profile.OnlineStatus,
            Motto = profile.Motto,
            Description = profile.Description
        };

        if (!string.IsNullOrWhiteSpace(profile.GamerPicturePath) && File.Exists(profile.GamerPicturePath))
        {
            backup.GamerPictureFileName = Path.GetFileName(profile.GamerPicturePath);
            var bytes = await File.ReadAllBytesAsync(profile.GamerPicturePath, cancellationToken).ConfigureAwait(false);
            backup.GamerPictureBase64 = Convert.ToBase64String(bytes);
        }

        return backup;
    }

    private async Task<Profile> MergeProfileAsync(Profile current, DashboardBackupProfile imported, CancellationToken cancellationToken)
    {
        current.Gamertag = string.IsNullOrWhiteSpace(imported.Gamertag) ? current.Gamertag : imported.Gamertag;
        current.Gamerscore = imported.Gamerscore > 0 ? imported.Gamerscore : current.Gamerscore;
        current.OnlineStatus = string.IsNullOrWhiteSpace(imported.OnlineStatus) ? current.OnlineStatus : imported.OnlineStatus;
        current.Motto = imported.Motto ?? string.Empty;
        current.Description = imported.Description ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(imported.GamerPictureBase64))
        {
            current.GamerPicturePath = await RestoreProfilePictureAsync(imported, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(imported.GamerPicturePath) && File.Exists(imported.GamerPicturePath))
        {
            current.GamerPicturePath = imported.GamerPicturePath;
        }

        return current;
    }

    private async Task<string> RestoreProfilePictureAsync(DashboardBackupProfile imported, CancellationToken cancellationToken)
    {
        var profileFolder = Path.Combine(_dataRoot, "ImportedAssets", "Profile");
        Directory.CreateDirectory(profileFolder);

        var fileName = string.IsNullOrWhiteSpace(imported.GamerPictureFileName)
            ? "profile-import.png"
            : MakeSafeFileName(imported.GamerPictureFileName);
        var destination = Path.Combine(profileFolder, fileName);

        if (File.Exists(destination))
        {
            destination = Path.Combine(
                profileFolder,
                $"{Path.GetFileNameWithoutExtension(fileName)}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(fileName)}");
        }

        var bytes = Convert.FromBase64String(imported.GamerPictureBase64);
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
        return destination;
    }

    private async Task<List<DashboardBackupTheme>> BuildThemesBackupAsync(CancellationToken cancellationToken)
    {
        var themes = new List<DashboardBackupTheme>();

        foreach (var folderPath in Directory.EnumerateDirectories(_themesRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            DashboardThemeManifest manifest;
            var manifestPath = Path.Combine(folderPath, "theme.json");
            if (File.Exists(manifestPath))
            {
                await using var stream = File.OpenRead(manifestPath);
                manifest = await JsonSerializer.DeserializeAsync<DashboardThemeManifest>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
                    ?? new DashboardThemeManifest();
            }
            else
            {
                manifest = new DashboardThemeManifest
                {
                    Name = Path.GetFileName(folderPath)
                };
            }

            themes.Add(new DashboardBackupTheme
            {
                Name = string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileName(folderPath) : manifest.Name,
                FolderName = Path.GetFileName(folderPath),
                HomeImageFileName = string.IsNullOrWhiteSpace(manifest.HomeImage) ? "home.png" : manifest.HomeImage,
                HomeImageBase64 = await ReadThemeImageBase64Async(folderPath, manifest.HomeImage, cancellationToken).ConfigureAwait(false),
                GamesImageFileName = string.IsNullOrWhiteSpace(manifest.GamesImage) ? "games.png" : manifest.GamesImage,
                GamesImageBase64 = await ReadThemeImageBase64Async(folderPath, manifest.GamesImage, cancellationToken).ConfigureAwait(false),
                SettingsImageFileName = string.IsNullOrWhiteSpace(manifest.SettingsImage) ? "settings.png" : manifest.SettingsImage,
                SettingsImageBase64 = await ReadThemeImageBase64Async(folderPath, manifest.SettingsImage, cancellationToken).ConfigureAwait(false),
                AppsImageFileName = string.IsNullOrWhiteSpace(manifest.AppsImage) ? "apps.png" : manifest.AppsImage,
                AppsImageBase64 = await ReadThemeImageBase64Async(folderPath, manifest.AppsImage, cancellationToken).ConfigureAwait(false)
            });
        }

        return themes;
    }

    private async Task RestoreThemesAsync(IEnumerable<DashboardBackupTheme>? themes, CancellationToken cancellationToken)
    {
        if (themes is null)
        {
            return;
        }

        foreach (var theme in themes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folderName = string.IsNullOrWhiteSpace(theme.FolderName)
                ? MakeSafeFolderName(theme.Name)
                : MakeSafeFolderName(theme.FolderName);
            var folderPath = Path.Combine(_themesRoot, folderName);
            Directory.CreateDirectory(folderPath);

            var manifest = new DashboardThemeManifest
            {
                Name = string.IsNullOrWhiteSpace(theme.Name) ? folderName : theme.Name,
                HomeImage = string.IsNullOrWhiteSpace(theme.HomeImageFileName) ? "home.png" : MakeSafeFileName(theme.HomeImageFileName),
                GamesImage = string.IsNullOrWhiteSpace(theme.GamesImageFileName) ? "games.png" : MakeSafeFileName(theme.GamesImageFileName),
                SettingsImage = string.IsNullOrWhiteSpace(theme.SettingsImageFileName) ? "settings.png" : MakeSafeFileName(theme.SettingsImageFileName),
                AppsImage = string.IsNullOrWhiteSpace(theme.AppsImageFileName) ? "apps.png" : MakeSafeFileName(theme.AppsImageFileName)
            };

            await WriteThemeImageAsync(folderPath, manifest.HomeImage, theme.HomeImageBase64, cancellationToken).ConfigureAwait(false);
            await WriteThemeImageAsync(folderPath, manifest.GamesImage, theme.GamesImageBase64, cancellationToken).ConfigureAwait(false);
            await WriteThemeImageAsync(folderPath, manifest.SettingsImage, theme.SettingsImageBase64, cancellationToken).ConfigureAwait(false);
            await WriteThemeImageAsync(folderPath, manifest.AppsImage, theme.AppsImageBase64, cancellationToken).ConfigureAwait(false);

            await using var stream = File.Create(Path.Combine(folderPath, "theme.json"));
            await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> ReadThemeImageBase64Async(string folderPath, string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var filePath = Path.Combine(folderPath, fileName);
        if (!File.Exists(filePath))
        {
            return string.Empty;
        }

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(bytes);
    }

    private static async Task WriteThemeImageAsync(string folderPath, string fileName, string base64, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(folderPath, fileName);
        if (string.IsNullOrWhiteSpace(base64))
        {
            DeleteIfExists(destination);
            return;
        }

        var bytes = Convert.FromBase64String(base64);
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static GameLibrary CloneLibrary(GameLibrary library)
        => NormalizeLibrary(new GameLibrary
        {
            LibraryPaths = library.LibraryPaths.ToList(),
            Games = library.Games
                .Select(game => new GameMetadata
                {
                    Id = game.Id,
                    Title = game.Title,
                    LaunchType = game.LaunchType,
                    ExecutablePath = game.ExecutablePath,
                    SteamAppId = game.SteamAppId,
                    InstallPath = game.InstallPath,
                    LaunchCommand = game.LaunchCommand,
                    Arguments = game.Arguments,
                    WorkingDirectory = game.WorkingDirectory,
                    CoverArtPath = game.CoverArtPath,
                    HeaderImagePath = game.HeaderImagePath,
                    StoreScreenshotPath = game.StoreScreenshotPath,
                    BackgroundArtPath = game.BackgroundArtPath,
                    LogoImagePath = game.LogoImagePath,
                    CoverZoom = game.CoverZoom,
                    CoverOffsetX = game.CoverOffsetX,
                    CoverOffsetY = game.CoverOffsetY,
                    Genre = game.Genre,
                    Rating = game.Rating,
                    MultiplayerInfo = game.MultiplayerInfo,
                    CoOpInfo = game.CoOpInfo,
                    ReviewStarRating = game.ReviewStarRating,
                    ReviewCount = game.ReviewCount,
                    Platform = game.Platform,
                    IsFavorite = game.IsFavorite,
                    LastPlayed = game.LastPlayed,
                    Playtime = game.Playtime
                })
                .ToList()
        });

    private static GameLibrary NormalizeLibrary(GameLibrary? library)
    {
        library ??= new GameLibrary();
        library.LibraryPaths ??= [];
        library.Games ??= [];

        foreach (var game in library.Games)
        {
            game.Id = string.IsNullOrWhiteSpace(game.Id) ? Guid.NewGuid().ToString("N") : game.Id;
            game.Title ??= string.Empty;
            game.LaunchType = string.IsNullOrWhiteSpace(game.LaunchType) ? "Exe" : game.LaunchType;
            game.ExecutablePath ??= string.Empty;
            game.SteamAppId ??= string.Empty;
            game.InstallPath ??= string.Empty;
            game.LaunchCommand ??= string.Empty;
            game.Arguments ??= string.Empty;
            game.WorkingDirectory ??= string.Empty;
            game.CoverArtPath ??= string.Empty;
            game.HeaderImagePath ??= string.Empty;
            game.StoreScreenshotPath ??= string.Empty;
            game.BackgroundArtPath ??= string.Empty;
            game.LogoImagePath ??= string.Empty;
            game.Genre ??= string.Empty;
            game.Rating ??= string.Empty;
            game.MultiplayerInfo ??= string.Empty;
            game.CoOpInfo ??= string.Empty;
            game.Platform ??= string.Empty;
        }

        return library;
    }

    private static async Task WriteBackupAsync(DashboardBackup backup, string filePath, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, backup, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DashboardBackup> ReadAndValidateBackupAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new InvalidDataException("The selected backup file could not be found.");
        }

        await using var stream = File.OpenRead(filePath);
        var backup = await JsonSerializer.DeserializeAsync<DashboardBackup>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (backup is null)
        {
            throw new InvalidDataException("The selected backup file is empty or unreadable.");
        }

        backup.Settings ??= new DashboardBackupSettings();
        backup.Profile ??= new DashboardBackupProfile();
        backup.Library = NormalizeLibrary(backup.Library);
        backup.CustomThemes ??= [];
        return backup;
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "profile-import.png" : safe;
    }

    private static string MakeSafeFolderName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value
            .Where(ch => !invalid.Contains(ch))
            .Select(ch => char.IsWhiteSpace(ch) ? '_' : ch)
            .ToArray())
            .Trim('_');

        return string.IsNullOrWhiteSpace(safe) ? "Custom_Theme" : safe;
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
}
