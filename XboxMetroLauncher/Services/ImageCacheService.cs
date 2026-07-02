using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace XboxMetroLauncher.Services;

internal static class ImageCacheService
{
    private const int MaxCacheEntries = 48;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, ImageCacheEntry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> LruKeys = [];
    private static readonly Dictionary<string, LinkedListNode<string>> LruNodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageMetadata> MetadataCache = new(StringComparer.OrdinalIgnoreCase);
    public static BitmapSource? GetDecodedImage(string resolvedPath, int decodePixelWidth = 0, int decodePixelHeight = 0)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            return null;
        }

        var cacheKey = $"decoded|{CreateFileToken(resolvedPath)}|w={decodePixelWidth}|h={decodePixelHeight}";
        return GetOrCreate(cacheKey, resolvedPath, () => LoadBitmap(resolvedPath, decodePixelWidth, decodePixelHeight));
    }

    public static BitmapSource? GetFullImage(string resolvedPath)
        => GetDecodedImage(resolvedPath);

    public static BitmapSource? GetOrCreate(string cacheKey, string? sourcePath, Func<BitmapSource?> factory)
    {
        lock (SyncRoot)
        {
            if (Entries.TryGetValue(cacheKey, out var cached))
            {
                cached.RequestCount++;
                cached.LastAccessUtc = DateTime.UtcNow;
                Touch(cacheKey);
                return cached.Bitmap;
            }
        }

        var created = factory();
        if (created is not null && created.CanFreeze && !created.IsFrozen)
        {
            created.Freeze();
        }

        lock (SyncRoot)
        {
            if (Entries.TryGetValue(cacheKey, out var existing))
            {
                existing.RequestCount++;
                existing.LastAccessUtc = DateTime.UtcNow;
                Touch(cacheKey);
                return existing.Bitmap;
            }

            Entries[cacheKey] = new ImageCacheEntry
            {
                CacheKey = cacheKey,
                SourcePath = sourcePath ?? string.Empty,
                Bitmap = created,
                PixelWidth = created?.PixelWidth ?? 0,
                PixelHeight = created?.PixelHeight ?? 0,
                ApproximateDecodedBytes = EstimateDecodedBytes(created),
                LastAccessUtc = DateTime.UtcNow,
                RequestCount = 1
            };

            AddToLru(cacheKey);
            TrimCache();
            return created;
        }
    }

    public static string CreateFileToken(string resolvedPath)
    {
        var info = new FileInfo(resolvedPath);
        return $"{resolvedPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    public static ImageMetadata? GetMetadata(string resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            return null;
        }

        lock (SyncRoot)
        {
            if (MetadataCache.TryGetValue(resolvedPath, out var cached))
            {
                return cached;
            }
        }

        try
        {
            using var stream = File.OpenRead(resolvedPath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames.FirstOrDefault();
            if (frame is null)
            {
                return null;
            }

            var metadata = new ImageMetadata(frame.PixelWidth, frame.PixelHeight);
            lock (SyncRoot)
            {
                MetadataCache[resolvedPath] = metadata;
            }

            return metadata;
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
            var activeEntries = Entries.Values
                .Where(entry => entry.Bitmap is not null)
                .ToList();

            return new ImageCacheSnapshot(
                activeEntries.Count,
                activeEntries.Count(entry => IsCoverPath(entry.SourcePath)),
                activeEntries.Sum(entry => entry.ApproximateDecodedBytes),
                activeEntries.Count == 0 ? 0 : activeEntries.Max(entry => entry.PixelWidth),
                activeEntries.Count == 0 ? 0 : activeEntries.Max(entry => entry.PixelHeight));
        }
    }

    private static BitmapSource? LoadBitmap(string resolvedPath, int decodePixelWidth, int decodePixelHeight)
    {
        try
        {
            using var stream = File.OpenRead(resolvedPath);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;

            if (decodePixelWidth > 0)
            {
                image.DecodePixelWidth = decodePixelWidth;
            }

            if (decodePixelHeight > 0)
            {
                image.DecodePixelHeight = decodePixelHeight;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static long EstimateDecodedBytes(BitmapSource? bitmap)
        => bitmap is null ? 0 : (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;

    private static void AddToLru(string cacheKey)
    {
        if (LruNodes.ContainsKey(cacheKey))
        {
            Touch(cacheKey);
            return;
        }

        var node = LruKeys.AddFirst(cacheKey);
        LruNodes[cacheKey] = node;
    }

    private static void Touch(string cacheKey)
    {
        if (!LruNodes.TryGetValue(cacheKey, out var node))
        {
            return;
        }

        LruKeys.Remove(node);
        LruKeys.AddFirst(node);
    }

    private static void TrimCache()
    {
        while (Entries.Count > MaxCacheEntries && LruKeys.Last is not null)
        {
            var lastKey = LruKeys.Last.Value;
            LruKeys.RemoveLast();
            LruNodes.Remove(lastKey);
            Entries.Remove(lastKey);
        }
    }

    private static string BuildDebugReportUnsafe()
    {
        var activeEntries = Entries.Values
            .Where(entry => entry.Bitmap is not null)
            .ToList();

        var largest = activeEntries
            .OrderByDescending(entry => entry.ApproximateDecodedBytes)
            .FirstOrDefault();

        var duplicatePaths = activeEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.SourcePath))
            .GroupBy(entry => entry.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Path = group.Key,
                Variants = group.Count(),
                Requests = group.Sum(entry => entry.RequestCount)
            })
            .Where(group => group.Variants > 1 || group.Requests > 1)
            .OrderByDescending(group => group.Variants)
            .ThenByDescending(group => group.Requests)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("[IMAGE CACHE]");
        builder.AppendLine($"loaded image count: {activeEntries.Count}");
        builder.AppendLine($"cache count: {Entries.Count}");
        builder.AppendLine($"approx decoded memory: {activeEntries.Sum(entry => entry.ApproximateDecodedBytes) / 1024d / 1024d:0.0} MB");
        builder.AppendLine($"largest loaded image: {(largest is null ? "<none>" : $"{largest.PixelWidth}x{largest.PixelHeight} ({largest.ApproximateDecodedBytes / 1024d / 1024d:0.0} MB) from {largest.SourcePath}")}");
        builder.AppendLine("duplicate image paths loaded:");
        if (duplicatePaths.Count == 0)
        {
            builder.AppendLine("  <none>");
        }
        else
        {
            foreach (var duplicate in duplicatePaths)
            {
                builder.AppendLine($"  {duplicate.Path} | variants={duplicate.Variants} | requests={duplicate.Requests}");
            }
        }

        builder.AppendLine("cached images:");
        foreach (var entry in activeEntries.OrderByDescending(item => item.ApproximateDecodedBytes))
        {
            builder.AppendLine($"  {entry.PixelWidth}x{entry.PixelHeight} | {entry.ApproximateDecodedBytes / 1024d / 1024d:0.0} MB | {entry.SourcePath}");
        }

        return builder.ToString();
    }

    private static bool IsCoverPath(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && (path.Contains("cover", StringComparison.OrdinalIgnoreCase)
               || path.Contains("gameart", StringComparison.OrdinalIgnoreCase)
               || path.Contains("steam", StringComparison.OrdinalIgnoreCase));

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
}
