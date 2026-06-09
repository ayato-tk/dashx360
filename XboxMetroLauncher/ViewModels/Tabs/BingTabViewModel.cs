using System.Collections.ObjectModel;
using System.Windows.Input;

namespace XboxMetroLauncher.ViewModels.Tabs;

public sealed class BingTabViewModel : DashboardTabViewModel
{
	public ObservableCollection<string> TrendingSearches { get; }

	public ICommand SubmitSearchCommand => base.Shell.SubmitSearchCommand;

	public ICommand UseTrendingSearchCommand => base.Shell.UseTrendingSearchCommand;

	public BingTabViewModel(DashboardViewModel shell)
		: base(shell, "bing", "bing")
	{
		TrendingSearches = new ObservableCollection<string> { "Halo Reach", "Forza Horizon", "Game Pass PC", "Local co-op games", "Xbox 360 dashboard" };
	}
}
