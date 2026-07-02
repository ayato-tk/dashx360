using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XboxMetroLauncher.Services;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Themes;

internal static class AppPathResolver
{
    private const string OldMetroRoot = @"C:\Metro";

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

        var oldRoot = Path.TrimEndingDirectorySeparator(OldMetroRoot);
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(oldRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(oldRoot, fullPath);
            foreach (var root in AppPaths.CandidateRoots())
            {
                var portablePath = Path.Combine(root, relative);
                if (File.Exists(portablePath))
                {
                    return portablePath;
                }
            }
        }

        return fullPath;
    }
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility visibility && visibility != Visibility.Visible;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = value?.ToString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var decodeWidth = ResolveDecodeWidth(path, parameter?.ToString());
            if (TryCreateRemoteUri(path, out var remoteUri))
            {
                return LoadRemoteImage(remoteUri, decodeWidth);
            }

            path = AppPathResolver.Resolve(path);

            if (!File.Exists(path))
            {
                return null;
            }

            return ImageCacheService.GetDecodedImage(path, decodePixelWidth: decodeWidth);
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static bool TryCreateRemoteUri(string path, out Uri uri)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out uri!)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static ImageSource? LoadRemoteImage(Uri uri, int decodeWidth)
    {
        var cacheKey = $"remote-image|{uri.AbsoluteUri}|w={decodeWidth}";
        return ImageCacheService.GetOrCreate(cacheKey, uri.AbsoluteUri, () =>
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = uri;

            if (decodeWidth > 0)
            {
                image.DecodePixelWidth = decodeWidth;
            }

            image.EndInit();
            image.Freeze();
            return image;
        });
    }

    private static int ResolveDecodeWidth(string path, string? parameter)
    {
        if (int.TryParse(parameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var explicitWidth))
        {
            return explicitWidth;
        }

        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();
        if (normalizedPath.Contains("/profile/")
            || normalizedPath.Contains("/friendpool/")
            || normalizedPath.Contains("gamerpic")
            || normalizedPath.Contains("avatar"))
        {
            return 96;
        }

        if (normalizedPath.Contains("/background")
            || normalizedPath.Contains("/boot/")
            || normalizedPath.Contains("home screen"))
        {
            return 1280;
        }

        if (normalizedPath.Contains("/tiles/")
            || normalizedPath.Contains("/marketplace/")
            || normalizedPath.Contains("/misc/")
            || normalizedPath.Contains("cover")
            || normalizedPath.Contains("art"))
        {
            return 320;
        }

        return 320;
    }
}

public sealed class GameCoverStretchConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var path = values.Length > 0 ? values[0]?.ToString() : string.Empty;
        var mode = values.Length > 1 ? values[1]?.ToString() : "Auto";

        return mode switch
        {
            "Fit" => Stretch.Uniform,
            "Stretch" => Stretch.UniformToFill,
            "Cover" => Stretch.UniformToFill,
            "Fill" => Stretch.UniformToFill,
            _ => ChooseAutoStretch(path)
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => targetTypes.Select(_ => Binding.DoNothing).ToArray();

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

            var normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (normalizedPath.Contains($"{Path.DirectorySeparatorChar}Assets{Path.DirectorySeparatorChar}GameArt{Path.DirectorySeparatorChar}Steam{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                return Stretch.UniformToFill;
            }

            var decoder = BitmapDecoder.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames.FirstOrDefault();
            if (frame is null)
            {
                return Stretch.UniformToFill;
            }

            var width = frame.PixelWidth;
            var height = frame.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                return Stretch.UniformToFill;
            }

            var ratio = width / (double)height;
            return ratio is < 0.52 or > 2.4 ? Stretch.Uniform : Stretch.UniformToFill;
        }
        catch
        {
            return Stretch.UniformToFill;
        }
    }
}

public sealed class GameCoverBrushConverter : IMultiValueConverter
{
    private const double CoverBoxRatio = 188d / 264d;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var image = LoadPreparedImage(values.Length > 0 ? values[0]?.ToString() : null);
        if (image is null)
        {
            return new SolidColorBrush(Color.FromRgb(153, 153, 153));
        }

