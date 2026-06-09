using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public sealed class FriendsService : IFriendsService
{
	private const string FriendsFileName = "friends.json";

	private readonly IJsonStore _jsonStore;

	public FriendsService(IJsonStore jsonStore)
	{
		_jsonStore = jsonStore;
	}

	public async Task<IReadOnlyList<FriendProfile>> LoadAsync()
	{
		return (await _jsonStore.ReadAsync<FriendsData>("friends.json").ConfigureAwait(continueOnCapturedContext: false))?.Friends ?? new List<FriendProfile>();
	}

	public Task SaveAsync(IReadOnlyList<FriendProfile> friends)
	{
		return _jsonStore.WriteAsync("friends.json", new FriendsData
		{
			Friends = friends.ToList()
		});
	}
}
