namespace XboxMetroLauncher.Services;

public sealed class RunningGameCloseResult
{
    public required bool Success { get; init; }

    public required bool RequiresForceConfirmation { get; init; }

    public required string Message { get; init; }
}
