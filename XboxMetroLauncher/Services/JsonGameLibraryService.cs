using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class JsonGameLibraryService : IGameLibraryService
{
	private const string LibraryFileName = "library.json";

	private readonly IJsonStore _store;

	public JsonGameLibraryService(IJsonStore store)
	{
		_store = store;
	}

	public async Task<GameLibrary> LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		GameLibrary gameLibrary = await _store.ReadAsync<GameLibrary>("library.json", cancellationToken);
		if (gameLibrary != null)
		{
			return gameLibrary;
		}
		string path = AppPaths.FindFile(Path.Combine("Data", "library.seed.json"));
		if (File.Exists(path))
		{
			await using FileStream stream = File.OpenRead(path);
			GameLibrary seeded = await JsonSerializer.DeserializeAsync<GameLibrary>((Stream)stream, (JsonSerializerOptions?)null, cancellationToken);
			if (seeded != null)
			{
				await SaveAsync(seeded, cancellationToken);
				return seeded;
			}
		}
		return new GameLibrary();
	}

	public Task SaveAsync(GameLibrary library, CancellationToken cancellationToken = default(CancellationToken))
	{
		return _store.WriteAsync("library.json", library, cancellationToken);
	}

	public Task<IReadOnlyList<GameMetadata>> ScanFolderAsync(string folderPath, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!Directory.Exists(folderPath))
		{
			return Task.FromResult((IReadOnlyList<GameMetadata>)Array.Empty<GameMetadata>());
		}
		HashSet<string> ignoredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UnityCrashHandler64", "UnityCrashHandler32", "CrashReportClient", "unins000", "uninstall" };
		return Task.FromResult((IReadOnlyList<GameMetadata>)(from path in Directory.EnumerateFiles(folderPath, "*.exe", SearchOption.AllDirectories)
			where !ignoredNames.Contains(Path.GetFileNameWithoutExtension(path))
			select new GameMetadata
			{
				Title = CleanTitle(Path.GetFileNameWithoutExtension(path)),
				LaunchType = "Exe",
				ExecutablePath = path,
				WorkingDirectory = (Path.GetDirectoryName(path) ?? folderPath),
				Platform = "PC",
				Genre = "Imported"
			} into game
			orderby game.Title
			select game).ToList());
	}

	private static string CleanTitle(string value)
	{
		return value.Replace("_", " ").Replace("-", " ").Trim();
	}
}
