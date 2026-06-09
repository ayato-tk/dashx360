using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface IProfileService
{
	Task<Profile> LoadAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task SaveAsync(Profile profile, CancellationToken cancellationToken = default(CancellationToken));
}
