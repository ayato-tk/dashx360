using System.Collections.Generic;

namespace XboxMetroLauncher.Models;

public sealed class GameLibrary
{
	public List<GameMetadata> Games { get; set; } = new List<GameMetadata>();

	public List<string> LibraryPaths { get; set; } = new List<string>();
}
