namespace XboxMetroLauncher.ViewModels;

public sealed class GuideAchievementGameItem : ObservableObject
{
    private bool _isSelected;
    private int _unlockedCount;
    private int _totalCount;
    private string _statusText = "Steam achievements";

    public required string Title { get; init; }

    public required string SteamAppId { get; init; }

    public string CoverArtPath { get; init; } = string.Empty;

    public int UnlockedCount
    {
        get => _unlockedCount;
        set
        {
            if (SetProperty(ref _unlockedCount, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string CountText => TotalCount > 0
        ? $"{UnlockedCount} of {TotalCount} Achievements"
        : StatusText;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
