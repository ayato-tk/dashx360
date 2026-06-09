using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using XboxMetroLauncher.Input;
using XboxMetroLauncher.ViewModels;

namespace XboxMetroLauncher.Views;

public class GuideWindow : Window, IComponentConnector, IStyleConnector
{
	private readonly record struct OverlayFocusCandidate(Control Control, Rect Bounds);

	private readonly GuideViewModel _viewModel;

	private int _lastAnimatedMenuIndex = -1;

	private int _lastAnimatedMediaControlIndex = -1;

	private int _lastAnimatedMediaSubmenuIndex = -1;

	private int _lastAnimatedFriendListIndex = -1;

	private int _lastAnimatedPartyRowIndex = -1;

	private int _lastAnimatedSearchKeyIndex = -1;

	private int _lastAnimatedFriendProfileActionIndex = -1;

	private bool _isOpening;

	private bool _isClosing;

	internal Grid GuideRoot;

	internal Viewbox GuideContent;

	internal TranslateTransform GuideContentOffset;

	internal Grid GuideBladePanel;

	internal TranslateTransform GuideBladeOffset;

	internal Grid MainGuidePanel;

	internal ListBox GuideMenu;

	internal Button MediaSongRowButton;

	internal ItemsControl MediaTransportItems;

	internal Grid MediaSubmenuBlade;

	internal TranslateTransform MediaSubmenuOffset;

	internal ListBox MediaSubmenuList;

	internal Grid GuideMusicPickerPanel;

	internal ListBox GuideMusicPickerListBox;

	internal Grid FriendsListPanel;

	internal ListBox FriendsListBox;

	internal Grid FriendSearchPanel;

	internal ItemsControl SearchMainKeysItems;

	internal ItemsControl SearchActionKeysItems;

	internal Grid FriendProfilePanel;

	internal ListBox FriendProfileActionsList;

	internal Grid FriendsListOverlay;

	internal TranslateTransform FriendsListOverlayOffset;

	internal ListBox FriendsOverlayListBox;

	internal Grid PartyOverlay;

	internal TranslateTransform PartyOverlayOffset;

	internal ListBox PartyOverlayListBox;

	internal Grid FriendSearchOverlay;

	internal TranslateTransform FriendSearchOverlayOffset;

	internal Button FriendSearchCapsButton;

	internal ItemsControl FriendSearchOverlayMainKeysItems;

	internal Button FriendSearchBackspaceButton;

	internal Button FriendSearchSpaceButton;

	internal Button FriendSearchDoneButton;

	internal Grid FriendProfileOverlay;

	internal TranslateTransform FriendProfileOverlayOffset;

	internal ListBox FriendProfileOverlayActionsList;

	internal Grid GuideMusicOverlay;

	internal Grid GuideMusicContentGrid;

	private bool _contentLoaded;

	public bool IsGuideOpen
	{
		get
		{
			if (base.IsVisible && !_isOpening)
			{
				return !_isClosing;
			}
			return false;
		}
	}

	public bool IsTransitioning
	{
		get
		{
			if (!_isOpening)
			{
				return _isClosing;
			}
			return true;
		}
	}

	public event EventHandler? HiddenCompleted;

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool BringWindowToTop(nint hWnd);

	[DllImport("user32.dll")]
	private static extern nint SetFocus(nint hWnd);

	[DllImport("user32.dll")]
	private static extern nint SetActiveWindow(nint hWnd);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	public GuideWindow(GuideViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		base.DataContext = viewModel;
		_viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
	}

