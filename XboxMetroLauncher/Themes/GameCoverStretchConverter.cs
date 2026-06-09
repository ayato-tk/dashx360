using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XboxMetroLauncher.Themes;

public sealed class GameCoverStretchConverter : IMultiValueConverter
{
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	{
		string path = ((values.Length == 0) ? string.Empty : values[0]?.ToString());
		return ((values.Length <= 1) ? "Auto" : values[1]?.ToString()) switch
		{
			"Fit" => Stretch.Uniform, 
			"Stretch" => Stretch.UniformToFill, 
			"Cover" => Stretch.UniformToFill, 
			"Fill" => Stretch.UniformToFill, 
			_ => ChooseAutoStretch(path), 
		};
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		return targetTypes.Select((Type _) => Binding.DoNothing).ToArray();
	}

	private static Stretch ChooseAutoStretch(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return Stretch.UniformToFill;
		}
		try
		{
			path = AppPathResolver.Resolve(path);
			if (!File.Exists(path))
			{
				return Stretch.UniformToFill;
			}
			if (path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Contains($"{Path.DirectorySeparatorChar}Assets{Path.DirectorySeparatorChar}GameArt{Path.DirectorySeparatorChar}Steam{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
			{
				return Stretch.UniformToFill;
			}
			BitmapFrame bitmapFrame = BitmapDecoder.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames.FirstOrDefault();
			if (bitmapFrame == null)
			{
				return Stretch.UniformToFill;
			}
			int pixelWidth = bitmapFrame.PixelWidth;
			int pixelHeight = bitmapFrame.PixelHeight;
			if (pixelWidth <= 0 || pixelHeight <= 0)
			{
				return Stretch.UniformToFill;
			}
			double num = (double)pixelWidth / (double)pixelHeight;
			bool flag = (num < 0.52 || num > 2.4);
			return flag ? Stretch.Uniform : Stretch.UniformToFill;
		}
		catch
		{
			return Stretch.UniformToFill;
		}
	}
}
