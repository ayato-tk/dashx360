using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public sealed class DiscordPartyService
{
	public Task<DiscordPartySnapshot> GetCurrentPartySnapshotAsync(Profile profile, DiscordConnectionState connectionState, CancellationToken cancellationToken = default(CancellationToken))
	{
		return Task.FromResult(BuildSoloSnapshot(profile, "Party data is local only in the public build."));
	}

	public Task LeaveCurrentPartyAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return Task.CompletedTask;
	}

	public static DiscordPartySnapshot BuildSoloSnapshot(Profile profile, string statusMessage = "")
	{
		SocialFriend socialFriend = SocialIntegrationManager.BuildPartyHost(profile);
		return new DiscordPartySnapshot
		{
			Members = new[]
			{
				new SocialFriend
				{
					Id = socialFriend.Id,
					DisplayName = socialFriend.DisplayName,
					Source = socialFriend.Source,
					AvatarPathOrUrl = socialFriend.AvatarPathOrUrl,
					IsOnline = socialFriend.IsOnline,
					StatusText = socialFriend.StatusText,
					ActivityText = socialFriend.ActivityText,
					GamerscoreText = socialFriend.GamerscoreText,
					ReputationText = socialFriend.ReputationText,
					ZoneText = socialFriend.ZoneText,
					IdentityDetailText = socialFriend.IdentityDetailText,
					IsPartyHost = true,
					ShowVoiceIndicator = socialFriend.ShowVoiceIndicator
				}
			},
			UsesDiscordData = false,
			StatusMessage = statusMessage
		};
	}
}
