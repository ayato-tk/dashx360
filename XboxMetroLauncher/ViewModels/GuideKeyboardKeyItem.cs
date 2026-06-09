using System;

namespace XboxMetroLauncher.ViewModels;

public sealed class GuideKeyboardKeyItem : ObservableObject
{
	private string _label = string.Empty;

	private bool _isSelected;

	public string Label
	{
		get
		{
			return _label;
		}
		set
		{
			SetProperty(ref _label, value, "Label");
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

	public bool IsWide { get; }

	public Action Action { get; }

	public GuideKeyboardKeyItem(string label, Action action, bool isWide = false)
	{
		_label = label;
		Action = action;
		IsWide = isWide;
	}
}
