using System.IO;

namespace XboxMetroLauncher.ViewModels;

public sealed class MusicTrackViewModel : ObservableObject
{
	private bool _isPlaying;

	public string Path { get; }

	public string Title { get; }

	public bool IsPlaying
	{
		get
		{
			return _isPlaying;
		}
		set
		{
			SetProperty(ref _isPlaying, value, "IsPlaying");
		}
	}

	public MusicTrackViewModel(string path)
	{
		Path = path;
		Title = System.IO.Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
	}
}
