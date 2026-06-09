using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public sealed class SettingsService : ISettingsService
{
	private const string SettingsFileName = "settings.json";

	private readonly IJsonStore _store;

	public SettingsService(IJsonStore store)
	{
		_store = store;
	}

	public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return (await _store.ReadAsync<AppSettings>("settings.json", cancellationToken)) ?? new AppSettings();
	}

	public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default(CancellationToken))
	{
		return _store.WriteAsync("settings.json", settings, cancellationToken);
	}
}