	public bool Open()
	{
		if (_isOpening || _isClosing)
		{
			return false;
		}
		if (base.IsVisible)
		{
			ForceForegroundAndCaptureInput();
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)new Action(FocusGuideMenu), (DispatcherPriority)5, Array.Empty<object>());
			return false;
		}
		_isOpening = true;
		_isClosing = false;
		_viewModel.Start();
		base.WindowState = WindowState.Maximized;
		base.Opacity = 0.0;
		GuideContentOffset.Y = -12.0;
		Show();
		_viewModel.PlaySound("select");
		BeginOpenAnimation();
		((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)delegate
		{
			try
			{
				ForceForegroundAndCaptureInput();
				FocusGuideMenu();
			}
			catch (Exception exception)
			{
				App.LogException(exception, "GuideWindow.Open.FocusGuideMenu");
			}
		}, (DispatcherPriority)5, Array.Empty<object>());
		return true;
	}

	public bool CloseGuide(bool playSound = false)
	{
		if (_isClosing || _isOpening || !base.IsVisible)
		{
			return false;
		}
		if (playSound)
		{
			_viewModel.PlaySound("back");
		}
		_isClosing = true;
		_viewModel.Stop();
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(130.0))
		{
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseIn
			}
		};
		doubleAnimation.Completed += delegate
		{
			ReleaseInputCapture();
			Hide();
			base.Opacity = 1.0;
			GuideContentOffset.Y = -12.0;
			_isClosing = false;
			this.HiddenCompleted?.Invoke(this, EventArgs.Empty);
		};
		BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
		GuideContentOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8.0, TimeSpan.FromMilliseconds(130.0))
		{
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseIn
			}
		});
		return true;
	}

	public bool HandleInput(DashboardInputAction action)
	{
		if (_viewModel.IsGuideMusicPickerScreen)
		{
			switch (action)
			{
			case DashboardInputAction.MoveLeft:
			case DashboardInputAction.MoveRight:
			case DashboardInputAction.MoveUp:
			case DashboardInputAction.MoveDown:
				TryMoveGuideMusicFocus(action);
				return true;
			case DashboardInputAction.Activate:
				ActivateFocusedGuideMusicControl();
				return true;
			case DashboardInputAction.Back:
				if (_viewModel.HandleBack())
				{
					FocusGuideMenu();
					return true;
				}
				CloseGuide(playSound: true);
				return true;
			case DashboardInputAction.Details:
				if (_viewModel.Dashboard.OpenMusicVisualizerFullscreenCommand.CanExecute(null))
				{
					_viewModel.Dashboard.OpenMusicVisualizerFullscreenCommand.Execute(null);
				}
				return true;
			}
		}
		switch (action)
		{
		case DashboardInputAction.MoveUp:
			_viewModel.Move(-1);
			FocusGuideMenu();
			return true;
		case DashboardInputAction.MoveDown:
			_viewModel.Move(1);
			FocusGuideMenu();
			return true;
		case DashboardInputAction.MoveLeft:
			if (!_viewModel.TryHandleHorizontal(-1))
			{
				_viewModel.MoveTab(-1);
			}
			FocusGuideMenu();
			return true;
		case DashboardInputAction.MoveRight:
			if (!_viewModel.TryHandleHorizontal(1))
			{
				_viewModel.MoveTab(1);
			}
			FocusGuideMenu();
			return true;
		case DashboardInputAction.PreviousTab:
			if (_viewModel.IsFriendSearchScreen)
			{
				_viewModel.MoveFriendSearchCursor(-1);
				FocusGuideMenu();
				return true;
			}
			_viewModel.MoveTab(-1);
			FocusGuideMenu();
			return true;
		case DashboardInputAction.NextTab:
			if (_viewModel.IsFriendSearchScreen)
			{
				_viewModel.MoveFriendSearchCursor(1);
				FocusGuideMenu();
				return true;
			}
			_viewModel.MoveTab(1);
			FocusGuideMenu();
			return true;
		case DashboardInputAction.LeftTrigger:
			_viewModel.SwitchToSymbolKeyboard();
			FocusGuideMenu();
			return true;
		case DashboardInputAction.RightTrigger:
			_viewModel.SwitchToAccentKeyboard();
			FocusGuideMenu();
			return true;
		case DashboardInputAction.Activate:
			_viewModel.ActivateSelected();
			FocusGuideMenu();
			return true;
		case DashboardInputAction.Back:
			if (_viewModel.HandleBack())
			{
				FocusGuideMenu();
				return true;
			}
			CloseGuide(playSound: true);
			return true;
		case DashboardInputAction.Details:
			_viewModel.HandleFooterX();
			return true;
		case DashboardInputAction.Search:
			_viewModel.HandleFooterY();
			return true;
		case DashboardInputAction.Guide:
			CloseGuide(playSound: true);
			return true;
		default:
			return true;
		}
	}

	private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (_viewModel.IsFriendSearchScreen)
		{
			if ((int)e.Key == 2)
			{
				_viewModel.BackspaceFriendSearchFromKeyboard();
				_viewModel.PlaySound("select");
				e.Handled = true;
			}
			else if ((int)e.Key == 6)
			{
				_viewModel.ConfirmFriendSearch();
				_viewModel.PlaySound("select");
				e.Handled = true;
			}
			else if ((int)e.Key == 13 && _viewModel.HandleBack())
			{
				e.Handled = true;
			}
		}
		if (e.Handled)
		{
			FocusGuideMenu();
		}
		else
		{
			if (_viewModel.IsFriendSearchScreen && IsPhysicalTypingKey(e.Key))
			{
				return;
			}
			Key key;
			if (_viewModel.IsGuideMusicPickerScreen)
			{
				key = e.Key;
				if (key is Key.Up or Key.W)
				{
					TryMoveGuideMusicFocus(DashboardInputAction.MoveUp);
					e.Handled = true;
					return;
				}
				key = e.Key;
				if (key is Key.Down or Key.S)
				{
					TryMoveGuideMusicFocus(DashboardInputAction.MoveDown);
					e.Handled = true;
					return;
				}
				key = e.Key;
				if (key is Key.Left or Key.A)
				{
					TryMoveGuideMusicFocus(DashboardInputAction.MoveLeft);
					e.Handled = true;
					return;
				}
				key = e.Key;
				if (key is Key.Right or Key.D)
				{
					TryMoveGuideMusicFocus(DashboardInputAction.MoveRight);
					e.Handled = true;
					return;
				}
				key = e.Key;
				if (key is Key.Return or Key.Space)
				{
					ActivateFocusedGuideMusicControl();
					e.Handled = true;
					return;
				}
				key = e.Key;
				if (key is Key.Back or Key.Escape)
				{
					if (_viewModel.HandleBack())
					{
						FocusGuideMenu();
						e.Handled = true;
					}
					else
					{
						CloseGuide(playSound: true);
						e.Handled = true;
					}
				}
				else if ((int)e.Key == 67)
				{
					if (_viewModel.Dashboard.OpenMusicVisualizerFullscreenCommand.CanExecute(null))
					{
						_viewModel.Dashboard.OpenMusicVisualizerFullscreenCommand.Execute(null);
					}
					e.Handled = true;
				}
				return;
			}
			key = e.Key;
			if (key is Key.Up or Key.W)
			{
				_viewModel.Move(-1);
				e.Handled = true;
			}
			else
			{
				key = e.Key;
				if (key is Key.Down or Key.S)
				{
					_viewModel.Move(1);
					e.Handled = true;
				}
				else
				{
					key = e.Key;
					if (key is Key.Left or Key.A)
					{
						if (!_viewModel.TryHandleHorizontal(-1))
						{
							_viewModel.MoveTab(-1);
						}
						e.Handled = true;
					}
					else
					{
						key = e.Key;
						if (key is Key.Right or Key.D)
						{
							if (!_viewModel.TryHandleHorizontal(1))
							{
								_viewModel.MoveTab(1);
							}
							e.Handled = true;
						}
						else if ((int)e.Key == 60)
						{
							_viewModel.MoveTab(-1);
							e.Handled = true;
						}
						else if ((int)e.Key == 48)
						{
							_viewModel.MoveTab(1);
							e.Handled = true;
						}
						else
						{
							key = e.Key;
							if (key is Key.Return or Key.Space)
							{
								_viewModel.ActivateSelected();
								e.Handled = true;
							}
							else
							{
								key = e.Key;
								if (key is Key.Back or Key.Escape)
								{
									if (_viewModel.HandleBack())
									{
										e.Handled = true;
									}
									else
									{
										CloseGuide(playSound: true);
										e.Handled = true;
									}
								}
								else if ((int)e.Key == 67)
								{
									_viewModel.HandleFooterX();
									e.Handled = true;
								}
								else if ((int)e.Key == 68)
								{
									_viewModel.HandleFooterY();
									e.Handled = true;
								}
							}
						}
					}
				}
			}
			if (e.Handled)
			{
				FocusGuideMenu();
			}
		}
	}

	private void Window_OnTextInput(object sender, TextCompositionEventArgs e)
	{
		if (!_viewModel.IsFriendSearchScreen || string.IsNullOrEmpty(e.Text))
		{
			return;
		}
		string text = e.Text;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (!char.IsControl(c))
			{
				_viewModel.AppendFriendSearchCharacter(c.ToString());
			}
		}
		_viewModel.PlaySound("focus");
		e.Handled = true;
		FocusGuideMenu();
	}

	private void FocusGuideMenu()
	{
		if (_viewModel.IsGuideMusicPickerScreen)
		{
			FocusGuideMusicOverlay();
			return;
		}
		if (_viewModel.IsFriendsListScreen)
		{
			FocusFriendsList();
			return;
		}
		if (_viewModel.IsPartyScreen)
		{
			FocusPartyRows();
			return;
		}
		if (_viewModel.IsFriendSearchScreen)
		{
			FocusSearchKeys();
			return;
		}
		if (_viewModel.IsFriendProfileScreen)
		{
			FocusFriendProfileActions();
			return;
		}
		if (_viewModel.IsMediaSubmenuOpen)
		{
			FocusMediaSubmenu();
			return;
		}
		if (_viewModel.IsMediaTab && _viewModel.IsMediaSongRowFocused)
		{
			MediaSongRowButton.Focus();
			return;
		}
		if (_viewModel.IsMediaTab && _viewModel.IsMediaTransportFocused)
		{
			FocusMediaTransport();
			return;
		}
		GuideMenu.Focus();
		ListBoxItem obj = GuideMenu.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedIndex) as ListBoxItem;
		obj?.Focus();
		AnimateSelectedListItem(obj, _viewModel.SelectedIndex, ref _lastAnimatedMenuIndex);
	}

	private void FocusFriendsList()
	{
		FriendsOverlayListBox.UpdateLayout();
		FriendsOverlayListBox.Focus();
		ListBoxItem obj = FriendsOverlayListBox.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedFriendListIndex) as ListBoxItem;
		obj?.Focus();
		AnimateSelectedListItem(obj, _viewModel.SelectedFriendListIndex, ref _lastAnimatedFriendListIndex);
	}

	private void FocusPartyRows()
	{
		PartyOverlayListBox.UpdateLayout();
		PartyOverlayListBox.Focus();
		ListBoxItem obj = PartyOverlayListBox.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedPartyRowIndex) as ListBoxItem;
		obj?.Focus();
		AnimateSelectedListItem(obj, _viewModel.SelectedPartyRowIndex, ref _lastAnimatedPartyRowIndex);
	}

	private void FocusSearchKeys()
	{
		Button button = FindSearchKeyButton(_viewModel.SelectedSearchKeyIndex);
		if (button == null)
		{
			GuideMenu.Focus();
			return;
		}
		button.Focus();
		AnimateSelectedButton(button, _viewModel.SelectedSearchKeyIndex, ref _lastAnimatedSearchKeyIndex);
	}

	private void FocusFriendProfileActions()
	{
		FriendProfileOverlayActionsList.UpdateLayout();
		FriendProfileOverlayActionsList.Focus();
		ListBoxItem obj = FriendProfileOverlayActionsList.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedFriendProfileActionIndex) as ListBoxItem;
		obj?.Focus();
		AnimateSelectedListItem(obj, _viewModel.SelectedFriendProfileActionIndex, ref _lastAnimatedFriendProfileActionIndex);
	}

	private void FocusGuideMusicOverlay()
	{
		TryFocus(FindFocusableControl((DependencyObject?)(object)GuideMusicOverlay));
	}

	private void FocusMediaSubmenu()
	{
		MediaSubmenuList.UpdateLayout();
		MediaSubmenuList.Focus();
		ListBoxItem obj = MediaSubmenuList.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedMediaSubmenuIndex) as ListBoxItem;
		obj?.Focus();
		AnimateSelectedListItem(obj, _viewModel.SelectedMediaSubmenuIndex, ref _lastAnimatedMediaSubmenuIndex);
	}

	private void FocusMediaTransport()
	{
		MediaTransportItems.UpdateLayout();
		Button button = FindVisualChild<Button>(MediaTransportItems.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedMediaControlIndex));
		if (button != null)
		{
			button.Focus();
			AnimateSelectedButton(button, _viewModel.SelectedMediaControlIndex, ref _lastAnimatedMediaControlIndex);
		}
		else
		{
			GuideMenu.Focus();
		}
	}

	private void BeginOpenAnimation()
	{
		DoubleAnimation doubleAnimation = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(135.0))
		{
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		doubleAnimation.Completed += delegate
		{
			_isOpening = false;
		};
		BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
		GuideContentOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
	}

	private void ForceForegroundAndCaptureInput()
	{
		WindowInteropHelper windowInteropHelper = new WindowInteropHelper(this);
		nint num = windowInteropHelper.Handle;
		if (num == IntPtr.Zero)
		{
			num = windowInteropHelper.EnsureHandle();
		}
		nint foregroundWindow = GetForegroundWindow();
		uint currentThreadId = GetCurrentThreadId();
		uint processId;
		uint num2 = ((foregroundWindow != IntPtr.Zero) ? GetWindowThreadProcessId(foregroundWindow, out processId) : 0u);
		bool flag = false;
		try
		{
			if (num2 != 0 && num2 != currentThreadId)
			{
				flag = AttachThreadInput(currentThreadId, num2, fAttach: true);
			}
			Activate();
			BringWindowToTop(num);
			SetForegroundWindow(num);
			SetActiveWindow(num);
			SetFocus(num);
			Focus();
			Keyboard.Focus(GuideRoot);
			Mouse.Capture(GuideRoot, CaptureMode.SubTree);
		}
		finally
		{
			if (flag)
			{
				AttachThreadInput(currentThreadId, num2, fAttach: false);
			}
		}
	}

	private static void ReleaseInputCapture()
	{
		if (Mouse.Captured != null)
		{
			Mouse.Capture(null);
		}
	}

	private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "CurrentTabTitle")
		{
			BeginBladeTransition(_viewModel.TabTransitionDirection);
			_lastAnimatedMenuIndex = -1;
			_lastAnimatedMediaControlIndex = -1;
			_lastAnimatedMediaSubmenuIndex = -1;
			_lastAnimatedFriendListIndex = -1;
			_lastAnimatedPartyRowIndex = -1;
			_lastAnimatedSearchKeyIndex = -1;
			_lastAnimatedFriendProfileActionIndex = -1;
		}
		if (e.PropertyName == "IsMediaSubmenuOpen" && _viewModel.IsMediaSubmenuOpen)
		{
			BeginMediaSubmenuOpenAnimation();
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)new Action(FocusGuideMenu), Array.Empty<object>());
		}
		if (e.PropertyName == "IsFriendsListScreen" && _viewModel.IsFriendsListScreen)
		{
			_lastAnimatedFriendListIndex = -1;
			BeginOverlayOpenAnimation(FriendsListOverlay, FriendsListOverlayOffset, 18.0);
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)new Action(FocusGuideMenu), Array.Empty<object>());
		}
		if (e.PropertyName == "IsPartyScreen" && _viewModel.IsPartyScreen)
		{
			_lastAnimatedPartyRowIndex = -1;
			BeginOverlayOpenAnimation(PartyOverlay, PartyOverlayOffset, 18.0);
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)new Action(FocusGuideMenu), Array.Empty<object>());
		}
		if (e.PropertyName == "IsFriendSearchScreen" && _viewModel.IsFriendSearchScreen)
		{
			_lastAnimatedSearchKeyIndex = -1;
			BeginOverlayOpenAnimation(FriendSearchOverlay, FriendSearchOverlayOffset, 18.0);
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)new Action(FocusGuideMenu), Array.Empty<object>());
		}
		if (e.PropertyName == "IsFriendProfileScreen" && _viewModel.IsFriendProfileScreen)
		{
			_lastAnimatedFriendProfileActionIndex = -1;
			BeginOverlayOpenAnimation(FriendProfileOverlay, FriendProfileOverlayOffset, 16.0);
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)new Action(FocusGuideMenu), Array.Empty<object>());
		}
		if (e.PropertyName == "IsGuideMusicPickerScreen" && _viewModel.IsGuideMusicPickerScreen)
		{
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)new Action(FocusGuideMenu), Array.Empty<object>());
		}
	}

	private void BeginMediaSubmenuOpenAnimation()
	{
		MediaSubmenuBlade.Opacity = 0.0;
		MediaSubmenuOffset.X = 22.0;
		MediaSubmenuBlade.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(105.0))
		{
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
		MediaSubmenuOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(145.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
	}

	private static void BeginOverlayOpenAnimation(FrameworkElement element, TranslateTransform offset, double fromX)
	{
		element.BeginAnimation(UIElement.OpacityProperty, null);
		offset.BeginAnimation(TranslateTransform.XProperty, null);
		element.Opacity = 0.0;
		offset.X = fromX;
		element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(110.0))
		{
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
		offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(145.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
	}

	private void BeginBladeTransition(int direction)
	{
		if (direction != 0)
		{
			GuideBladePanel.BeginAnimation(UIElement.OpacityProperty, null);
			GuideBladeOffset.BeginAnimation(TranslateTransform.XProperty, null);
			GuideBladePanel.Opacity = 0.92;
			GuideBladeOffset.X = ((direction > 0) ? 26 : (-26));
			GuideBladePanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(130.0))
			{
				EasingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
			GuideBladeOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(155.0))
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		}
	}

	private static void AnimateSelectedListItem(ListBoxItem? item, int selectedIndex, ref int lastAnimatedIndex)
	{
		if (item != null && selectedIndex != lastAnimatedIndex)
		{
			lastAnimatedIndex = selectedIndex;
			AnimateFocusNudge(item);
		}
	}

	private static void AnimateSelectedButton(Button? button, int selectedIndex, ref int lastAnimatedIndex)
	{
		if (button != null && selectedIndex != lastAnimatedIndex)
		{
			lastAnimatedIndex = selectedIndex;
			AnimateFocusNudge(button);
		}
	}

	private static void AnimateFocusNudge(UIElement element)
	{
		TranslateTransform translateTransform = element.RenderTransform as TranslateTransform;
		if (translateTransform == null)
		{
			translateTransform = (TranslateTransform)(element.RenderTransform = new TranslateTransform());
		}
		element.BeginAnimation(UIElement.OpacityProperty, null);
		translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
		element.Opacity = 0.96;
		translateTransform.X = 5.0;
		element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(85.0))
		{
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
		translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(105.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
	}

	private void LeftOuterTab_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_viewModel.SelectRelativeTab(-2);
		FocusGuideMenu();
	}

	private void LeftInnerTab_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_viewModel.SelectRelativeTab(-1);
		FocusGuideMenu();
	}

	private void RightInnerTab_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_viewModel.SelectRelativeTab(1);
		FocusGuideMenu();
	}

	private void RightOuterTab_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_viewModel.SelectRelativeTab(2);
		FocusGuideMenu();
	}

	private void GuideMenu_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		object originalSource = e.OriginalSource;
		ListBoxItem listBoxItem = FindAncestor<ListBoxItem>((DependencyObject?)((originalSource is DependencyObject) ? originalSource : null));
		if (listBoxItem != null)
		{
			GuideMenu.SelectedItem = listBoxItem.DataContext;
			_viewModel.ActivateSelected();
			FocusGuideMenu();
		}
	}

	private void FriendsList_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		object originalSource = e.OriginalSource;
		if (FindAncestor<ListBoxItem>((DependencyObject?)((originalSource is DependencyObject) ? originalSource : null))?.DataContext is GuideFriendListItem guideFriendListItem)
		{
			FriendsOverlayListBox.SelectedItem = guideFriendListItem;
			_viewModel.ActivateFriendListItem(guideFriendListItem);
			FocusGuideMenu();
		}
	}

	private void FriendProfileActions_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		object originalSource = e.OriginalSource;
		if (FindAncestor<ListBoxItem>((DependencyObject?)((originalSource is DependencyObject) ? originalSource : null))?.DataContext is GuideMenuItem guideMenuItem)
		{
			FriendProfileOverlayActionsList.SelectedItem = guideMenuItem;
			_viewModel.ActivateFriendProfileAction(guideMenuItem);
			FocusGuideMenu();
		}
	}

	private void PartyRows_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		object originalSource = e.OriginalSource;
		if (FindAncestor<ListBoxItem>((DependencyObject?)((originalSource is DependencyObject) ? originalSource : null))?.DataContext is GuidePartyRowItem { IsSelectable: not false } guidePartyRowItem)
		{
			PartyOverlayListBox.SelectedItem = guidePartyRowItem;
			_viewModel.ActivatePartyRowItem(guidePartyRowItem);
			FocusGuideMenu();
		}
	}

	private void DisableMouseWheelScroll(object sender, MouseWheelEventArgs e)
	{
		e.Handled = true;
	}

	private void MediaSubmenu_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		object originalSource = e.OriginalSource;
		ListBoxItem listBoxItem = FindAncestor<ListBoxItem>((DependencyObject?)((originalSource is DependencyObject) ? originalSource : null));
		if (listBoxItem != null)
		{
			MediaSubmenuList.SelectedItem = listBoxItem.DataContext;
			_viewModel.ActivateSelected();
			FocusGuideMenu();
		}
	}

	private void GuideMusicPicker_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
	}

	private void GuideMusicFullscreenHint_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_viewModel.Dashboard.OpenMusicVisualizerFullscreenCommand.CanExecute(null))
		{
			_viewModel.Dashboard.OpenMusicVisualizerFullscreenCommand.Execute(null);
		}
		e.Handled = true;
	}

	private void MinimizeDashboardHint_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		_viewModel.PlaySound("select");
		_viewModel.MinimizeDashboard();
		e.Handled = true;
	}

	private void FooterXHint_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		_viewModel.HandleFooterX();
		e.Handled = true;
		FocusGuideMenu();
	}

	protected override void OnClosed(EventArgs e)
	{
		_viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
		base.OnClosed(e);
	}

	private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
	{
		while (current != null)
		{
			T val = (T)(object)((current is T) ? current : null);
			if (val != null)
			{
				return val;
			}
			current = VisualTreeHelper.GetParent(current);
		}
		return default(T);
	}

	private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
	{
		if (parent == null)
		{
			return default(T);
		}
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			T val = (T)(object)((child is T) ? child : null);
			if (val != null)
			{
				return val;
			}
			T val2 = FindVisualChild<T>(child);
			if (val2 != null)
			{
				return val2;
			}
		}
		return default(T);
	}

	private bool TryMoveGuideMusicFocus(DashboardInputAction action)
	{
		List<OverlayFocusCandidate> overlayFocusCandidates = GetOverlayFocusCandidates(GuideMusicOverlay);
		if (overlayFocusCandidates.Count == 0)
		{
			return false;
		}
		IInputElement focusedElement = Keyboard.FocusedElement;
		Control currentControl = focusedElement as Control;
		if (currentControl == null || !overlayFocusCandidates.Any((OverlayFocusCandidate candidate) => candidate.Control == currentControl))
		{
			return TryFocus(overlayFocusCandidates[0].Control);
		}
		Point currentCenter = GetCenter(overlayFocusCandidates.First((OverlayFocusCandidate candidate) => candidate.Control == currentControl).Bounds);
		Vector direction = (Vector)(action switch
		{
			DashboardInputAction.MoveLeft => new Vector(-1.0, 0.0), 
			DashboardInputAction.MoveRight => new Vector(1.0, 0.0), 
			DashboardInputAction.MoveUp => new Vector(0.0, -1.0), 
			DashboardInputAction.MoveDown => new Vector(0.0, 1.0), 
			_ => new Vector(0.0, 0.0), 
		});
		var anon = (from item in (from candidate in overlayFocusCandidates
				where candidate.Control != currentControl
				select new
				{
					Candidate = candidate,
					Center = GetCenter(candidate.Bounds)
				}).Select(item =>
			{
				OverlayFocusCandidate candidate = item.Candidate;
				Vector delta = item.Center - currentCenter;
				DashboardInputAction dashboardInputAction = action;
				Point center;
				double num;
				if ((uint)dashboardInputAction > 1u)
				{
					center = item.Center;
					num = Math.Abs(center.Y - currentCenter.Y);
				}
				else
				{
					center = item.Center;
					num = Math.Abs(center.X - currentCenter.X);
				}
				double primary = num;
				dashboardInputAction = action;
				double secondary;
				if (!((uint)dashboardInputAction <= 1u))
				{
					center = item.Center;
					secondary = Math.Abs(center.X - currentCenter.X);
				}
				else
				{
					center = item.Center;
					secondary = Math.Abs(center.Y - currentCenter.Y);
				}
				return new
				{
					Candidate = candidate,
					Delta = delta,
					Primary = primary,
					Secondary = secondary
				};
			})
			where Vector.Multiply(item.Delta, direction) > 1.0
			orderby item.Secondary * 2.2 + item.Primary, item.Primary
			select item).FirstOrDefault();
		if (anon != null)
		{
			return TryFocus(anon.Candidate.Control);
		}
		return false;
	}

	private void ActivateFocusedGuideMusicControl()
	{
		if (!(Keyboard.FocusedElement is Button button))
		{
			return;
		}
		if (button.Command != null)
		{
			if (button.Command.CanExecute(button.CommandParameter))
			{
				button.Command.Execute(button.CommandParameter);
			}
		}
		else
		{
			button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
		}
	}

	private static List<OverlayFocusCandidate> GetOverlayFocusCandidates(FrameworkElement overlay)
	{
		try
		{
			return (from control in FindVisualChildren<Control>((DependencyObject?)(object)overlay)
				where control.IsVisible && control.IsEnabled && control.Focusable
				select new OverlayFocusCandidate(control, GetElementBounds(control, overlay))).Where(delegate(OverlayFocusCandidate candidate)
			{
				Rect bounds = candidate.Bounds;
				if (bounds.Width > 0.0)
				{
					bounds = candidate.Bounds;
					return bounds.Height > 0.0;
				}
				return false;
			}).Where(delegate(OverlayFocusCandidate candidate)
			{
				Point center = GetCenter(candidate.Bounds);
				return center.X >= 0.0 && center.Y >= 0.0 && center.X <= overlay.ActualWidth && center.Y <= overlay.ActualHeight;
			}).ToList();
		}
		catch
		{
			return new List<OverlayFocusCandidate>();
		}
	}

	private static Rect GetElementBounds(FrameworkElement element, Visual relativeTo)
	{
		try
		{
			return element.TransformToAncestor(relativeTo).TransformBounds(new Rect(0.0, 0.0, element.ActualWidth, element.ActualHeight));
		}
		catch (InvalidOperationException)
		{
			return Rect.Empty;
		}
		catch (ArgumentException)
		{
			return Rect.Empty;
		}
		catch
		{
			return Rect.Empty;
		}
	}

	private static Point GetCenter(Rect rect)
	{
		return new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
	}

	private static UIElement? FindFocusableControl(DependencyObject? root)
	{
		return FindVisualChildren<UIElement>(root).FirstOrDefault((UIElement element) => element.IsVisible && element.Focusable && (!(element is Control control) || control.IsEnabled));
	}

	private bool TryFocus(UIElement? element)
	{
		if (element == null || !element.IsVisible || !element.Focusable)
		{
			return false;
		}
		if (element is Control && !element.IsEnabled)
		{
			return false;
		}
		try
		{
			return element.Focus();
		}
		catch
		{
			return false;
		}
	}

	private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent) where T : DependencyObject
	{
		if (parent == null)
		{
			yield break;
		}
		int count = VisualTreeHelper.GetChildrenCount(parent);
		for (int index = 0; index < count; index++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, index);
			T val = (T)(object)((child is T) ? child : null);
			if (val != null)
			{
				yield return val;
			}
			foreach (T item in FindVisualChildren<T>(child))
			{
				yield return item;
			}
		}
	}

	private static bool IsPhysicalTypingKey(Key key)
	{
		return key is >= Key.D0 and <= Key.Z
			or >= Key.NumPad0 and <= Key.NumPad9
			or Key.Subtract
			or Key.Decimal
			or >= Key.Oem1 and <= Key.Oem3
			or Key.Oem102
			or Key.Space;
	}

	private Button? FindSearchKeyButton(int index)
	{
		if (index < 0)
		{
			return null;
		}
		if (index < 40)
		{
			FriendSearchOverlayMainKeysItems.UpdateLayout();
			return FindVisualChild<Button>(FriendSearchOverlayMainKeysItems.ItemContainerGenerator.ContainerFromIndex(index));
		}
		return index switch
		{
			40 => FriendSearchCapsButton, 
			41 => FriendSearchBackspaceButton, 
			42 => FriendSearchSpaceButton, 
			43 => FriendSearchDoneButton, 
			_ => null, 
		};
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.26.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/XboxMetroLauncher;V1.0.0.0;component/views/guidewindow.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.26.0")]
	internal Delegate _CreateDelegate(Type delegateType, string handler)
	{
		return Delegate.CreateDelegate(delegateType, this, handler);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.26.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			((GuideWindow)target).PreviewKeyDown += Window_OnPreviewKeyDown;
			((GuideWindow)target).TextInput += Window_OnTextInput;
			break;
		case 2:
			GuideRoot = (Grid)target;
			break;
		case 5:
			GuideContent = (Viewbox)target;
			break;
		case 6:
			GuideContentOffset = (TranslateTransform)target;
			break;
		case 7:
			GuideBladePanel = (Grid)target;
			break;
		case 8:
			GuideBladeOffset = (TranslateTransform)target;
			break;
		case 9:
			((Border)target).MouseLeftButtonDown += LeftOuterTab_OnMouseLeftButtonDown;
			break;
		case 10:
			((Border)target).MouseLeftButtonDown += LeftInnerTab_OnMouseLeftButtonDown;
			break;
		case 11:
			MainGuidePanel = (Grid)target;
			break;
		case 12:
			GuideMenu = (ListBox)target;
			GuideMenu.MouseLeftButtonUp += GuideMenu_OnMouseLeftButtonUp;
			break;
		case 13:
			MediaSongRowButton = (Button)target;
			break;
		case 14:
			MediaTransportItems = (ItemsControl)target;
			break;
		case 15:
			MediaSubmenuBlade = (Grid)target;
			break;
		case 16:
			MediaSubmenuOffset = (TranslateTransform)target;
			break;
		case 17:
			MediaSubmenuList = (ListBox)target;
			MediaSubmenuList.MouseLeftButtonUp += MediaSubmenu_OnMouseLeftButtonUp;
			break;
		case 18:
			GuideMusicPickerPanel = (Grid)target;
			break;
		case 19:
			GuideMusicPickerListBox = (ListBox)target;
			GuideMusicPickerListBox.MouseLeftButtonUp += GuideMusicPicker_OnMouseLeftButtonUp;
			break;
		case 20:
			FriendsListPanel = (Grid)target;
			break;
		case 21:
			FriendsListBox = (ListBox)target;
			FriendsListBox.MouseLeftButtonUp += FriendsList_OnMouseLeftButtonUp;
			break;
		case 22:
			FriendSearchPanel = (Grid)target;
			break;
		case 23:
			SearchMainKeysItems = (ItemsControl)target;
			break;
		case 24:
			SearchActionKeysItems = (ItemsControl)target;
			break;
		case 25:
			FriendProfilePanel = (Grid)target;
			break;
		case 26:
			FriendProfileActionsList = (ListBox)target;
			FriendProfileActionsList.MouseLeftButtonUp += FriendProfileActions_OnMouseLeftButtonUp;
			break;
		case 27:
			((Border)target).MouseLeftButtonDown += RightInnerTab_OnMouseLeftButtonDown;
			break;
		case 28:
			((Border)target).MouseLeftButtonDown += RightOuterTab_OnMouseLeftButtonDown;
			break;
		case 29:
			FriendsListOverlay = (Grid)target;
			break;
		case 30:
			FriendsListOverlayOffset = (TranslateTransform)target;
			break;
		case 31:
			FriendsOverlayListBox = (ListBox)target;
			FriendsOverlayListBox.PreviewMouseWheel += DisableMouseWheelScroll;
			FriendsOverlayListBox.MouseLeftButtonUp += FriendsList_OnMouseLeftButtonUp;
			break;
		case 32:
			PartyOverlay = (Grid)target;
			break;
		case 33:
			PartyOverlayOffset = (TranslateTransform)target;
			break;
		case 34:
			PartyOverlayListBox = (ListBox)target;
			PartyOverlayListBox.PreviewMouseWheel += DisableMouseWheelScroll;
			PartyOverlayListBox.MouseLeftButtonUp += PartyRows_OnMouseLeftButtonUp;
			break;
		case 35:
			FriendSearchOverlay = (Grid)target;
			break;
		case 36:
			FriendSearchOverlayOffset = (TranslateTransform)target;
			break;
		case 37:
			FriendSearchCapsButton = (Button)target;
			break;
		case 38:
			FriendSearchOverlayMainKeysItems = (ItemsControl)target;
			break;
		case 39:
			FriendSearchBackspaceButton = (Button)target;
			break;
		case 40:
			FriendSearchSpaceButton = (Button)target;
			break;
		case 41:
			FriendSearchDoneButton = (Button)target;
			break;
		case 42:
			FriendProfileOverlay = (Grid)target;
			break;
		case 43:
			FriendProfileOverlayOffset = (TranslateTransform)target;
			break;
		case 44:
			FriendProfileOverlayActionsList = (ListBox)target;
			FriendProfileOverlayActionsList.PreviewMouseWheel += DisableMouseWheelScroll;
			FriendProfileOverlayActionsList.MouseLeftButtonUp += FriendProfileActions_OnMouseLeftButtonUp;
			break;
		case 45:
			GuideMusicOverlay = (Grid)target;
			break;
		case 46:
			GuideMusicContentGrid = (Grid)target;
			break;
		case 47:
			((Grid)target).MouseLeftButtonUp += GuideMusicFullscreenHint_OnMouseLeftButtonUp;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.26.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IStyleConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 3:
			((Grid)target).MouseLeftButtonUp += FooterXHint_OnMouseLeftButtonUp;
			break;
		case 4:
			((StackPanel)target).MouseLeftButtonUp += MinimizeDashboardHint_OnMouseLeftButtonUp;
			break;
		}
	}
}
