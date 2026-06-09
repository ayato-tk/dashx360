using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace XboxMetroLauncher.Services;

public sealed class SearchService : ISearchService
{
	public Task SearchWebAsync(string query, string baseSearchUrl, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return Task.CompletedTask;
		}
		string text = Uri.EscapeDataString(query.Trim());
		string fileName = baseSearchUrl + text;
		Process.Start(new ProcessStartInfo
		{
			FileName = fileName,
			UseShellExecute = true
		});
		return Task.CompletedTask;
	}
}
