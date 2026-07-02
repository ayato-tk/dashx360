using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface ISteamLibraryScannerService
{
    Task<SteamGameScanResult> ScanAsync(GameLibrary library, CancellationToken cancellationToken = default);
}
