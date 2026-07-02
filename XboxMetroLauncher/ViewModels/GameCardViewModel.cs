using System.Windows.Media;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.ViewModels;

public sealed class GameCardViewModel : ObservableObject
{
    public GameCardViewModel(GameMetadata game, Brush accentBrush)
    {
        Game = game;
        AccentBrush = accentBrush;
    }

    public GameMetadata Game { get; }
    public string Title => Game.Title;
    public string TileTitle => BuildTileTitle(Game.Title);
    public string Subtitle => IsSteamGame ? "Steam - Imported" : "PC - Manual";
    public string CoverArtPath => Game.CoverArtPath;
    public double CoverZoom => Game.CoverZoom;
    public double CoverOffsetX => Game.CoverOffsetX;
    public double CoverOffsetY => Game.CoverOffsetY;
    public string BackgroundArtPath => Game.BackgroundArtPath;
    public string DetailsStoreImagePath
        => IsSteamGame && !string.IsNullOrWhiteSpace(Game.StoreScreenshotPath)
            ? Game.StoreScreenshotPath
            : Game.HeaderImagePath;
    public bool IsFavorite => Game.IsFavorite;
    public bool IsSteamGame => string.Equals(Game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase);
    public bool IsManualGame => !IsSteamGame;
    public string DetailsSourceText
        => IsSteamGame
            ? "Steam Imported"
            : "Manual";

    public string DetailsRatingLabel => string.IsNullOrWhiteSpace(Game.Rating) ? "NR" : Game.Rating.Trim().ToUpperInvariant();

    public string DetailsRatingDescription
        => string.IsNullOrWhiteSpace(Game.Rating)
            ? IsSteamGame
                ? "Rating not provided by Steam for this game."
                : "Rating not provided. User-added games may include content from third-party stores."
            : $"{DetailsRatingLabel} rating information from Steam.";

    public Brush AccentBrush { get; }
    public string DetailsGenreText
        => string.IsNullOrWhiteSpace(Game.Genre) || string.Equals(Game.Genre, "Imported", StringComparison.OrdinalIgnoreCase)
            ? "Game"
            : Game.Genre;

    public string DetailsMultiplayerText
        => string.IsNullOrWhiteSpace(Game.MultiplayerInfo) ? "Multiplayer: None" : Game.MultiplayerInfo;

    public string DetailsCoOpText
        => string.IsNullOrWhiteSpace(Game.CoOpInfo) ? "Co-op: None" : Game.CoOpInfo;

    public string DetailsPlaytimeText
    {
        get
        {
            if (Game.Playtime <= TimeSpan.Zero)
            {
                return "Time played: Not tracked";
            }

            var totalHours = (int)Game.Playtime.TotalHours;
            return totalHours > 0
                ? $"Time played: {totalHours}h {Game.Playtime.Minutes}m"
                : $"Time played: {Game.Playtime.Minutes}m";
        }
    }

    public string DetailsReviewStarsText
    {
        get
        {
            var filled = (int)Math.Round(Math.Clamp(Game.ReviewStarRating, 0, 5), MidpointRounding.AwayFromZero);
            return new string('★', filled) + new string('☆', 5 - filled);
        }
    }

    public string DetailsReviewCountText
        => Game.ReviewCount > 0
            ? $"({Game.ReviewCount:N0})"
            : string.Empty;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(TileTitle));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CoverArtPath));
        OnPropertyChanged(nameof(CoverZoom));
        OnPropertyChanged(nameof(CoverOffsetX));
        OnPropertyChanged(nameof(CoverOffsetY));
        OnPropertyChanged(nameof(BackgroundArtPath));
        OnPropertyChanged(nameof(DetailsStoreImagePath));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(IsSteamGame));
        OnPropertyChanged(nameof(IsManualGame));
        OnPropertyChanged(nameof(DetailsSourceText));
        OnPropertyChanged(nameof(DetailsRatingLabel));
        OnPropertyChanged(nameof(DetailsRatingDescription));
        OnPropertyChanged(nameof(DetailsGenreText));
        OnPropertyChanged(nameof(DetailsMultiplayerText));
        OnPropertyChanged(nameof(DetailsCoOpText));
        OnPropertyChanged(nameof(DetailsPlaytimeText));
        OnPropertyChanged(nameof(DetailsReviewStarsText));
        OnPropertyChanged(nameof(DetailsReviewCountText));
    }

    private static string BuildTileTitle(string title)
    {
        return string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : title.Trim();
    }
}
