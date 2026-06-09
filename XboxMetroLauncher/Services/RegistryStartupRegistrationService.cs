using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class RegistryStartupRegistrationService : IStartupRegistrationService
{
	private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string AppName = "XboxMetroLauncher";

	public void SetLaunchOnStartup(bool enabled)
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey == null)
			{
				return;
			}
			if (!enabled)
			{
				registryKey.DeleteValue("XboxMetroLauncher", throwOnMissingValue: false);
				return;
			}
			string text = ResolveLauncherPath();
			if (!string.IsNullOrWhiteSpace(text) && File.Exists(text))
			{
				registryKey.SetValue("XboxMetroLauncher", "\"" + text + "\"", RegistryValueKind.String);
			}
		}
		catch (Exception exception)
		{
			App.LogException(exception, "RegistryStartupRegistrationService.SetLaunchOnStartup");
		}
	}

	private static string ResolveLauncherPath()
	{
		string processPath = Environment.ProcessPath;
		if (!string.IsNullOrWhiteSpace(processPath) && Path.GetFileName(processPath).Equals("XboxMetroLauncher.exe", StringComparison.OrdinalIgnoreCase))
		{
			return processPath;
		}
		try
		{
			string text = Process.GetCurrentProcess().MainModule?.FileName;
			if (!string.IsNullOrWhiteSpace(text) && Path.GetFileName(text).Equals("XboxMetroLauncher.exe", StringComparison.OrdinalIgnoreCase))
			{
				return text;
			}
		}
		catch
		{
		}
		return Path.Combine(AppPaths.AppFolder, "XboxMetroLauncher.exe");
	}
}
