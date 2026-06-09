using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class AudioService : IAudioService
{
	private readonly Func<bool> _isEnabled;

	private readonly Panel? _host;

	private readonly List<MediaPlayer> _activePlayers = new List<MediaPlayer>();

	private readonly List<MediaElement> _activeElements = new List<MediaElement>();

	private readonly Dictionary<MediaPlayer, string> _playerSoundNames = new Dictionary<MediaPlayer, string>();

	private readonly Dictionary<MediaElement, string> _elementSoundNames = new Dictionary<MediaElement, string>();

	private readonly Dictionary<string, DateTimeOffset> _lastPlayTimes = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

	private const int MaxActivePlayers = 8;

	private static readonly Dictionary<string, string[]> SoundFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["startup"] = new string[2] { "02. Startup (2010).mp3", "startup.wav" },
		["page-left"] = new string[2] { "08. Page Left.mp3", "tab.wav" },
		["page-right"] = new string[2] { "09. Page Right.mp3", "tab.wav" },
		["tab"] = new string[2] { "09. Page Right.mp3", "tab.wav" },
		["select"] = new string[3] { "10. Select A.mp3", "13. Select.mp3", "select.wav" },
		["activate"] = new string[3] { "10. Select A.mp3", "13. Select.mp3", "select.wav" },
		["back"] = new string[3] { "14. Back.mp3", "15. Back 2.mp3", "back.wav" },
		["focus"] = new string[3] { "13. Select.mp3", "11. Select A (Alt).mp3", "focus.wav" }
	};

	public AudioService(Func<bool> isEnabled, Panel? host = null)
	{
		_isEnabled = isEnabled;
		_host = host;
	}

	public void Play(string soundName)
	{
		if (!_isEnabled() || IsThrottled(soundName))
		{
			return;
		}
		string text = ResolveSoundPath(soundName);
		if (text == null)
		{
			SystemSounds.Asterisk.Play();
			return;
		}
		if (_host != null)
		{
			PlayThroughElement(text, soundName);
			return;
		}
		if (string.Equals(Path.GetExtension(text), ".wav", StringComparison.OrdinalIgnoreCase))
		{
			using (SoundPlayer soundPlayer = new SoundPlayer(text))
			{
				soundPlayer.Play();
				return;
			}
		}
		MediaPlayer mediaPlayer = null;
		try
		{
			TrimActivePlayers();
			mediaPlayer = new MediaPlayer();
			mediaPlayer.MediaEnded += delegate
			{
				ClosePlayer(mediaPlayer);
			};
			mediaPlayer.MediaFailed += delegate
			{
				ClosePlayer(mediaPlayer);
			};
			_activePlayers.Add(mediaPlayer);
			_playerSoundNames[mediaPlayer] = soundName;
			mediaPlayer.Open(new Uri(text, UriKind.Absolute));
			mediaPlayer.Play();
		}
		catch
		{
			if (mediaPlayer != null)
			{
				ClosePlayer(mediaPlayer);
			}
		}
	}

	public void Stop(string soundName)
	{
		foreach (MediaPlayer item in (from pair in _playerSoundNames
			where string.Equals(pair.Value, soundName, StringComparison.OrdinalIgnoreCase)
			select pair.Key).ToList())
		{
			ClosePlayer(item);
		}
		foreach (MediaElement item2 in (from pair in _elementSoundNames
			where string.Equals(pair.Value, soundName, StringComparison.OrdinalIgnoreCase)
			select pair.Key).ToList())
		{
			CloseElement(item2);
		}
	}

	private void PlayThroughElement(string path, string soundName)
	{
		Panel? host = _host;
		object obj = ((host != null) ? ((DispatcherObject)host).Dispatcher : null);
		if (obj == null)
		{
			Application current = Application.Current;
			obj = ((current != null) ? ((DispatcherObject)current).Dispatcher : null);
		}
		Dispatcher val = (Dispatcher)obj;
		if (val != null && !val.CheckAccess())
		{
			val.BeginInvoke((Delegate)(Action)delegate
			{
				PlayThroughElement(path, soundName);
			}, Array.Empty<object>());
		}
		else
		{
			if (_host == null)
			{
				return;
			}
			try
			{
				TrimActiveElements();
				MediaElement element = new MediaElement
				{
					Width = 1.0,
					Height = 1.0,
					Opacity = 0.0,
					IsHitTestVisible = false,
					LoadedBehavior = MediaState.Manual,
					UnloadedBehavior = MediaState.Manual,
					Volume = 1.0,
					Source = new Uri(path, UriKind.Absolute)
				};
				element.MediaEnded += delegate
				{
					CloseElement(element);
				};
				element.MediaFailed += delegate
				{
					CloseElement(element);
				};
				_activeElements.Add(element);
				_elementSoundNames[element] = soundName;
				_host.Children.Add(element);
				element.Play();
			}
			catch
			{
			}
		}
	}

	private static string? ResolveSoundPath(string soundName)
	{
		string[] value;
		string[] array = (SoundFiles.TryGetValue(soundName, out value) ? value : new string[2]
		{
			soundName + ".mp3",
			soundName + ".wav"
		});
		foreach (string item in AppPaths.CandidateRoots().SelectMany((string root) => new string[3]
		{
			Path.Combine(root, "Assets", "Audio", "Sounds"),
			Path.Combine(root, "sounds"),
			Path.Combine(root, "Assets", "Audio")
		}).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			.ToList())
		{
			string[] array2 = array;
			foreach (string path in array2)
			{
				string text = Path.Combine(item, path);
				if (File.Exists(text))
				{
					return text;
				}
			}
		}
		return null;
	}

	private void ClosePlayer(MediaPlayer mediaPlayer)
	{
		mediaPlayer.Close();
		_activePlayers.Remove(mediaPlayer);
		_playerSoundNames.Remove(mediaPlayer);
	}

	private void TrimActivePlayers()
	{
		while (_activePlayers.Count >= 8)
		{
			ClosePlayer(_activePlayers[0]);
		}
	}

	private void CloseElement(MediaElement element)
	{
		try
		{
			element.Stop();
			element.Source = null;
			_host?.Children.Remove(element);
			_activeElements.Remove(element);
			_elementSoundNames.Remove(element);
		}
		catch
		{
		}
	}

	private void TrimActiveElements()
	{
		while (_activeElements.Count >= 8)
		{
			CloseElement(_activeElements[0]);
		}
	}

	private bool IsThrottled(string soundName)
	{
		TimeSpan timeSpan;
		switch (soundName)
		{
		case "focus":
			timeSpan = TimeSpan.FromMilliseconds(110.0);
			break;
		case "page-left":
		case "page-right":
		case "tab":
			timeSpan = TimeSpan.FromMilliseconds(150.0);
			break;
		default:
			timeSpan = TimeSpan.Zero;
			break;
		}
		TimeSpan timeSpan2 = timeSpan;
		if (timeSpan2 == TimeSpan.Zero)
		{
			return false;
		}
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		if (_lastPlayTimes.TryGetValue(soundName, out var value) && utcNow - value < timeSpan2)
		{
			return true;
		}
		_lastPlayTimes[soundName] = utcNow;
		return false;
	}
}
