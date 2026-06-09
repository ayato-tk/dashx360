using System.Threading;
using System.Threading.Tasks;

namespace XboxMetroLauncher.Services;

public interface IJsonStore
{
	Task<T?> ReadAsync<T>(string fileName, CancellationToken cancellationToken = default(CancellationToken));

	Task WriteAsync<T>(string fileName, T value, CancellationToken cancellationToken = default(CancellationToken));
}
