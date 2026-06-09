using System;
using System.Security.Cryptography;
using System.Text;

namespace XboxMetroLauncher.Utilities;

internal static class SecureStringStorage
{
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("XboxMetroLauncher.DiscordSession");

	public static string Protect(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
	}

	public static string Unprotect(string? protectedValue)
	{
		if (string.IsNullOrWhiteSpace(protectedValue))
		{
			return string.Empty;
		}
		try
		{
			byte[] bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(bytes);
		}
		catch
		{
			return string.Empty;
		}
	}
}
