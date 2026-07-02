namespace XboxMetroLauncher.ViewModels;

public sealed class GuideAchievementItem : ObservableObject
{
    private bool _isSelected;

    public required string Title { get; init; }

    public string Description { get; init; } = string.Empty;

    public bool Achieved { get; init; }

    public string StatusText { get; init; } = string.Empty;

    public long UnlockTimeUnix { get; init; }

    public string IconGlyph => Achieved ? "\uE7C1" : "\uE72E";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
