using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace XboxMetroLauncher.Services;

internal static class ImageCacheService
{
	private sealed class ImageCacheEntry
	{
		public string CacheKey { get; init; } = string.Empty;

		public string SourcePath { get; init; } = string.Empty;

		public BitmapSource? Bitmap { get; init; }

		public int PixelWidth { get; init; }

		public int PixelHeight { get; init; }

		public long ApproximateDecodedBytes { get; init; }

		public DateTime LastAccessUtc { get; set; }

		public int RequestCount { get; set; }
	}

	internal readonly record struct ImageMetadata(int PixelWidth, int PixelHeight);

	internal readonly record struct ImageCacheSnapshot(int LoadedImageCount, int LoadedCoverCount, long ApproximateDecodedBytes, int LargestPixelWidth, int LargestPixelHeight);

	private const int MaxCacheEntries = 48;

	private static readonly object SyncRoot = new object();

	private static readonly Dictionary<string, ImageCacheEntry> Entries = new Dictionary<string, ImageCacheEntry>(StringComparer.OrdinalIgnoreCase);

	private static readonly LinkedList<string> LruKeys = new LinkedList<string>();

	private static readonly Dictionary<string, LinkedListNode<string>> LruNodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, ImageMetadata> MetadataCache = new Dictionary<string, ImageMetadata>(StringComparer.OrdinalIgnoreCase);

	public static BitmapSource? GetDecodedImage(string resolvedPath, int decodePixelWidth = 0, int decodePixelHeight = 0)
	{
		if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
		{
			return null;
		}
		return GetOrCreate($"decoded|{CreateFileToken(resolvedPath)}|w={decodePixelWidth}|h={decodePixelHeight}", resolvedPath, () => LoadBitmap(resolvedPath, decodePixelWidth, decodePixelHeight));
	}

	public static BitmapSource? GetFullImage(string resolvedPath)
	{
		return GetDecodedImage(resolvedPath);
	}

	public static BitmapSource? GetOrCreate(string cacheKey, string? sourcePath, Func<BitmapSource?> factory)
	{
		lock (SyncRoot)
		{
			if (Entries.TryGetValue(cacheKey, out ImageCacheEntry value))
			{
				value.RequestCount++;
				value.LastAccessUtc = DateTime.UtcNow;
				Touch(cacheKey);
				return value.Bitmap;
			}
		}
		BitmapSource bitmapSource = factory();
		if (bitmapSource != null && ((Freezable)bitmapSource).CanFreeze && !((Freezable)bitmapSource).IsFrozen)
		{
			((Freezable)bitmapSource).Freeze();
		}
		lock (SyncRoot)
		{
			if (Entries.TryGetValue(cacheKey, out ImageCacheEntry value2))
			{
				value2.RequestCount++;
				value2.LastAccessUtc = DateTime.UtcNow;
				Touch(cacheKey);
				return value2.Bitmap;
			}
			Entries[cacheKey] = new ImageCacheEntry
			{
				CacheKey = cacheKey,
				SourcePath = (sourcePath ?? string.Empty),
				Bitmap = bitmapSource,
				PixelWidth = (bitmapSource?.PixelWidth ?? 0),
				PixelHeight = (bitmapSource?.PixelHeight ?? 0),
				ApproximateDecodedBytes = EstimateDecodedBytes(bitmapSource),
				LastAccessUtc = DateTime.UtcNow,
				RequestCount = 1
			};
			AddToLru(cacheKey);
			TrimCache();
			return bitmapSource;
		}
	}

	public static string CreateFileToken(string resolvedPath)
	{
		FileInfo fileInfo = new FileInfo(resolvedPath);
		return $"{resolvedPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
	}

	public static ImageMetadata? GetMetadata(string resolvedPath)
	{
		if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
		{
			return null;
		}
		lock (SyncRoot)
		{
			if (MetadataCache.TryGetValue(resolvedPath, out var value))
			{
				return value;
			}
		}
		try
		{
			using FileStream bitmapStream = File.OpenRead(resolvedPath);
			BitmapFrame bitmapFrame = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames.FirstOrDefault();
			if (bitmapFrame == null)
			{
				return null;
			}
			ImageMetadata value2 = new ImageMetadata(bitmapFrame.PixelWidth, bitmapFrame.PixelHeight);
			lock (SyncRoot)
			{
				MetadataCache[resolvedPath] = value2;
			}
			return value2;
		}
		catch
		{
			return null;
		}
	}

	public static string GetDebugReport()
	{
		lock (SyncRoot)
		{
			return BuildDebugReportUnsafe();
		}
	}

	public static ImageCacheSnapshot GetSnapshot()
	{
		lock (SyncRoot)
		{
			List<ImageCacheEntry> list = Entries.Values.Where((ImageCacheEntry entry) => entry.Bitmap != null).ToList();
			return new ImageCacheSnapshot(list.Count, list.Count((ImageCacheEntry entry) => IsCoverPath(entry.SourcePath)), list.Sum((ImageCacheEntry entry) => entry.ApproximateDecodedBytes), (list.Count != 0) ? list.Max((ImageCacheEntry entry) => entry.PixelWidth) : 0, (list.Count != 0) ? list.Max((ImageCacheEntry entry) => entry.PixelHeight) : 0);
		}
	}

