using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class SocialIntegrationManager
{
    private readonly IFriendsService _friendsService;
    private readonly LocalSocialIntegrationService _localService;
    private readonly ISteamCommunityService _steamCommunityService;
    private readonly DiscordSocialService _discordSocialService;

    public SocialIntegrationManager(
        IFriendsService friendsService,
        LocalSocialIntegrationService localService,
        ISteamCommunityService steamCommunityService,
        DiscordSocialService discordSocialService)
    {
        _friendsService = friendsService;
        _localService = localService;
        _steamCommunityService = steamCommunityService;
        _discordSocialService = discordSocialService;
    }

    public async Task<SocialFriendsLoadResult> LoadFriendsAsync(
        SocialIntegrationMode mode,
        DiscordConnectionState discordConnectionState,
        CancellationToken cancellationToken = default)
    {
        var localFriends = await _localService.LoadFriendsAsync(cancellationToken).ConfigureAwait(false);
        var friends = localFriends.ToList();
        var popup = string.Empty;

        if (_steamCommunityService.IsConfigured || mode is SocialIntegrationMode.Steam or SocialIntegrationMode.Hybrid)
        {
            var steamFriends = await _steamCommunityService.LoadFriendsAsync(cancellationToken).ConfigureAwait(false);
            friends.AddRange(steamFriends);
            popup = _steamCommunityService.LastStatusMessage;
        }

        if (_discordSocialService.IsSessionActive)
        {
            var discordFriends = await _discordSocialService.LoadFriendsAsync(cancellationToken).ConfigureAwait(false);
            friends.AddRange(discordFriends);
            if (string.IsNullOrWhiteSpace(popup))
            {
                popup = _discordSocialService.LastStatusMessage;
            }
        }

        return new SocialFriendsLoadResult
        {
            Friends = friends,
            PopupMessage = popup
        };
    }

    public SocialFriend CreateLocalFriend(string gamertag)
    {
        var seed = gamertag.ToLowerInvariant().Aggregate(17, (current, character) => unchecked(current * 31 + character));
        var random = new Random(seed);
        var statuses = new[]
        {
            "Offline",
            "Away",
            $"Last online {random.Next(8, 46)} minutes ago",
            $"Last online {random.Next(1, 12)} hours ago"
        };
        var zones = new[] { "Recreation", "Family", "Pro", "Underground" };
        var repStars = BuildReputation(random.Next(3, 6));

        return new SocialFriend
        {
            Id = $"local:{gamertag}",
            DisplayName = gamertag,
            Source = SocialFriendSource.Local,
            AvatarPathOrUrl = ProfileImagePool.GetRandomPoolAvatarPath(),
            IsOnline = false,
            StatusText = statuses[random.Next(statuses.Length)],
            ActivityText = string.Empty,
            GamerscoreText = $"{random.Next(2500, 95000):N0} G",
            ReputationText = repStars,
            ZoneText = zones[random.Next(zones.Length)],
            IdentityDetailText = "Local"
        };
    }

    public Task<SocialConnectionResult> ConnectDiscordAsync(CancellationToken cancellationToken = default)
        => _discordSocialService.ConnectAsync(cancellationToken);

    public async Task AddLocalFriendAsync(SocialFriend friend, CancellationToken cancellationToken = default)
    {
        var existing = await _friendsService.LoadAsync().ConfigureAwait(false);
        var updated = existing
            .Where(item => !string.Equals(item.Gamertag, friend.DisplayName, StringComparison.OrdinalIgnoreCase))
            .Append(LocalSocialIntegrationService.MapToLocalFriend(friend))
            .ToList();

        await _friendsService.SaveAsync(updated).ConfigureAwait(false);
    }

    public async Task RemoveLocalFriendAsync(SocialFriend friend, CancellationToken cancellationToken = default)
    {
        var existing = await _friendsService.LoadAsync().ConfigureAwait(false);
        var updated = existing
            .Where(item => !string.Equals(item.Gamertag, friend.DisplayName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        await _friendsService.SaveAsync(updated).ConfigureAwait(false);
    }

    public Task<SocialPartyInviteResult> InviteToPartyAsync(
        SocialFriend friend,
        DiscordConnectionState discordConnectionState,
        CancellationToken cancellationToken = default)
    {
        return friend.Source switch
        {
            SocialFriendSource.Local => _localService.InviteToPartyAsync(friend, cancellationToken),
            SocialFriendSource.Steam => Task.FromResult(new SocialPartyInviteResult
            {
                AddToPartyList = false,
                PopupMessage = "Steam friends are read-only in this launcher."
            }),
            SocialFriendSource.Discord => _discordSocialService.InviteToPartyAsync(friend, cancellationToken),
            _ => Task.FromResult(new SocialPartyInviteResult
            {
                AddToPartyList = false,
                PopupMessage = $"{GetSourceLabel(friend)} integration is not available in the public build."
            })
        };
    }

    public static string GetSourceLabel(SocialFriend friend)
        => friend.Source switch
        {
            SocialFriendSource.Discord => "Discord",
            SocialFriendSource.Steam => "Steam",
            _ => "Local"
        };

    public event Action? DiscordFriendsUpdated
    {
        add => _discordSocialService.FriendsUpdated += value;
        remove => _discordSocialService.FriendsUpdated -= value;
    }

    public event Action<ulong>? DiscordDirectMessageReceived
    {
        add => _discordSocialService.DirectMessageReceived += value;
        remove => _discordSocialService.DirectMessageReceived -= value;
    }

    public IReadOnlyList<DiscordDmMessage> GetDiscordDirectMessages(ulong userId)
        => _discordSocialService.GetDirectMessages(userId);

    public Task<(bool Success, string ErrorMessage)> SendDiscordDirectMessageAsync(
        ulong userId,
        string content,
        CancellationToken cancellationToken = default)
        => _discordSocialService.SendDirectMessageAsync(userId, content, cancellationToken);

    public bool IsDiscordFriendAccessAvailable => _discordSocialService.IsSessionActive;

    public bool IsDiscordConfigured => _discordSocialService.IsConfigured;

    public bool IsDiscordSessionActive => _discordSocialService.IsSessionActive;

    public string DiscordApplicationId => _discordSocialService.ConfiguredApplicationId;

    public string DiscordBotToken => _discordSocialService.ConfiguredBotToken;

    public void SaveDiscordConfig(string applicationId, string botToken)
        => _discordSocialService.SaveConfig(applicationId, botToken);

    public Task<IReadOnlyList<DiscordProfileBadge>> GetDiscordUserBadgesAsync(ulong userId, CancellationToken cancellationToken = default)
        => _discordSocialService.GetUserBadgesAsync(userId, cancellationToken);

    public void DisconnectDiscord() => _discordSocialService.DisconnectSession();

    public Task<SocialConnectionResult> RestoreDiscordSessionAsync(
        string accessToken,
        string refreshToken,
        CancellationToken cancellationToken = default)
        => _discordSocialService.RestoreSessionAsync(accessToken, refreshToken, cancellationToken);

    public static string GetFriendStatusLabel(SocialFriend friend)
    {
        if (friend.Source is SocialFriendSource.Steam)
        {
            return friend.IsOnline ? "Online" : "Offline";
        }

        return string.IsNullOrWhiteSpace(friend.StatusText)
            ? (friend.IsOnline ? "Online" : "Offline")
            : friend.StatusText;
    }

    public static string GetFriendActivityLabel(SocialFriend friend)
    {
        if (friend.Source is SocialFriendSource.Steam)
        {
            return string.IsNullOrWhiteSpace(friend.ActivityText)
                ? GetFriendStatusLabel(friend)
                : friend.ActivityText;
        }

        return string.IsNullOrWhiteSpace(friend.ActivityText) ? GetFriendStatusLabel(friend) : friend.ActivityText;
    }

    public static SocialFriend BuildPartyHost(Profile profile)
        => new()
        {
            Id = $"local-host:{profile.Gamertag}",
            DisplayName = profile.Gamertag,
            Source = SocialFriendSource.Local,
            AvatarPathOrUrl = profile.GamerPicturePath,
            IsOnline = true,
            StatusText = profile.OnlineStatus,
            ActivityText = "Xbox 360 Dashboard",
            GamerscoreText = $"{profile.Gamerscore:N0} G",
            ReputationText = "★★★★★",
            ZoneText = "Party",
            IdentityDetailText = $"{profile.Gamertag}'s Profile",
            IsPartyHost = true
        };

    private static string BuildReputation(int filledStars)
        => new string('★', Math.Clamp(filledStars, 0, 5))
           + new string('☆', Math.Clamp(5 - filledStars, 0, 5));
}
