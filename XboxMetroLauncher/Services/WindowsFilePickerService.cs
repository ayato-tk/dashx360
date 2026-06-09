using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace XboxMetroLauncher.Services;

public sealed class WindowsFilePickerService : IFilePickerService
{
	public string? PickExecutable()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Choose game executable",
			Filter = "Windows executables (*.exe)|*.exe|All files (*.*)|*.*",
			CheckFileExists = true
		};
		if (!ShowDialog(openFileDialog))
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	public string? PickFolder()
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Choose a game library folder",
			Multiselect = false
		};
		Window window = Application.Current?.MainWindow;
		if (window == null)
		{
			if (openFolderDialog.ShowDialog() != true)
			{
				return null;
			}
			return openFolderDialog.FolderName;
		}
		if (openFolderDialog.ShowDialog(window) != true)
		{
			return null;
		}
		return openFolderDialog.FolderName;
	}

	public string? PickImage(string? initialDirectory = null)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Choose artwork",
			Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
			CheckFileExists = true
		};
		if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
		{
			openFileDialog.InitialDirectory = initialDirectory;
		}
		if (!ShowDialog(openFileDialog))
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	public string? PickJsonFile(string title = "Choose backup file")
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = title,
			Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
			CheckFileExists = true
		};
		if (!ShowDialog(openFileDialog))
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	public string? PickSaveJsonFile(string suggestedFileName = "DashX360_Backup.json", string title = "Save dashboard backup")
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = title,
			Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
			AddExtension = true,
			DefaultExt = ".json",
			OverwritePrompt = true,
			FileName = suggestedFileName
		};
		Window window = Application.Current?.MainWindow;
		if (window == null)
		{
			if (saveFileDialog.ShowDialog() != true)
			{
				return null;
			}
			return saveFileDialog.FileName;
		}
		if (saveFileDialog.ShowDialog(window) != true)
		{
			return null;
		}
		return saveFileDialog.FileName;
	}

	private static bool ShowDialog(OpenFileDialog dialog)
	{
		Window window = Application.Current?.MainWindow;
		if (window == null)
		{
			return dialog.ShowDialog() == true;
		}
		return dialog.ShowDialog(window) == true;
	}
}
