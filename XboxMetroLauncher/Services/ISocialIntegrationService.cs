using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface ISocialIntegrationService
{
	SocialFriendSource Source { get; }

	Task<IReadOnlyList<SocialFriend>> LoadFriendsAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<SocialPartyInviteResult> InviteToPartyAsync(SocialFriend friend, CancellationToken cancellationToken = default(CancellationToken));
}
