using System.Threading;
using System.Threading.Tasks;

namespace XboxMetroLauncher.Services;

public interface ISearchService
{
	Task SearchWebAsync(string query, string baseSearchUrl, CancellationToken cancellationToken = default(CancellationToken));
}
