using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using XboxMetroLauncher.Services;

namespace XboxMetroLauncher.Themes;

public sealed class StringToImageSourceConverter : IValueConverter
{
	public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		string text = value?.ToString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		try
		{
			text = AppPathResolver.Resolve(text);
			if (!File.Exists(text))
			{
				return null;
			}
			int decodePixelWidth = ResolveDecodeWidth(text, parameter?.ToString());
			return ImageCacheService.GetDecodedImage(text, decodePixelWidth);
		}
		catch
		{
			return null;
		}
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}

	private static int ResolveDecodeWidth(string path, string? parameter)
	{
		if (int.TryParse(parameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		string text = path.Replace('\\', '/').ToLowerInvariant();
		if (text.Contains("/profile/") || text.Contains("/friendpool/") || text.Contains("gamerpic") || text.Contains("avatar"))
		{
			return 96;
		}
		if (text.Contains("/background") || text.Contains("/boot/") || text.Contains("home screen"))
		{
			return 1280;
		}
		if (!text.Contains("/tiles/") && !text.Contains("/marketplace/") && !text.Contains("/misc/") && !text.Contains("cover"))
		{
			text.Contains("art");
		}
		return 320;
	}
}
