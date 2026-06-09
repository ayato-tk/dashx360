using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public sealed class LocalSocialIntegrationService : ISocialIntegrationService
{
	private readonly IFriendsService _friendsService;

	public SocialFriendSource Source => SocialFriendSource.Local;

	public LocalSocialIntegrationService(IFriendsService friendsService)
	{
		_friendsService = friendsService;
	}

	public async Task<IReadOnlyList<SocialFriend>> LoadFriendsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<FriendProfile> friends = await _friendsService.LoadAsync().ConfigureAwait(continueOnCapturedContext: false);
		bool flag = false;
		foreach (FriendProfile item in friends)
		{
			string text = ProfileImagePool.ResolveAssignedAvatarPath(item.GamerPicturePath);
			if (!string.Equals(item.GamerPicturePath, text, StringComparison.OrdinalIgnoreCase))
			{
				item.GamerPicturePath = text;
				flag = true;
			}
		}
		if (flag)
		{
			await _friendsService.SaveAsync(friends).ConfigureAwait(continueOnCapturedContext: false);
		}
		return friends.Select(MapFromLocalFriend).OrderBy<SocialFriend, string>((SocialFriend friend) => friend.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	public Task<SocialPartyInviteResult> InviteToPartyAsync(SocialFriend friend, CancellationToken cancellationToken = default(CancellationToken))
	{
		return Task.FromResult(new SocialPartyInviteResult
		{
			AddToPartyList = true
		});
	}

	public static SocialFriend MapFromLocalFriend(FriendProfile friend)
	{
		return new SocialFriend
		{
			Id = "local:" + friend.Gamertag,
			DisplayName = friend.Gamertag,
			Source = SocialFriendSource.Local,
			AvatarPathOrUrl = friend.GamerPicturePath,
			IsOnline = IsOnline(friend.Status),
			StatusText = NormalizeOfflineStatus(friend.Status),
			ActivityText = string.Empty,
			GamerscoreText = $"{friend.Gamerscore:N0} G",
			ReputationText = NormalizeReputation(friend.Reputation),
			ZoneText = (string.IsNullOrWhiteSpace(friend.Zone) ? "Recreation" : friend.Zone),
			IdentityDetailText = BuildLocalIdentityDetail(friend.Gamertag)
		};
	}

	public static FriendProfile MapToLocalFriend(SocialFriend friend)
	{
		return new FriendProfile
		{
			Gamertag = friend.DisplayName,
			RealName = BuildLocalIdentityDetail(friend.DisplayName),
			GamerPicturePath = friend.AvatarPathOrUrl,
			Gamerscore = ParseGamerscore(friend.GamerscoreText),
			Reputation = NormalizeReputation(friend.ReputationText),
			Zone = (string.IsNullOrWhiteSpace(friend.ZoneText) ? "Recreation" : friend.ZoneText),
			Status = NormalizeOfflineStatus(friend.StatusText),
			Country = "Offline"
		};
	}

	private static string BuildLocalIdentityDetail(string gamertag)
	{
		return "Local";
	}

	private static int ParseGamerscore(string gamerscoreText)
	{
		if (string.IsNullOrWhiteSpace(gamerscoreText))
		{
			return 0;
		}
		if (!int.TryParse(new string(gamerscoreText.Where(char.IsDigit).ToArray()), out var result))
		{
			return 0;
		}
		return result;
	}

	private static bool IsOnline(string? status)
	{
		if (!string.IsNullOrWhiteSpace(status) && (status.Contains("Online", StringComparison.OrdinalIgnoreCase) || status.Contains("Away", StringComparison.OrdinalIgnoreCase)))
		{
			return !status.Contains("Last online", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static string NormalizeOfflineStatus(string? status)
	{
		if (string.IsNullOrWhiteSpace(status))
		{
			return "Offline";
		}
		if (status.Contains("Last online", StringComparison.OrdinalIgnoreCase) || status.Contains("Away", StringComparison.OrdinalIgnoreCase) || status.Contains("Offline", StringComparison.OrdinalIgnoreCase))
		{
			return status;
		}
		return "Offline";
	}

	private static string NormalizeReputation(string? reputation)
	{
		if (string.IsNullOrWhiteSpace(reputation))
		{
			return "★★★★★";
		}
		int num = reputation.Count(character => character == '*' || character == '★');
		return new string('★', Math.Clamp((num == 0) ? 5 : num, 1, 5));
	}
}
