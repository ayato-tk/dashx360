using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XboxMetroLauncher.Services;

namespace XboxMetroLauncher.Themes;

public sealed class GameCoverImageSourceConverter : IMultiValueConverter
{
	private const double CoverBoxRatio = 94.0 / 111.0;

	public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	{
		BitmapSource bitmapSource = GameCoverBrushConverter.LoadPreparedImage((values.Length == 0) ? null : values[0]?.ToString());
		if (bitmapSource == null)
		{
			return null;
		}
		string a = ((values.Length <= 1) ? "Auto" : values[1]?.ToString());
		string path = ((values.Length == 0) ? null : values[0]?.ToString());
		if (string.Equals(a, "Fit", StringComparison.OrdinalIgnoreCase))
		{
			return PadToCoverAspect(bitmapSource, path);
		}
		return CropToCoverAspect(bitmapSource, path);
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		return targetTypes.Select((Type _) => Binding.DoNothing).ToArray();
	}

	private static ImageSource CropToCoverAspect(BitmapSource image, string? path)
	{
		return ImageCacheService.GetOrCreate(CreateDerivedKey("cover-crop", path, "default"), path, () => CropToCoverAspectCore(image)) ?? image;
	}

	private static BitmapSource? CropToCoverAspectCore(BitmapSource image)
	{
		int pixelWidth = image.PixelWidth;
		int pixelHeight = image.PixelHeight;
		if (pixelWidth <= 0 || pixelHeight <= 0)
		{
			return image;
		}
		double num = (double)pixelWidth / (double)pixelHeight;
		if (Math.Abs(num - 94.0 / 111.0) < 0.01)
		{
			return image;
		}
		int num2 = pixelWidth;
		int num3 = pixelHeight;
		if (num > 94.0 / 111.0)
		{
			num2 = Math.Max(1, (int)Math.Round((double)pixelHeight * (94.0 / 111.0)));
		}
		else
		{
			num3 = Math.Max(1, (int)Math.Round((double)pixelWidth / (94.0 / 111.0)));
		}
		int num4 = Math.Max(0, (pixelWidth - num2) / 2);
		int num5 = Math.Max(0, (pixelHeight - num3) / 2);
		num2 = Math.Min(num2, pixelWidth - num4);
		num3 = Math.Min(num3, pixelHeight - num5);
		try
		{
			CroppedBitmap croppedBitmap = new CroppedBitmap(image, new Int32Rect(num4, num5, num2, num3));
			((Freezable)croppedBitmap).Freeze();
			return croppedBitmap;
		}
		catch
		{
			return image;
		}
	}

	private static ImageSource PadToCoverAspect(BitmapSource image, string? path)
	{
		return ImageCacheService.GetOrCreate(CreateDerivedKey("cover-pad", path, "fit"), path, () => PadToCoverAspectCore(image)) ?? image;
	}

	private static BitmapSource? PadToCoverAspectCore(BitmapSource image)
	{
		int num = (int)Math.Round(940.0);
		if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
		{
			return image;
		}
		try
		{
			double num2 = Math.Min((double)num / (double)image.PixelWidth, 1110.0 / (double)image.PixelHeight);
			double num3 = (double)image.PixelWidth * num2;
			double num4 = (double)image.PixelHeight * num2;
			double num5 = ((double)num - num3) / 2.0;
			double num6 = (1110.0 - num4) / 2.0;
			DrawingVisual drawingVisual = new DrawingVisual();
			using (DrawingContext drawingContext = drawingVisual.RenderOpen())
			{
				drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(112, 112, 112)), null, new Rect(0.0, 0.0, (double)num, 1110.0));
				drawingContext.DrawImage(image, new Rect(num5, num6, num3, num4));
			}
			RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(num, 1110, 96.0, 96.0, PixelFormats.Pbgra32);
			renderTargetBitmap.Render(drawingVisual);
			((Freezable)renderTargetBitmap).Freeze();
			return renderTargetBitmap;
		}
		catch
		{
			return image;
		}
	}

	private static string CreateDerivedKey(string operation, string? path, string variant)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return operation + "|" + variant;
		}
		try
		{
			string text = AppPathResolver.Resolve(path);
			return File.Exists(text) ? $"{operation}|{variant}|{ImageCacheService.CreateFileToken(text)}" : $"{operation}|{variant}|{path}";
		}
		catch
		{
			return $"{operation}|{variant}|{path}";
		}
	}
}
