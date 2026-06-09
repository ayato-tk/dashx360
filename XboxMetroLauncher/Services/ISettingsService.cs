using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface ISettingsService
{
	Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default(CancellationToken));
}
