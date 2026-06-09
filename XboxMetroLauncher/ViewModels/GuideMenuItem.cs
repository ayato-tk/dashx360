using System;

namespace XboxMetroLauncher.ViewModels;

public sealed class GuideMenuItem : ObservableObject
{
	private string _title = string.Empty;

	private string _count = string.Empty;

	private string _icon = string.Empty;

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

	public string Count
	{
		get
		{
			return _count;
		}
		set
		{
			SetProperty(ref _count, value, "Count");
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

	public bool IsNoOp { get; }

	public Action Action { get; }

	public GuideMenuItem(string title, string icon, Action action, string count = "", bool isNoOp = false)
	{
		_title = title;
		_icon = icon;
		Count = count;
		Action = action;
		IsNoOp = isNoOp;
	}
}
