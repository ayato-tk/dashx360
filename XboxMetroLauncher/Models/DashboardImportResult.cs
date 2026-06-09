namespace XboxMetroLauncher.Models;

public sealed class DashboardImportResult
{
	public bool Success { get; init; }

	public string Message { get; init; } = string.Empty;

	public string? SafetyBackupPath { get; init; }
}
