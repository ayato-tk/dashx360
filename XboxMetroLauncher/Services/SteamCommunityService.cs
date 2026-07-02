using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class SteamCommunityService : ISteamCommunityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly TimeSpan FriendsCacheAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AchievementsCacheAge = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan GameDetailsCacheAge = TimeSpan.FromHours(12);
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private readonly string _configPath;
    private readonly string _cacheFolder;

    public SteamCommunityService()
    {
        _configPath = Path.Combine(AppPaths.UserDataFolder, "steam-web-config.json");
        _cacheFolder = Path.Combine(AppPaths.UserDataFolder, "SteamCache");
        Directory.CreateDirectory(_cacheFolder);
        EnsureConfigExample();
    }

    public string LastStatusMessage { get; private set; } = string.Empty;

    public bool IsConfigured
    {
        get
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    return false;
                }

                var config = JsonSerializer.Deserialize<SteamCommunityConfig>(File.ReadAllText(_configPath), JsonOptions);
                return config is not null && HasCredentials(config);
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<IReadOnlyList<SocialFriend>> LoadFriendsAsync(CancellationToken cancellationToken = default)
    {
        LastStatusMessage = string.Empty;
        var config = await LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        if (!HasCredentials(config))
        {
            LastStatusMessage = "Steam friends need UserData\\steam-web-config.json";
            return [];
        }

        var cachePath = Path.Combine(_cacheFolder, "friends.json");
        var cached = await ReadFreshCacheAsync<List<SocialFriend>>(cachePath, FriendsCacheAge, cancellationToken).ConfigureAwait(false);
        if (cached is not null && cached.Any(HasStaleSteamFriendCache))
        {
            cached = null;
        }

        if (cached is not null)
        {
            return cached.Select(NormalizeSteamFriendDisplay).ToList();
        }

        try
        {
            var friendsUri = $"https://api.steampowered.com/ISteamUser/GetFriendList/v0001/?key={Uri.EscapeDataString(config.SteamApiKey)}&steamid={Uri.EscapeDataString(config.SteamId64)}&relationship=friend";
            var friendsResponse = await GetJsonAsync<SteamFriendsResponse>(friendsUri, cancellationToken).ConfigureAwait(false);
            var ids = friendsResponse?.FriendsList?.Friends?
                .Select(friend => friend.SteamId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToList() ?? [];

            if (ids.Count == 0)
            {
                await WriteCacheAsync(cachePath, new List<SocialFriend>(), cancellationToken).ConfigureAwait(false);
                return [];
            }

            var summariesUri = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={Uri.EscapeDataString(config.SteamApiKey)}&steamids={Uri.EscapeDataString(string.Join(',', ids))}";
            var summaries = await GetJsonAsync<SteamPlayerSummariesResponse>(summariesUri, cancellationToken).ConfigureAwait(false);
            var players = summaries?.Response?.Players ?? [];
            var mapped = players
                .Select(MapPlayer)
                .Select(NormalizeSteamFriendDisplay)
                .OrderByDescending(friend => friend.IsOnline)
                .ThenBy(friend => friend.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            mapped = await CacheSteamAvatarsAsync(mapped, cancellationToken).ConfigureAwait(false);
            await WriteCacheAsync(cachePath, mapped, cancellationToken).ConfigureAwait(false);
            return mapped;
        }
        catch (Exception ex)
        {
            LastStatusMessage = $"Steam friends unavailable: {ex.Message}";
            var fallback = await ReadCacheAsync<List<SocialFriend>>(cachePath, cancellationToken).ConfigureAwait(false) ?? [];
            return fallback.Select(NormalizeSteamFriendDisplay).ToList();
        }
    }

    public async Task SaveConfigAsync(SteamCommunityConfig config, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        await using var stream = File.Create(_configPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SteamConnectionTestResult> TestConnectionAsync(
        SteamCommunityConfig config,
        CancellationToken cancellationToken = default)
    {
        if (!HasCredentials(config))
        {
            return new SteamConnectionTestResult
            {
                Success = false,
                Message = "Enter a Steam Web API key and SteamID64 first."
            };
        }

        try
        {
            var summariesUri = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={Uri.EscapeDataString(config.SteamApiKey)}&steamids={Uri.EscapeDataString(config.SteamId64)}";
            var summaries = await GetJsonAsync<SteamPlayerSummariesResponse>(summariesUri, cancellationToken).ConfigureAwait(false);
            var player = summaries?.Response?.Players?.FirstOrDefault();
            if (player is null)
            {
                return new SteamConnectionTestResult
                {
                    Success = false,
                    Message = "Steam did not find that profile. Check the SteamID64."
                };
            }

            var displayName = string.IsNullOrWhiteSpace(player.PersonaName) ? player.SteamId : player.PersonaName;
            return new SteamConnectionTestResult
            {
                Success = true,
                DisplayName = displayName,
                Message = $"Connected as {displayName}."
            };
        }
        catch (Exception ex)
        {
            return new SteamConnectionTestResult
            {
                Success = false,
                Message = $"Steam connection failed: {ex.Message}"
            };
        }
    }

    public async Task<IReadOnlyList<SteamAchievementItem>> LoadAchievementsAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        LastStatusMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(appId))
        {
            LastStatusMessage = "Select a Steam game first.";
            return [];
        }

        var config = await LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        if (!HasCredentials(config))
        {
            LastStatusMessage = "Steam achievements need UserData\\steam-web-config.json";
            return [];
        }

        var safeAppId = new string(appId.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(safeAppId))
        {
            LastStatusMessage = "This game does not have a Steam AppID.";
            return [];
        }

        var cachePath = Path.Combine(_cacheFolder, "Achievements", $"{safeAppId}.json");
        var cached = await ReadFreshCacheAsync<List<SteamAchievementItem>>(cachePath, AchievementsCacheAge, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var uri = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/?key={Uri.EscapeDataString(config.SteamApiKey)}&steamid={Uri.EscapeDataString(config.SteamId64)}&appid={Uri.EscapeDataString(safeAppId)}&l=en";
            var response = await GetJsonAsync<SteamAchievementsResponse>(uri, cancellationToken).ConfigureAwait(false);
            if (response?.PlayerStats?.Success == false)
            {
                return await LoadAchievementSchemaFallbackAsync(config, safeAppId, cachePath, "Steam did not return unlock status for this game.", cancellationToken)
                    .ConfigureAwait(false);
            }

            var achievements = response?.PlayerStats?.Achievements?
                .Select(item => new SteamAchievementItem
                {
                    ApiName = item.ApiName ?? string.Empty,
                    Name = string.IsNullOrWhiteSpace(item.Name) ? item.ApiName ?? "Achievement" : item.Name,
                    Description = item.Description ?? string.Empty,
                    Achieved = item.Achieved > 0,
                    UnlockTimeUnix = item.UnlockTime
                })
                .OrderBy(item => item.Achieved)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList() ?? [];

            await WriteCacheAsync(cachePath, achievements, cancellationToken).ConfigureAwait(false);
            return achievements;
        }
        catch (Exception ex)
        {
            return await LoadAchievementSchemaFallbackAsync(config, safeAppId, cachePath, $"Steam unlock status unavailable: {FriendlySteamError(ex)}", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<SteamGameDetails> LoadGameDetailsAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        var safeAppId = new string((appId ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(safeAppId))
        {
            return new SteamGameDetails();
        }

        var playtime = await LoadSteamPlaytimeAsync(safeAppId, cancellationToken).ConfigureAwait(false);
        var storeDetails = await LoadSteamStoreDetailsAsync(safeAppId, cancellationToken).ConfigureAwait(false);

        return new SteamGameDetails
        {
            Playtime = playtime,
            Genre = storeDetails.Genre,
            Rating = storeDetails.Rating,
            MultiplayerInfo = storeDetails.MultiplayerInfo,
            CoOpInfo = storeDetails.CoOpInfo,
            StoreScreenshotPath = storeDetails.StoreScreenshotPath,
            ReviewStarRating = storeDetails.ReviewStarRating,
            ReviewCount = storeDetails.ReviewCount
        };
    }

    private async Task<TimeSpan?> LoadSteamPlaytimeAsync(string safeAppId, CancellationToken cancellationToken)
    {
        var config = await LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        if (!HasCredentials(config))
        {
            return await LoadLocalSteamPlaytimeAsync(safeAppId, cancellationToken).ConfigureAwait(false);
        }

        var cachePath = Path.Combine(_cacheFolder, "owned-games.json");
        var ownedGames = await ReadFreshCacheAsync<SteamOwnedGamesResponse>(cachePath, GameDetailsCacheAge, cancellationToken)
            .ConfigureAwait(false);

        if (ownedGames is null)
        {
            try
            {
                var uri = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={Uri.EscapeDataString(config.SteamApiKey)}&steamid={Uri.EscapeDataString(config.SteamId64)}&include_played_free_games=1&format=json";
                ownedGames = await GetJsonAsync<SteamOwnedGamesResponse>(uri, cancellationToken).ConfigureAwait(false);
                if (ownedGames is not null)
                {
                    await WriteCacheAsync(cachePath, ownedGames, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                ownedGames = await ReadCacheAsync<SteamOwnedGamesResponse>(cachePath, cancellationToken).ConfigureAwait(false);
            }
        }

        var game = ownedGames?.Response?.Games?
            .FirstOrDefault(candidate => candidate.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture) == safeAppId);
        if (game is not null && game.PlaytimeForever > 0)
        {
            return TimeSpan.FromMinutes(game.PlaytimeForever);
        }

        return await LoadLocalSteamPlaytimeAsync(safeAppId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TimeSpan?> LoadLocalSteamPlaytimeAsync(string safeAppId, CancellationToken cancellationToken)
    {
        var steamPath = FindSteamPath();
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return null;
        }

        var userdataPath = Path.Combine(steamPath, "userdata");
        if (!Directory.Exists(userdataPath))
        {
            return null;
        }

        var bestMinutes = 0;
        foreach (var localConfigPath in Directory.EnumerateFiles(userdataPath, "localconfig.vdf", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            try
            {
                text = await File.ReadAllTextAsync(localConfigPath, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            var appMatch = Regex.Match(
                text,
                $"\"{Regex.Escape(safeAppId)}\"\\s*\\{{(?<body>.*?)\\n\\s*\\}}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!appMatch.Success)
            {
                continue;
            }

            var playtimeMatch = Regex.Match(
                appMatch.Groups["body"].Value,
                "\"playtime\"\\s*\"(?<minutes>\\d+)\"",
                RegexOptions.IgnoreCase);
            if (playtimeMatch.Success
                && int.TryParse(playtimeMatch.Groups["minutes"].Value, out var minutes)
                && minutes > bestMinutes)
            {
                bestMinutes = minutes;
            }
        }

        return bestMinutes > 0 ? TimeSpan.FromMinutes(bestMinutes) : null;
    }

    private async Task<SteamGameDetails> LoadSteamStoreDetailsAsync(string safeAppId, CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(_cacheFolder, "StoreDetails", $"{safeAppId}.json");
        var cached = await ReadFreshCacheAsync<SteamStoreAppDetails>(cachePath, GameDetailsCacheAge, cancellationToken)
            .ConfigureAwait(false);

        if (cached is null)
        {
            try
            {
                var uri = $"https://store.steampowered.com/api/appdetails?appids={Uri.EscapeDataString(safeAppId)}&filters=genres,categories,ratings,screenshots";
                var response = await GetJsonAsync<Dictionary<string, SteamStoreEnvelope>>(uri, cancellationToken).ConfigureAwait(false);
                cached = response is not null
                         && response.TryGetValue(safeAppId, out var envelope)
                         && envelope.Success
                    ? envelope.Data
                    : null;

                if (cached is not null)
                {
                    await WriteCacheAsync(cachePath, cached, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                cached = await ReadCacheAsync<SteamStoreAppDetails>(cachePath, cancellationToken).ConfigureAwait(false);
            }
        }

        if (cached is null)
        {
            return new SteamGameDetails();
        }

        var categories = cached.Categories?
            .Select(category => category.Description)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];
        var genres = cached.Genres?
            .Select(genre => genre.Description)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(2)
            .ToList() ?? [];
        var reviewSummary = await LoadSteamReviewSummaryAsync(safeAppId, cancellationToken).ConfigureAwait(false);
        var screenshotPath = await CacheStoreScreenshotAsync(safeAppId, cached.Screenshots, cancellationToken).ConfigureAwait(false);

        return new SteamGameDetails
        {
            Genre = genres.Count == 0 ? string.Empty : string.Join(" & ", genres),
            Rating = BuildRatingLabel(cached.Ratings),
            MultiplayerInfo = BuildCategoryLine(categories, "Multiplayer", ["Multi-player", "MMO", "PvP", "Online PvP"]),
            CoOpInfo = BuildCategoryLine(categories, "Co-op", ["Co-op", "Online Co-op", "Shared/Split Screen Co-op", "LAN Co-op"]),
            StoreScreenshotPath = screenshotPath,
            ReviewStarRating = reviewSummary.Stars,
            ReviewCount = reviewSummary.Count
        };
    }

    private async Task<(double Stars, int Count)> LoadSteamReviewSummaryAsync(string safeAppId, CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(_cacheFolder, "StoreReviews", $"{safeAppId}.json");
        var cached = await ReadFreshCacheAsync<SteamReviewResponse>(cachePath, GameDetailsCacheAge, cancellationToken)
            .ConfigureAwait(false);

        if (cached is null)
        {
            try
            {
                var uri = $"https://store.steampowered.com/appreviews/{Uri.EscapeDataString(safeAppId)}?json=1&purchase_type=all&num_per_page=0&language=all";
                cached = await GetJsonAsync<SteamReviewResponse>(uri, cancellationToken).ConfigureAwait(false);
                if (cached is not null)
                {
                    await WriteCacheAsync(cachePath, cached, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                cached = await ReadCacheAsync<SteamReviewResponse>(cachePath, cancellationToken).ConfigureAwait(false);
            }
        }

        var summary = cached?.QuerySummary;
        if (summary is null || summary.TotalReviews <= 0)
        {
            return (0, 0);
        }

        var positiveRatio = summary.TotalPositive > 0
            ? summary.TotalPositive / (double)summary.TotalReviews
            : summary.ReviewScore / 9.0;
        var stars = Math.Clamp(positiveRatio * 5.0, 0, 5);
        return (stars, summary.TotalReviews);
    }

    private async Task<string> CacheStoreScreenshotAsync(
        string safeAppId,
        IReadOnlyList<SteamStoreScreenshot>? screenshots,
        CancellationToken cancellationToken)
    {
        var url = screenshots?
            .Select(screenshot => string.IsNullOrWhiteSpace(screenshot.PathThumbnail) ? screenshot.PathFull : screenshot.PathThumbnail)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var folder = Path.Combine(_cacheFolder, "StoreImages", safeAppId);
        var extension = Path.GetExtension(new Uri(url).AbsolutePath);
        if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".jpg";
        }

        var path = Path.Combine(folder, $"screenshot{extension}");
        if (File.Exists(path))
        {
            return path;
        }

        try
        {
            Directory.CreateDirectory(folder);
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = File.Create(path);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            return path;
        }
        catch
        {
            return File.Exists(path) ? path : string.Empty;
        }
    }

    private static string BuildRatingLabel(Dictionary<string, SteamStoreRating>? ratings)
    {
        if (ratings is null || ratings.Count == 0)
        {
            return string.Empty;
        }

        foreach (var key in new[] { "esrb", "pegi", "usk", "oflc", "dejus" })
        {
            if (ratings.TryGetValue(key, out var rating) && !string.IsNullOrWhiteSpace(rating.Rating))
            {
                return rating.Rating.Trim().ToUpperInvariant();
            }
        }

        return ratings.Values.Select(value => value.Rating).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    private static string? FindSteamPath()
    {
        var registryPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrWhiteSpace(registryPath) && Directory.Exists(registryPath))
        {
            return registryPath;
        }

        var installPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string
                          ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string;
        if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
        {
            return installPath;
        }

        return new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" }
            .FirstOrDefault(Directory.Exists);
    }

    private static string BuildCategoryLine(IReadOnlyCollection<string> categories, string label, string[] matches)
    {
        var found = categories
            .Where(category => matches.Any(match => category.Contains(match, StringComparison.OrdinalIgnoreCase)))
            .Take(2)
            .ToList();
        return found.Count == 0 ? $"{label}: None" : $"{label}: {string.Join(" & ", found)}";
    }

    private async Task<IReadOnlyList<SteamAchievementItem>> LoadAchievementSchemaFallbackAsync(
        SteamCommunityConfig config,
        string safeAppId,
        string cachePath,
        string reason,
        CancellationToken cancellationToken)
    {
        var cached = await ReadCacheAsync<List<SteamAchievementItem>>(cachePath, cancellationToken).ConfigureAwait(false);

        try
        {
            var uri = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={Uri.EscapeDataString(config.SteamApiKey)}&appid={Uri.EscapeDataString(safeAppId)}&l=en";
            var response = await GetJsonAsync<SteamAchievementSchemaResponse>(uri, cancellationToken).ConfigureAwait(false);
            var achievements = response?.Game?.AvailableGameStats?.Achievements?
                .Select(item => new SteamAchievementItem
                {
                    ApiName = item.Name ?? string.Empty,
                    Name = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name ?? "Achievement" : item.DisplayName,
                    Description = item.Description ?? string.Empty,
                    Achieved = false
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList() ?? [];

            if (achievements.Count > 0)
            {
                LastStatusMessage = $"{reason} Showing the public achievement list instead.";
                await WriteCacheAsync(cachePath, achievements, cancellationToken).ConfigureAwait(false);
                return achievements;
            }
        }
        catch (Exception schemaEx)
        {
            LastStatusMessage = $"{reason} Public achievement list unavailable: {FriendlySteamError(schemaEx)}";
            return cached ?? [];
        }

        LastStatusMessage = cached is { Count: > 0 }
            ? $"{reason} Showing cached achievements."
            : "Steam does not expose achievements for this game.";
        return cached ?? [];
    }

    private static string FriendlySteamError(Exception ex)
    {
        if (ex is HttpRequestException httpRequestException && httpRequestException.StatusCode is not null)
        {
            return $"Steam returned {(int)httpRequestException.StatusCode.Value} {httpRequestException.StatusCode.Value}.";
        }

        return ex.Message;
    }

    private static SocialFriend MapPlayer(SteamPlayerSummary player)
    {
        var isOnline = player.PersonaState > 0;
        var currentGame = string.IsNullOrWhiteSpace(player.GameExtraInfo)
            ? string.Empty
            : player.GameExtraInfo.Trim();
        var stats = BuildSteamProfileStats(player.SteamId, player.PersonaName);

        return new SocialFriend
        {
            Id = $"steam:{player.SteamId}",
            DisplayName = string.IsNullOrWhiteSpace(player.PersonaName) ? "Steam Friend" : player.PersonaName,
            Source = SocialFriendSource.Steam,
            AvatarPathOrUrl = player.AvatarFull ?? player.AvatarMedium ?? player.Avatar ?? string.Empty,
            IsOnline = isOnline,
            StatusText = isOnline ? "Online" : "Offline",
            ActivityText = currentGame,
            ActivityAppId = player.GameId ?? string.Empty,
            GamerscoreText = stats.GamerscoreText,
            ReputationText = stats.ReputationText,
            ZoneText = stats.ZoneText,
            IdentityDetailText = "Steam"
        };
    }

    private static (string GamerscoreText, string ReputationText, string ZoneText) BuildSteamProfileStats(string steamId, string personaName)
    {
        var seedText = string.IsNullOrWhiteSpace(steamId) ? personaName : steamId;
        var seed = seedText.Aggregate(23, (current, character) => unchecked(current * 31 + character));
        var random = new Random(seed);
        var zones = new[] { "Recreation", "Family", "Pro", "Underground" };
        var gamerscore = random.Next(2500, 125000);
        var filledStars = random.Next(3, 6);
        var reputation = new string('★', filledStars) + new string('☆', 5 - filledStars);

        return ($"{gamerscore:N0} G", reputation, zones[random.Next(zones.Length)]);
    }

    private static SocialFriend NormalizeSteamFriendDisplay(SocialFriend friend)
    {
        if (friend.Source is not SocialFriendSource.Steam)
        {
            return friend;
        }

        return new SocialFriend
        {
            Id = friend.Id,
            DisplayName = friend.DisplayName,
            Source = friend.Source,
            AvatarPathOrUrl = friend.AvatarPathOrUrl,
            IsOnline = friend.IsOnline,
            StatusText = friend.IsOnline ? "Online" : "Offline",
            ActivityText = friend.ActivityText,
            ActivityAppId = friend.ActivityAppId,
            GamerscoreText = friend.GamerscoreText,
            ReputationText = friend.ReputationText,
            ZoneText = friend.ZoneText,
            IdentityDetailText = friend.IdentityDetailText,
            IsPartyHost = friend.IsPartyHost,
            ShowVoiceIndicator = friend.ShowVoiceIndicator
        };
    }

    private static bool HasBlankSteamAvatar(SocialFriend friend)
        => friend.Source is SocialFriendSource.Steam && string.IsNullOrWhiteSpace(friend.AvatarPathOrUrl);

    private static bool HasStaleSteamFriendCache(SocialFriend friend)
        => friend.Source is SocialFriendSource.Steam
           && (string.IsNullOrWhiteSpace(friend.AvatarPathOrUrl)
               || string.Equals(friend.StatusText, "Away", StringComparison.OrdinalIgnoreCase));

    private async Task<List<SocialFriend>> CacheSteamAvatarsAsync(
        IReadOnlyList<SocialFriend> friends,
        CancellationToken cancellationToken)
    {
        var avatarFolder = Path.Combine(_cacheFolder, "FriendAvatars");
        Directory.CreateDirectory(avatarFolder);

        using var gate = new SemaphoreSlim(6);
        var tasks = friends.Select(friend => CacheSteamAvatarAsync(friend, avatarFolder, gate, cancellationToken));
        return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
    }

    private static async Task<SocialFriend> CacheSteamAvatarAsync(
        SocialFriend friend,
        string avatarFolder,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        if (friend.Source is not SocialFriendSource.Steam
            || !TryCreateHttpUri(friend.AvatarPathOrUrl, out var uri))
        {
            return friend;
        }

        var fileName = SanitizeFileName(friend.Id.Replace("steam:", string.Empty));
        var extension = GetAvatarExtension(uri);
        var localPath = Path.Combine(avatarFolder, $"{fileName}{extension}");

        if (!File.Exists(localPath))
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var response = await Http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return friend;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return friend;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var file = File.Create(localPath);
                await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return friend;
            }
            finally
            {
                gate.Release();
            }
        }

        return CopySteamFriend(friend, localPath);
    }

    private static SocialFriend CopySteamFriend(SocialFriend friend, string avatarPath)
        => new()
        {
            Id = friend.Id,
            DisplayName = friend.DisplayName,
            Source = friend.Source,
            AvatarPathOrUrl = avatarPath,
            IsOnline = friend.IsOnline,
            StatusText = friend.StatusText,
            ActivityText = friend.ActivityText,
            ActivityAppId = friend.ActivityAppId,
            GamerscoreText = friend.GamerscoreText,
            ReputationText = friend.ReputationText,
            ZoneText = friend.ZoneText,
            IdentityDetailText = friend.IdentityDetailText,
            IsPartyHost = friend.IsPartyHost,
            ShowVoiceIndicator = friend.ShowVoiceIndicator
        };

    private static bool TryCreateHttpUri(string path, out Uri uri)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out uri!)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static string GetAvatarExtension(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? extension
            : ".jpg";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "steam-friend" : safe;
    }

    public async Task<SteamCommunityConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_configPath))
        {
            return new SteamCommunityConfig();
        }

        await using var stream = File.OpenRead(_configPath);
        return await JsonSerializer.DeserializeAsync<SteamCommunityConfig>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new SteamCommunityConfig();
    }

    private static bool HasCredentials(SteamCommunityConfig config)
        => !string.IsNullOrWhiteSpace(config.SteamApiKey)
           && !string.IsNullOrWhiteSpace(config.SteamId64);

    private static async Task<T?> GetJsonAsync<T>(string uri, CancellationToken cancellationToken)
    {
        await using var stream = await Http.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> ReadFreshCacheAsync<T>(string path, TimeSpan maxAge, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path) > maxAge)
        {
            return default;
        }

        return await ReadCacheAsync<T>(path, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T?> ReadCacheAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteCacheAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureConfigExample()
    {
        var examplePath = Path.Combine(AppPaths.UserDataFolder, "steam-web-config.example.json");
        if (File.Exists(examplePath))
        {
            return;
        }

        var example = new SteamCommunityConfig
        {
            SteamApiKey = "paste-your-steam-web-api-key-here",
            SteamId64 = "paste-your-steamid64-here"
        };
        File.WriteAllText(examplePath, JsonSerializer.Serialize(example, JsonOptions));
    }

    private static string SteamPersonaStateLabel(int state)
        => state switch
        {
            1 => "Online",
            2 => "Busy",
            3 => "Away",
            4 => "Snooze",
            5 => "Looking to trade",
            6 => "Looking to play",
            _ => "Offline"
        };

    private sealed class SteamFriendsResponse
    {
        [JsonPropertyName("friendslist")]
        public SteamFriendsList? FriendsList { get; set; }
    }

    private sealed class SteamFriendsList
    {
        [JsonPropertyName("friends")]
        public List<SteamFriendEntry>? Friends { get; set; }
    }

    private sealed class SteamFriendEntry
    {
        [JsonPropertyName("steamid")]
        public string SteamId { get; set; } = string.Empty;
    }

    private sealed class SteamPlayerSummariesResponse
    {
        [JsonPropertyName("response")]
        public SteamPlayerSummaries? Response { get; set; }
    }

    private sealed class SteamPlayerSummaries
    {
        [JsonPropertyName("players")]
        public List<SteamPlayerSummary>? Players { get; set; }
    }

    private sealed class SteamPlayerSummary
    {
        [JsonPropertyName("steamid")]
        public string SteamId { get; set; } = string.Empty;

        [JsonPropertyName("personaname")]
        public string PersonaName { get; set; } = string.Empty;

        [JsonPropertyName("personastate")]
        public int PersonaState { get; set; }

        [JsonPropertyName("avatar")]
        public string? Avatar { get; set; }

        [JsonPropertyName("avatarmedium")]
        public string? AvatarMedium { get; set; }

        [JsonPropertyName("avatarfull")]
        public string? AvatarFull { get; set; }

        [JsonPropertyName("gameextrainfo")]
        public string? GameExtraInfo { get; set; }

        [JsonPropertyName("gameid")]
        public string? GameId { get; set; }
    }

    private sealed class SteamAchievementsResponse
    {
        [JsonPropertyName("playerstats")]
        public SteamPlayerStats? PlayerStats { get; set; }
    }

    private sealed class SteamPlayerStats
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; } = true;

        [JsonPropertyName("achievements")]
        public List<SteamAchievementResponseItem>? Achievements { get; set; }
    }

    private sealed class SteamAchievementResponseItem
    {
        [JsonPropertyName("apiname")]
        public string? ApiName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("achieved")]
        public int Achieved { get; set; }

        [JsonPropertyName("unlocktime")]
        public long UnlockTime { get; set; }
    }

    private sealed class SteamAchievementSchemaResponse
    {
        [JsonPropertyName("game")]
        public SteamAchievementSchemaGame? Game { get; set; }
    }

    private sealed class SteamAchievementSchemaGame
    {
        [JsonPropertyName("availableGameStats")]
        public SteamAvailableGameStats? AvailableGameStats { get; set; }
    }

    private sealed class SteamAvailableGameStats
    {
        [JsonPropertyName("achievements")]
        public List<SteamAchievementSchemaItem>? Achievements { get; set; }
    }

    private sealed class SteamAchievementSchemaItem
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    private sealed class SteamOwnedGamesResponse
    {
        [JsonPropertyName("response")]
        public SteamOwnedGamesData? Response { get; set; }
    }

    private sealed class SteamOwnedGamesData
    {
        [JsonPropertyName("games")]
        public List<SteamOwnedGame>? Games { get; set; }
    }

    private sealed class SteamOwnedGame
    {
        [JsonPropertyName("appid")]
        public int AppId { get; set; }

        [JsonPropertyName("playtime_forever")]
        public int PlaytimeForever { get; set; }
    }

    private sealed class SteamStoreEnvelope
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public SteamStoreAppDetails? Data { get; set; }
    }

    private sealed class SteamStoreAppDetails
    {
        [JsonPropertyName("genres")]
        public List<SteamStoreDescriptionItem>? Genres { get; set; }

        [JsonPropertyName("categories")]
        public List<SteamStoreDescriptionItem>? Categories { get; set; }

        [JsonPropertyName("ratings")]
        public Dictionary<string, SteamStoreRating>? Ratings { get; set; }

        [JsonPropertyName("screenshots")]
        public List<SteamStoreScreenshot>? Screenshots { get; set; }
    }

    private sealed class SteamStoreDescriptionItem
    {
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }

    private sealed class SteamStoreRating
    {
        [JsonPropertyName("rating")]
        public string Rating { get; set; } = string.Empty;
    }

    private sealed class SteamStoreScreenshot
    {
        [JsonPropertyName("path_thumbnail")]
        public string PathThumbnail { get; set; } = string.Empty;

        [JsonPropertyName("path_full")]
        public string PathFull { get; set; } = string.Empty;
    }

    private sealed class SteamReviewResponse
    {
        [JsonPropertyName("query_summary")]
        public SteamReviewSummary? QuerySummary { get; set; }
    }

    private sealed class SteamReviewSummary
    {
        [JsonPropertyName("total_reviews")]
        public int TotalReviews { get; set; }

        [JsonPropertyName("total_positive")]
        public int TotalPositive { get; set; }

        [JsonPropertyName("review_score")]
        public int ReviewScore { get; set; }
    }
}
