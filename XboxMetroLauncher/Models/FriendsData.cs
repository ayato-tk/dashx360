using System.Collections.Generic;

namespace XboxMetroLauncher.Models;

public sealed class FriendsData
{
	public List<FriendProfile> Friends { get; set; } = new List<FriendProfile>();
}
