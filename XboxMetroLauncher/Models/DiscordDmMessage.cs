namespace XboxMetroLauncher.Models;

public sealed class DiscordDmMessage
{
    public ulong MessageId { get; init; }

    public ulong AuthorId { get; init; }

    public string AuthorName { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public DateTimeOffset SentAt { get; init; }

    public bool IsFromCurrentUser { get; init; }
}