        var brush = new ImageBrush(image)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TileMode = TileMode.None
        };

        ApplyManualCrop(brush, values);

        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => targetTypes.Select(_ => Binding.DoNothing).ToArray();

    internal static BitmapSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            path = AppPathResolver.Resolve(path);

            if (!File.Exists(path))
            {
                return null;
            }

            return ImageCacheService.GetDecodedImage(path, decodePixelWidth: 320);
        }
        catch
        {
            return null;
        }
    }

    internal static BitmapSource? LoadPreparedImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            path = AppPathResolver.Resolve(path);
            if (!File.Exists(path))
            {
                return null;
            }

            var fileToken = ImageCacheService.CreateFileToken(path);
            return ImageCacheService.GetOrCreate(
                $"cover-trim|{fileToken}",
                path,
                () => TrimOuterBackground(LoadImage(path)));
        }
        catch
        {
            return null;
        }
    }

    internal static BitmapSource? TrimOuterBackground(BitmapSource? source)
    {
        if (source is null || source.PixelWidth < 12 || source.PixelHeight < 12)
        {
            return source;
        }

        try
        {
            var bitmap = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);

            var background = GetPixel(pixels, stride, width - 1, 0);
            const int threshold = 24;
            var minimumColumnHits = Math.Max(3, (int)Math.Round(height * 0.02));
            var minimumRowHits = Math.Max(3, (int)Math.Round(width * 0.02));

            var left = FindLeft(pixels, stride, width, height, background, threshold, minimumColumnHits);
            var right = FindRight(pixels, stride, width, height, background, threshold, minimumColumnHits);
            var top = FindTop(pixels, stride, width, height, background, threshold, minimumRowHits);
            var bottom = FindBottom(pixels, stride, width, height, background, threshold, minimumRowHits);

            if (left >= right || top >= bottom)
            {
                return source;
            }

            var cropWidth = right - left + 1;
            var cropHeight = bottom - top + 1;
            if (cropWidth >= width - 2 && cropHeight >= height - 2)
            {
                return source;
            }

            var cropped = new CroppedBitmap(source, new Int32Rect(left, top, cropWidth, cropHeight));
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return source;
        }
    }

    internal static bool NeedsInset(BitmapSource image)
    {
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
        {
            return false;
        }

        var ratio = image.PixelWidth / (double)image.PixelHeight;
        return ratio > CoverBoxRatio * 1.22 || ratio < CoverBoxRatio * 0.78;
    }

    private static void ApplyManualCrop(ImageBrush brush, object[] values)
    {
        var zoom = ReadDouble(values, 2, 1);
        if (zoom <= 1.001)
        {
            return;
        }

        zoom = Math.Clamp(zoom, 1, 1.8);
        var offsetX = Math.Clamp(ReadDouble(values, 3, 0), -1, 1);
        var offsetY = Math.Clamp(ReadDouble(values, 4, 0), -1, 1);
        var width = 1 / zoom;
        var height = 1 / zoom;
        var maxX = (1 - width) / 2;
        var maxY = (1 - height) / 2;
        var left = Math.Clamp(0.5 - width / 2 + offsetX * maxX, 0, 1 - width);
        var top = Math.Clamp(0.5 - height / 2 + offsetY * maxY, 0, 1 - height);

        brush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
        brush.Viewbox = new Rect(left, top, width, height);
    }

    private static double ReadDouble(object[] values, int index, double fallback)
    {
        if (values.Length <= index || values[index] is null)
        {
            return fallback;
        }

        return values[index] is double number
            ? number
            : double.TryParse(values[index].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
    }

    private static (byte B, byte G, byte R, byte A) GetPixel(byte[] pixels, int stride, int x, int y)
    {
        var index = y * stride + x * 4;
        return (pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
    }

    private static bool IsDifferent((byte B, byte G, byte R, byte A) pixel, (byte B, byte G, byte R, byte A) background, int threshold)
        => Math.Abs(pixel.R - background.R) > threshold
           || Math.Abs(pixel.G - background.G) > threshold
           || Math.Abs(pixel.B - background.B) > threshold
           || Math.Abs(pixel.A - background.A) > threshold;

    private static int FindLeft(byte[] pixels, int stride, int width, int height, (byte B, byte G, byte R, byte A) background, int threshold, int minimumHits)
    {
        for (var x = 0; x < width; x++)
        {
            var hits = 0;
            for (var y = 0; y < height; y++)
            {
                if (IsDifferent(GetPixel(pixels, stride, x, y), background, threshold) && ++hits >= minimumHits)
                {
                    return x;
                }
            }
        }

        return 0;
    }

    private static int FindRight(byte[] pixels, int stride, int width, int height, (byte B, byte G, byte R, byte A) background, int threshold, int minimumHits)
    {
        for (var x = width - 1; x >= 0; x--)
        {
            var hits = 0;
            for (var y = 0; y < height; y++)
            {
                if (IsDifferent(GetPixel(pixels, stride, x, y), background, threshold) && ++hits >= minimumHits)
                {
                    return x;
                }
            }
        }

        return width - 1;
    }

    private static int FindTop(byte[] pixels, int stride, int width, int height, (byte B, byte G, byte R, byte A) background, int threshold, int minimumHits)
    {
        for (var y = 0; y < height; y++)
        {
            var hits = 0;
            for (var x = 0; x < width; x++)
            {
                if (IsDifferent(GetPixel(pixels, stride, x, y), background, threshold) && ++hits >= minimumHits)
                {
                    return y;
                }
            }
        }

        return 0;
    }

    private static int FindBottom(byte[] pixels, int stride, int width, int height, (byte B, byte G, byte R, byte A) background, int threshold, int minimumHits)
    {
        for (var y = height - 1; y >= 0; y--)
        {
            var hits = 0;
            for (var x = 0; x < width; x++)
            {
                if (IsDifferent(GetPixel(pixels, stride, x, y), background, threshold) && ++hits >= minimumHits)
                {
                    return y;
                }
            }
        }

        return height - 1;
    }
}

public sealed class GameCoverImageSourceConverter : IMultiValueConverter
{
    private const double CoverBoxRatio = 188d / 222d;

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var image = GameCoverBrushConverter.LoadPreparedImage(values.Length > 0 ? values[0]?.ToString() : null);
        if (image is null)
        {
            return null;
        }

        var mode = values.Length > 1 ? values[1]?.ToString() : "Auto";
        var path = values.Length > 0 ? values[0]?.ToString() : null;
        if (string.Equals(mode, "Fit", StringComparison.OrdinalIgnoreCase))
        {
            return PadToCoverAspect(image, path);
        }

        return CropToCoverAspect(image, path);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => targetTypes.Select(_ => Binding.DoNothing).ToArray();

    private static ImageSource CropToCoverAspect(BitmapSource image, string? path)
    {
        var cacheKey = CreateDerivedKey("cover-crop", path, "default");
        var cached = ImageCacheService.GetOrCreate(cacheKey, path, () => CropToCoverAspectCore(image));
        return cached ?? image;
    }

    private static BitmapSource? CropToCoverAspectCore(BitmapSource image)
    {
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return image;
        }

        var sourceRatio = width / (double)height;
        if (Math.Abs(sourceRatio - CoverBoxRatio) < 0.01)
        {
            return image;
        }

        var cropWidth = width;
        var cropHeight = height;
        if (sourceRatio > CoverBoxRatio)
        {
            cropWidth = Math.Max(1, (int)Math.Round(height * CoverBoxRatio));
        }
        else
        {
            cropHeight = Math.Max(1, (int)Math.Round(width / CoverBoxRatio));
        }

        var cropX = Math.Max(0, (width - cropWidth) / 2);
        var cropY = Math.Max(0, (height - cropHeight) / 2);
        cropWidth = Math.Min(cropWidth, width - cropX);
        cropHeight = Math.Min(cropHeight, height - cropY);

        try
        {
            var cropped = new CroppedBitmap(image, new Int32Rect(cropX, cropY, cropWidth, cropHeight));
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return image;
        }
    }

    private static ImageSource PadToCoverAspect(BitmapSource image, string? path)
    {
        var cacheKey = CreateDerivedKey("cover-pad", path, "fit");
        var cached = ImageCacheService.GetOrCreate(cacheKey, path, () => PadToCoverAspectCore(image));
        return cached ?? image;
    }

    private static BitmapSource? PadToCoverAspectCore(BitmapSource image)
    {
        const int targetHeight = 1110;
        var targetWidth = (int)Math.Round(targetHeight * CoverBoxRatio);

        if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
        {
            return image;
        }

        try
        {
            var scale = Math.Min(targetWidth / (double)image.PixelWidth, targetHeight / (double)image.PixelHeight);
            var drawWidth = image.PixelWidth * scale;
            var drawHeight = image.PixelHeight * scale;
            var x = (targetWidth - drawWidth) / 2;
            var y = (targetHeight - drawHeight) / 2;

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(112, 112, 112)), null, new Rect(0, 0, targetWidth, targetHeight));
                context.DrawImage(image, new Rect(x, y, drawWidth, drawHeight));
            }

            var rendered = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);
            rendered.Freeze();
            return rendered;
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
            return $"{operation}|{variant}";
        }

        try
        {
            var resolved = AppPathResolver.Resolve(path);
            return File.Exists(resolved)
                ? $"{operation}|{variant}|{ImageCacheService.CreateFileToken(resolved)}"
                : $"{operation}|{variant}|{path}";
        }
        catch
        {
            return $"{operation}|{variant}|{path}";
        }
    }
}

