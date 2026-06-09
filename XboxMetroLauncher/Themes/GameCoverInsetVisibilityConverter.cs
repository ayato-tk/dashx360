using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace XboxMetroLauncher.Themes;

public sealed class GameCoverInsetVisibilityConverter : IMultiValueConverter
{
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	{
		string a = ((values.Length <= 1) ? "Auto" : values[1]?.ToString());
		BitmapSource bitmapSource = GameCoverBrushConverter.LoadPreparedImage((values.Length == 0) ? null : values[0]?.ToString());
		if (bitmapSource == null)
		{
			return Visibility.Collapsed;
		}
		if (string.Equals(a, "Fit", StringComparison.OrdinalIgnoreCase))
		{
			return Visibility.Visible;
		}
		return (!GameCoverBrushConverter.NeedsInset(bitmapSource)) ? Visibility.Collapsed : Visibility.Visible;
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		return targetTypes.Select((Type _) => Binding.DoNothing).ToArray();
	}
}