	private static BitmapSource? LoadBitmap(string resolvedPath, int decodePixelWidth, int decodePixelHeight)
	{
		try
		{
			using FileStream streamSource = File.OpenRead(resolvedPath);
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
			bitmapImage.StreamSource = streamSource;
			if (decodePixelWidth > 0)
			{
				bitmapImage.DecodePixelWidth = decodePixelWidth;
			}
			if (decodePixelHeight > 0)
			{
				bitmapImage.DecodePixelHeight = decodePixelHeight;
			}
			bitmapImage.EndInit();
			((Freezable)bitmapImage).Freeze();
			return bitmapImage;
		}
		catch
		{
			return null;
		}
	}

	private static long EstimateDecodedBytes(BitmapSource? bitmap)
	{
		if (bitmap != null)
		{
			return (long)bitmap.PixelWidth * (long)bitmap.PixelHeight * 4;
		}
		return 0L;
	}

	private static void AddToLru(string cacheKey)
	{
		if (LruNodes.ContainsKey(cacheKey))
		{
			Touch(cacheKey);
			return;
		}
		LinkedListNode<string> value = LruKeys.AddFirst(cacheKey);
		LruNodes[cacheKey] = value;
	}

	private static void Touch(string cacheKey)
	{
		if (LruNodes.TryGetValue(cacheKey, out LinkedListNode<string> value))
		{
			LruKeys.Remove(value);
			LruKeys.AddFirst(value);
		}
	}

	private static void TrimCache()
	{
		while (Entries.Count > 48 && LruKeys.Last != null)
		{
			string value = LruKeys.Last.Value;
			LruKeys.RemoveLast();
			LruNodes.Remove(value);
			Entries.Remove(value);
		}
	}

	private static string BuildDebugReportUnsafe()
	{
		List<ImageCacheEntry> list = Entries.Values.Where((ImageCacheEntry entry) => entry.Bitmap != null).ToList();
		ImageCacheEntry imageCacheEntry = list.OrderByDescending((ImageCacheEntry entry) => entry.ApproximateDecodedBytes).FirstOrDefault();
		var list2 = (from @group in list.Where((ImageCacheEntry entry) => !string.IsNullOrWhiteSpace(entry.SourcePath)).GroupBy<ImageCacheEntry, string>((ImageCacheEntry entry) => entry.SourcePath, StringComparer.OrdinalIgnoreCase)
			select new
			{
				Path = @group.Key,
				Variants = @group.Count(),
				Requests = @group.Sum((ImageCacheEntry entry) => entry.RequestCount)
			} into @group
			where @group.Variants > 1 || @group.Requests > 1
			orderby @group.Variants descending, @group.Requests descending
			select @group).ToList();
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("[IMAGE CACHE]");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
		handler.AppendLiteral("loaded image count: ");
		handler.AppendFormatted(list.Count);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("cache count: ");
		handler.AppendFormatted(Entries.Count);
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(26, 1, stringBuilder2);
		handler.AppendLiteral("approx decoded memory: ");
		handler.AppendFormatted((double)list.Sum((ImageCacheEntry entry) => entry.ApproximateDecodedBytes) / 1024.0 / 1024.0, "0.0");
		handler.AppendLiteral(" MB");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder2);
		handler.AppendLiteral("largest loaded image: ");
		handler.AppendFormatted((imageCacheEntry == null) ? "<none>" : $"{imageCacheEntry.PixelWidth}x{imageCacheEntry.PixelHeight} ({(double)imageCacheEntry.ApproximateDecodedBytes / 1024.0 / 1024.0:0.0} MB) from {imageCacheEntry.SourcePath}");
		stringBuilder6.AppendLine(ref handler);
		stringBuilder.AppendLine("duplicate image paths loaded:");
		if (list2.Count == 0)
		{
			stringBuilder.AppendLine("  <none>");
		}
		else
		{
			foreach (var item in list2)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder7 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(26, 3, stringBuilder2);
				handler.AppendLiteral("  ");
				handler.AppendFormatted(item.Path);
				handler.AppendLiteral(" | variants=");
				handler.AppendFormatted(item.Variants);
				handler.AppendLiteral(" | requests=");
				handler.AppendFormatted(item.Requests);
				stringBuilder7.AppendLine(ref handler);
			}
		}
		stringBuilder.AppendLine("cached images:");
		foreach (ImageCacheEntry item2 in list.OrderByDescending((ImageCacheEntry item) => item.ApproximateDecodedBytes))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 4, stringBuilder2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(item2.PixelWidth);
			handler.AppendLiteral("x");
			handler.AppendFormatted(item2.PixelHeight);
			handler.AppendLiteral(" | ");
			handler.AppendFormatted((double)item2.ApproximateDecodedBytes / 1024.0 / 1024.0, "0.0");
			handler.AppendLiteral(" MB | ");
			handler.AppendFormatted(item2.SourcePath);
			stringBuilder8.AppendLine(ref handler);
		}
		return stringBuilder.ToString();
	}

	private static bool IsCoverPath(string? path)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			if (!path.Contains("cover", StringComparison.OrdinalIgnoreCase) && !path.Contains("gameart", StringComparison.OrdinalIgnoreCase))
			{
				return path.Contains("steam", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}
}