public sealed class GameCoverInsetVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var mode = values.Length > 1 ? values[1]?.ToString() : "Auto";
        var image = GameCoverBrushConverter.LoadPreparedImage(values.Length > 0 ? values[0]?.ToString() : null);
        if (image is null)
        {
            return Visibility.Collapsed;
        }

        if (string.Equals(mode, "Fit", StringComparison.OrdinalIgnoreCase))
        {
            return Visibility.Visible;
        }

        return GameCoverBrushConverter.NeedsInset(image) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => targetTypes.Select(_ => Binding.DoNothing).ToArray();
}

public sealed class CropImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = value?.ToString();
        var crop = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(crop))
        {
            return null;
        }

        try
        {
            path = AppPathResolver.Resolve(path);

            if (!File.Exists(path))
            {
                return null;
            }

            var parts = crop.Split([',', ';']).Select(part => int.Parse(part.Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (parts.Length != 4)
            {
                return null;
            }

            var sourceMetadata = ImageCacheService.GetMetadata(path);
            if (sourceMetadata is null || sourceMetadata.Value.PixelWidth <= 0 || sourceMetadata.Value.PixelHeight <= 0)
            {
                return null;
            }

            var sourceRect = new Int32Rect(parts[0], parts[1], parts[2], parts[3]);
            var decodeWidth = Math.Clamp(sourceRect.Width, 96, 640);
            var bitmap = ImageCacheService.GetDecodedImage(path, decodePixelWidth: decodeWidth);
            if (bitmap is null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                return null;
            }

            var scaleX = bitmap.PixelWidth / (double)sourceMetadata.Value.PixelWidth;
            var scaleY = bitmap.PixelHeight / (double)sourceMetadata.Value.PixelHeight;
            var rect = new Int32Rect(
                Math.Clamp((int)Math.Round(sourceRect.X * scaleX), 0, Math.Max(0, bitmap.PixelWidth - 1)),
                Math.Clamp((int)Math.Round(sourceRect.Y * scaleY), 0, Math.Max(0, bitmap.PixelHeight - 1)),
                Math.Max(1, Math.Min(bitmap.PixelWidth, (int)Math.Round(sourceRect.Width * scaleX))),
                Math.Max(1, Math.Min(bitmap.PixelHeight, (int)Math.Round(sourceRect.Height * scaleY))));

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

            var fileToken = ImageCacheService.CreateFileToken(path);
            return ImageCacheService.GetOrCreate(
                $"crop|{fileToken}|{rect.X},{rect.Y},{rect.Width},{rect.Height}",
                path,
                () =>
                {
                    var cropped = new CroppedBitmap(bitmap, rect);
                    cropped.Freeze();
                    return cropped;
                });
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
