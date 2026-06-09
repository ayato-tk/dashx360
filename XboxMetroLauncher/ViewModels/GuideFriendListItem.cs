namespace XboxMetroLauncher.ViewModels;

public sealed class GuideFriendListItem : ObservableObject
{
	private string _gamertag = string.Empty;

	private string _friendId = string.Empty;

	private string _subtitle = string.Empty;

	private string _status = string.Empty;

	private string _avatarPath = string.Empty;

	private bool _isAddFriend;

	public string Gamertag
	{
		get
		{
			return _gamertag;
		}
		set
		{
			SetProperty(ref _gamertag, value, "Gamertag");
		}
	}

	public string FriendId
	{
		get
		{
			return _friendId;
		}
		set
		{
			SetProperty(ref _friendId, value, "FriendId");
		}
	}

	public string Subtitle
	{
		get
		{
			return _subtitle;
		}
		set
		{
			SetProperty(ref _subtitle, value, "Subtitle");
		}
	}

	public string Status
	{
		get
		{
			return _status;
		}
		set
		{
			SetProperty(ref _status, value, "Status");
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

	public bool IsAddFriend
	{
		get
		{
			return _isAddFriend;
		}
		set
		{
			SetProperty(ref _isAddFriend, value, "IsAddFriend");
		}
	}
}
