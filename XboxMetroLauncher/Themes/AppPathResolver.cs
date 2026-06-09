using System;
using System.IO;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Themes;

internal static class AppPathResolver
{
	private const string OldMetroRoot = "C:\\Metro";

	public static string Resolve(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return path;
		}
		if (!Path.IsPathRooted(path))
		{
			return AppPaths.ResolvePath(path);
		}
		string text = Path.TrimEndingDirectorySeparator("C:\\Metro");
		string fullPath = Path.GetFullPath(path);
		if (fullPath.StartsWith(text + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			string relativePath = Path.GetRelativePath(text, fullPath);
			foreach (string item in AppPaths.CandidateRoots())
			{
				string text2 = Path.Combine(item, relativePath);
				if (File.Exists(text2))
				{
					return text2;
				}
			}
		}
		return fullPath;
	}
}
