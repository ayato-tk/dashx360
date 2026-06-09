namespace XboxMetroLauncher.ViewModels;

public sealed class GuidePartyRowItem : ObservableObject
{
	private string _rowKind = string.Empty;

	private string _title = string.Empty;

	private string _activityText = string.Empty;

	private string _avatarPath = string.Empty;

	private string _activityIcon = string.Empty;

	private bool _isSelectable;

	private bool _showVoiceIcon;

	private bool _isHost;

	public string RowKind
	{
		get
		{
			return _rowKind;
		}
		set
		{
			SetProperty(ref _rowKind, value, "RowKind");
		}
	}

	public string Title
	{
		get
		{
			return _title;
		}
		set
		{
			SetProperty(ref _title, value, "Title");
		}
	}

	public string ActivityText
	{
		get
		{
			return _activityText;
		}
		set
		{
			SetProperty(ref _activityText, value, "ActivityText");
		}
	}

	public string AvatarPath
	{
		get
		{
			return _avatarPath;
		}
		set
		{
			SetProperty(ref _avatarPath, value, "AvatarPath");
		}
	}

	public string ActivityIcon
	{
		get
		{
			return _activityIcon;
		}
		set
		{
			SetProperty(ref _activityIcon, value, "ActivityIcon");
		}
	}

	public bool IsSelectable
	{
		get
		{
			return _isSelectable;
		}
		set
		{
			SetProperty(ref _isSelectable, value, "IsSelectable");
		}
	}

	public bool ShowVoiceIcon
	{
		get
		{
			return _showVoiceIcon;
		}
		set
		{
			SetProperty(ref _showVoiceIcon, value, "ShowVoiceIcon");
		}
	}

	public bool IsHost
	{
		get
		{
			return _isHost;
		}
		set
		{
			SetProperty(ref _isHost, value, "IsHost");
		}
	}
}
