using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class RegistryStartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "XboxMetroLauncher";

    public void SetLaunchOnStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (!enabled)
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                return;
            }

            var exePath = ResolveLauncherPath();
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return;
            }

            key.SetValue(AppName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            App.LogException(ex, "RegistryStartupRegistrationService.SetLaunchOnStartup");
        }
    }

    private static string ResolveLauncherPath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && Path.GetFileName(processPath).Equals("XboxMetroLauncher.exe", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        try
        {
            var modulePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(modulePath) && Path.GetFileName(modulePath).Equals("XboxMetroLauncher.exe", StringComparison.OrdinalIgnoreCase))
            {
                return modulePath;
            }
        }
        catch
        {
        }

        var appHostPath = Path.Combine(AppPaths.AppFolder, "XboxMetroLauncher.exe");
        return appHostPath;
    }
}
