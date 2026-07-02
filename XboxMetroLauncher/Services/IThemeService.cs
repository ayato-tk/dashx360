using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface IThemeService
{
    Task<IReadOnlyList<DashboardTheme>> LoadThemesAsync(CancellationToken cancellationToken = default);
    Task<DashboardTheme> CreateThemeAsync(
        string themeName,
        string? homeImagePath,
        string? gamesImagePath,
        string? settingsImagePath,
        string? appsImagePath,
        CancellationToken cancellationToken = default);
}
