namespace XboxMetroLauncher.Services;

public interface IFilePickerService
{
    string? PickExecutable();
    string? PickFolder();
    string? PickImage(string? initialDirectory = null);
    string? PickJsonFile(string title = "Choose backup file");
    string? PickSaveJsonFile(string suggestedFileName = "DashX360_Backup.json", string title = "Save dashboard backup");
}
