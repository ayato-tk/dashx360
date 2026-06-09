using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XboxMetroLauncher.Services;

public sealed class JsonStore : IJsonStore
{
	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private readonly string _rootPath;

	public JsonStore(string rootPath)
	{
		_rootPath = rootPath;
		Directory.CreateDirectory(_rootPath);
	}

	public async Task<T?> ReadAsync<T>(string fileName, CancellationToken cancellationToken = default(CancellationToken))
	{
		string path = Path.Combine(_rootPath, fileName);
		if (!File.Exists(path))
		{
			return default(T);
		}
		T result;
		await using (FileStream stream = File.OpenRead(path))
		{
			result = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		return result;
	}

	public async Task WriteAsync<T>(string fileName, T value, CancellationToken cancellationToken = default(CancellationToken))
	{
		string path = Path.Combine(_rootPath, fileName);
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		await using FileStream stream = File.Create(path);
		await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}
}
