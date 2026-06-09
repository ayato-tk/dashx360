using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class ProfileService : IProfileService
{
	private const string ProfileFileName = "profile.json";

	private static readonly string DefaultGamerPicturePath = Path.Combine(AppPaths.AppFolder, "Assets", "Profile", "profilepicture.jpg");

	private readonly IJsonStore _store;

	public ProfileService(IJsonStore store)
	{
		_store = store;
	}

	public async Task<Profile> LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		Profile profile = await _store.ReadAsync<Profile>("profile.json", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (profile != null)
		{
			return profile;
		}
		profile = new Profile
		{
			Gamertag = "MetroPilot",
			GamerPicturePath = DefaultGamerPicturePath,
			Gamerscore = 36000,
			OnlineStatus = "Online",
			Motto = "(No motto)",
			Description = "(No bio)"
		};
		await SaveAsync(profile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		return profile;
	}

	public Task SaveAsync(Profile profile, CancellationToken cancellationToken = default(CancellationToken))
	{
		return _store.WriteAsync("profile.json", profile, cancellationToken);
	}
}
