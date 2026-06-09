using System.Windows.Media;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.ViewModels;

public sealed class GameCardViewModel : ObservableObject
{
	public GameMetadata Game { get; }

	public string Title => Game.Title;

	public string TileTitle => BuildTileTitle(Game.Title);

	public string Subtitle
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Game.Genre))
			{
				return Game.Platform + " - " + Game.Genre;
			}
			return Game.Platform;
		}
	}

	public string CoverArtPath => Game.CoverArtPath;

	public double CoverZoom => Game.CoverZoom;

	public double CoverOffsetX => Game.CoverOffsetX;

	public double CoverOffsetY => Game.CoverOffsetY;

	public string BackgroundArtPath => Game.BackgroundArtPath;

	public bool IsFavorite => Game.IsFavorite;

	public Brush AccentBrush { get; }

	public GameCardViewModel(GameMetadata game, Brush accentBrush)
	{
		Game = game;
		AccentBrush = accentBrush;
	}

	public void Refresh()
	{
		OnPropertyChanged("Title");
		OnPropertyChanged("TileTitle");
		OnPropertyChanged("Subtitle");
		OnPropertyChanged("CoverArtPath");
		OnPropertyChanged("CoverZoom");
		OnPropertyChanged("CoverOffsetX");
		OnPropertyChanged("CoverOffsetY");
		OnPropertyChanged("BackgroundArtPath");
		OnPropertyChanged("IsFavorite");
	}

	private static string BuildTileTitle(string title)
	{
		if (!string.IsNullOrWhiteSpace(title))
		{
			return title.Trim();
		}
		return string.Empty;
	}
}
