using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface IGameLaunchService
{
	Task<GameLaunchResult> LaunchAsync(GameMetadata game, CancellationToken cancellationToken = default(CancellationToken));
}
