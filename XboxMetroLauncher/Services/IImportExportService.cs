using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface IImportExportService
{
	Task ExportAsync(GameLibrary library, Profile profile, AppSettings settings, string filePath, CancellationToken cancellationToken = default(CancellationToken));

	Task<DashboardImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken));
}
