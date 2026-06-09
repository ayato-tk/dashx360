using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace XboxMetroLauncher.Utilities;

internal static class AppPaths
{
	private static readonly string[] LegacyUserDataFolders = new string[3]
	{
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XboxMetroLauncher"),
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XboxMetroLauncher"),
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "XboxMetroLauncherData")
	};

	public static string AppFolder
	{
		get
		{
			string text = Environment.ProcessPath;
			if (string.IsNullOrWhiteSpace(text))
			{
				try
				{
					text = Process.GetCurrentProcess().MainModule?.FileName;
				}
				catch
				{
					text = null;
				}
			}
			string text2 = (string.IsNullOrWhiteSpace(text) ? AppContext.BaseDirectory : Path.GetDirectoryName(text));
			return Path.TrimEndingDirectorySeparator(string.IsNullOrWhiteSpace(text2) ? AppContext.BaseDirectory : text2);
		}
	}

	public static string UserDataFolder => EnsureFolder(Path.Combine(AppFolder, "UserData"));

	public static string LogsFolder => EnsureFolder(Path.Combine(AppFolder, "Logs"));

	public static IReadOnlyList<string> LegacyDataRoots()
	{
		return LegacyUserDataFolders.Where((string path) => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public static IReadOnlyList<string> CandidateRoots()
	{
		List<string> list = new List<string>();
		AddRoot(list, AppFolder);
		AddRoot(list, Environment.CurrentDirectory);
		AddRoot(list, AppContext.BaseDirectory);
		return list;
	}

	public static string FindFolder(string relativePath, Func<string, bool>? predicate = null)
	{
		foreach (string item in CandidateRoots())
		{
			string text = Path.Combine(item, relativePath);
			if (Directory.Exists(text) && (predicate == null || predicate(text)))
			{
				return text;
			}
		}
		return Path.Combine(AppFolder, relativePath);
	}

	public static string FindFile(string relativePath)
	{
		foreach (string item in CandidateRoots())
		{
			string text = Path.Combine(item, relativePath);
			if (File.Exists(text))
			{
				return text;
			}
		}
		return Path.Combine(AppFolder, relativePath);
	}

	public static string ResolvePath(string path)
	{
		if (!Path.IsPathRooted(path))
		{
			return Path.Combine(AppFolder, path);
		}
		return path;
	}

	private static void AddRoot(List<string> roots, string? path)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
			if (!roots.Any((string root) => string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase)))
			{
				roots.Add(fullPath);
			}
		}
	}

	private static string EnsureFolder(string path)
	{
		Directory.CreateDirectory(path);
		return path;
	}
}
