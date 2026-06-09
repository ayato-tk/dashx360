using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using XboxMetroLauncher.Services;

namespace XboxMetroLauncher.Themes;

public sealed class CropImageSourceConverter : IValueConverter
{
	public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		string text = value?.ToString();
		string text2 = parameter?.ToString();
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text2))
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
			int[] array = (from part in text2.Split(',', ';')
				select int.Parse(part.Trim(), CultureInfo.InvariantCulture)).ToArray();
			if (array.Length != 4)
			{
				return null;
			}
			ImageCacheService.ImageMetadata? metadata = ImageCacheService.GetMetadata(text);
			if (!metadata.HasValue || metadata.Value.PixelWidth <= 0 || metadata.Value.PixelHeight <= 0)
			{
				return null;
			}
			Int32Rect val = default(Int32Rect);
			val = new Int32Rect(array[0], array[1], array[2], array[3]);
			int decodePixelWidth = Math.Clamp(val.Width, 96, 640);
			BitmapSource bitmap = ImageCacheService.GetDecodedImage(text, decodePixelWidth);
			if (bitmap == null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
			{
				return null;
			}
			double num = (double)bitmap.PixelWidth / (double)metadata.Value.PixelWidth;
			double num2 = (double)bitmap.PixelHeight / (double)metadata.Value.PixelHeight;
			Int32Rect rect = new Int32Rect(Math.Clamp((int)Math.Round((double)val.X * num), 0, Math.Max(0, bitmap.PixelWidth - 1)), Math.Clamp((int)Math.Round((double)val.Y * num2), 0, Math.Max(0, bitmap.PixelHeight - 1)), Math.Max(1, Math.Min(bitmap.PixelWidth, (int)Math.Round((double)val.Width * num))), Math.Max(1, Math.Min(bitmap.PixelHeight, (int)Math.Round((double)val.Height * num2))));
			if (rect.X + rect.Width > bitmap.PixelWidth)
			{
				rect.Width = bitmap.PixelWidth - rect.X;
			}
			if (rect.Y + rect.Height > bitmap.PixelHeight)
			{
				rect.Height = bitmap.PixelHeight - rect.Y;
			}
			if (rect.Width <= 0 || rect.Height <= 0)
			{
				return null;
			}
			string value2 = ImageCacheService.CreateFileToken(text);
			return ImageCacheService.GetOrCreate($"crop|{value2}|{rect.X},{rect.Y},{rect.Width},{rect.Height}", text, delegate
			{
				CroppedBitmap croppedBitmap = new CroppedBitmap(bitmap, rect);
				((Freezable)croppedBitmap).Freeze();
				return croppedBitmap;
			});
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
}
