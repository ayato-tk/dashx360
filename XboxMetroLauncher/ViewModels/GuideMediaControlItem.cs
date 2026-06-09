using System;

namespace XboxMetroLauncher.ViewModels;

public sealed class GuideMediaControlItem : ObservableObject
{
	private string _title = string.Empty;

	private string _icon = string.Empty;

	private bool _isSelected;

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

	public string Icon
	{
		get
		{
			return _icon;
		}
		set
		{
			SetProperty(ref _icon, value, "Icon");
		}
	}

	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			SetProperty(ref _isSelected, value, "IsSelected");
		}
	}

	public Action Action { get; }

	public GuideMediaControlItem(string title, string icon, Action action)
	{
		_title = title;
		_icon = icon;
		Action = action;
	}
}
