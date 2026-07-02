using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface ISteamCommunityService
{
    Task<SteamCommunityConfig> LoadConfigAsync(CancellationToken cancellationToken = default);

    Task SaveConfigAsync(SteamCommunityConfig config, CancellationToken cancellationToken = default);

    Task<SteamConnectionTestResult> TestConnectionAsync(
        SteamCommunityConfig config,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SocialFriend>> LoadFriendsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SteamAchievementItem>> LoadAchievementsAsync(
        string appId,
        CancellationToken cancellationToken = default);

    Task<SteamGameDetails> LoadGameDetailsAsync(
        string appId,
        CancellationToken cancellationToken = default);

    string LastStatusMessage { get; }

    bool IsConfigured { get; }
}
