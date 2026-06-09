using System;
using System.Collections.Generic;
using System.IO;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

internal static class ProfileImagePool
{
	private static readonly string[] PoolFileNames = new string[8] { "2000c.png", "20002.png", "20003.png", "20006.png", "20007.png", "20008.png", "20009.png", "2000a.png" };

	private static readonly object SyncRoot = new object();

	private static readonly Random Random = new Random();

	public static string GetDefaultAvatarPath()
	{
		string text = AppPaths.FindFile(Path.Combine("Assets", "Profile", "profilepicture.jpg"));
		if (File.Exists(text))
		{
			return text;
		}
		string text2 = AppPaths.FindFile(Path.Combine("Assets", "Art", "profilepicture.jpg"));
		if (!File.Exists(text2))
		{
			return text;
		}
		return text2;
	}

	public static bool NeedsAssignedPoolImage(string? currentPath)
	{
		if (string.IsNullOrWhiteSpace(currentPath))
		{
			return true;
		}
		string path = AppPaths.ResolvePath(currentPath);
		if (!File.Exists(path))
		{
			return true;
		}
		string fullPath = Path.GetFullPath(path);
		string fullPath2 = Path.GetFullPath(GetDefaultAvatarPath());
		if (string.Equals(fullPath, fullPath2, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return fullPath.EndsWith(Path.Combine("Assets", "Art", "profilepicture.jpg"), StringComparison.OrdinalIgnoreCase);
	}

	public static string ResolveAssignedAvatarPath(string? currentPath)
	{
		if (!NeedsAssignedPoolImage(currentPath))
		{
			return AppPaths.ResolvePath(currentPath);
		}
		return GetRandomPoolAvatarPath();
	}

	public static string GetRandomPoolAvatarPath()
	{
		List<string> availablePoolPaths = GetAvailablePoolPaths();
		if (availablePoolPaths.Count == 0)
		{
			return GetDefaultAvatarPath();
		}
		lock (SyncRoot)
		{
			return availablePoolPaths[Random.Next(availablePoolPaths.Count)];
		}
	}

	private static List<string> GetAvailablePoolPaths()
	{
		string path = AppPaths.FindFolder(Path.Combine("Assets", "Profile", "FriendPool"));
		List<string> list = new List<string>();
		string[] poolFileNames = PoolFileNames;
		foreach (string path2 in poolFileNames)
		{
			string text = Path.Combine(path, path2);
			if (File.Exists(text))
			{
				list.Add(text);
			}
		}
		return list;
	}
}
