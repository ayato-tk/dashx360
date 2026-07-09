namespace XboxMetroLauncher.ViewModels;

public sealed class GuideChatMessageItem
{
    public string AuthorName { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string TimeText { get; init; } = string.Empty;

    public bool IsOwn { get; init; }
}
