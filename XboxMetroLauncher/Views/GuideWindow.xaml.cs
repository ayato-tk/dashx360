using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XboxMetroLauncher.Input;
using XboxMetroLauncher.ViewModels;

namespace XboxMetroLauncher.Views;

public partial class GuideWindow : Window
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private readonly GuideViewModel _viewModel;
    private int _lastAnimatedMenuIndex = -1;
    private int _lastAnimatedMediaControlIndex = -1;
    private int _lastAnimatedMediaSubmenuIndex = -1;
    private int _lastAnimatedFriendListIndex = -1;
    private int _lastAnimatedPartyRowIndex = -1;
    private int _lastAnimatedSearchKeyIndex = -1;
    private int _lastAnimatedFriendProfileActionIndex = -1;
    private int _lastAnimatedAchievementIndex = -1;
    private int _pendingCommunitySwipeDirection;
    private bool _isOpening;
    private bool _isClosing;
    private const double GuideBladeLeft = 210;
    private const double GuideBladeTop = 50;
    private const double GuideBladeWidth = 860;
    private const double GuideBladeHeight = 606;

    public event EventHandler? HiddenCompleted;

    public bool IsGuideOpen => IsVisible && !_isOpening && !_isClosing;
    public bool IsTransitioning => _isOpening || _isClosing;

    public GuideWindow(GuideViewModel viewModel)
    {
        InitializeComponent();
        ApplyGuideBladeScale();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        _viewModel.ChatMessages.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => FriendChatScroll.ScrollToEnd()));
    }

    public bool Open()
    {
        if (_isOpening || _isClosing)
        {
            return false;
        }

        if (IsVisible)
        {
            ForceForegroundAndCaptureInput();
            Dispatcher.BeginInvoke(new Action(FocusGuideMenu), System.Windows.Threading.DispatcherPriority.Input);
            return false;
        }

        _isOpening = true;
        _isClosing = false;
        _viewModel.Start();
        ApplyGuideBladeScale();
        WindowState = WindowState.Maximized;
        Opacity = 0;
        GuideContentOffset.Y = -12;
        Show();

        _viewModel.PlaySound("select");
        BeginOpenAnimation();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                ForceForegroundAndCaptureInput();
                FocusGuideMenu();
            }
            catch (Exception ex)
            {
                App.LogException(ex, "GuideWindow.Open.FocusGuideMenu");
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
        return true;
    }

    private void ApplyGuideBladeScale()
    {
        SetGuideBladeBounds(AchievementsOverlay);
        SetGuideBladeBounds(FriendsListOverlay);
        SetGuideBladeBounds(PartyOverlay);
        SetGuideBladeBounds(FriendProfileOverlay);
        SetGuideBladeBounds(FriendChatOverlay);

        if (AchievementsOverlay.RowDefinitions.Count >= 2)
        {
            AchievementsOverlay.RowDefinitions[1].Height = new GridLength(526);
        }

        if (FriendsListOverlay.RowDefinitions.Count >= 2)
        {
            FriendsListOverlay.RowDefinitions[1].Height = new GridLength(526);
        }

        if (PartyOverlay.RowDefinitions.Count >= 2)
        {
            PartyOverlay.RowDefinitions[1].Height = new GridLength(526);
        }

        if (FriendProfileOverlay.RowDefinitions.Count >= 2)
        {
            FriendProfileOverlay.RowDefinitions[0].Height = new GridLength(40);
            FriendProfileOverlay.RowDefinitions[1].Height = new GridLength(526);
        }

        if (FriendChatOverlay.RowDefinitions.Count >= 2)
        {
            FriendChatOverlay.RowDefinitions[0].Height = new GridLength(40);
            FriendChatOverlay.RowDefinitions[1].Height = new GridLength(526);
        }
    }

    private static void SetGuideBladeBounds(FrameworkElement blade)
    {
        Canvas.SetLeft(blade, GuideBladeLeft);
        Canvas.SetTop(blade, GuideBladeTop);
        blade.Width = GuideBladeWidth;
        blade.Height = GuideBladeHeight;
    }

    public bool CloseGuide(bool playSound = false)
    {
        if (_isClosing || _isOpening || !IsVisible)
        {
            return false;
        }

        if (playSound)
        {
            _viewModel.PlaySound("back");
        }

        _isClosing = true;
        _viewModel.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            ReleaseInputCapture();
            Hide();
            Opacity = 1;
            GuideContentOffset.Y = -12;
            _isClosing = false;
            HiddenCompleted?.Invoke(this, EventArgs.Empty);
        };

        BeginAnimation(OpacityProperty, fade);
        GuideContentOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }
        });
        return true;
    }

    public bool HandleInput(DashboardInputAction action)
    {
        if (_viewModel.IsGuideMusicPickerScreen)
        {
            switch (action)
            {
                case DashboardInputAction.MoveUp:
                case DashboardInputAction.MoveDown:
                case DashboardInputAction.MoveLeft:
                case DashboardInputAction.MoveRight:
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

                    CloseGuide(true);
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

                RememberCommunitySwipeDirection(-1);
                if (_viewModel.SwitchCommunityTab(-1))
                {
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

                RememberCommunitySwipeDirection(1);
                if (_viewModel.SwitchCommunityTab(1))
                {
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

                CloseGuide(true);
                return true;
            case DashboardInputAction.Details:
                _viewModel.HandleFooterX();
                return true;
            case DashboardInputAction.Search:
                _viewModel.HandleFooterY();
                return true;
            case DashboardInputAction.Guide:
                CloseGuide(true);
                return true;
            default:
                return true;
        }
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.IsFriendSearchScreen)
        {
            if (e.Key == Key.Back)
            {
                _viewModel.BackspaceFriendSearchFromKeyboard();
                _viewModel.PlaySound("select");
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                _viewModel.ConfirmFriendSearch();
                _viewModel.PlaySound("select");
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (_viewModel.HandleBack())
                {
                    e.Handled = true;
                }
            }
        }

        if (e.Handled)
        {
            FocusGuideMenu();
            return;
        }

        if (_viewModel.IsFriendSearchScreen && IsPhysicalTypingKey(e.Key))
        {
            return;
        }

        if (_viewModel.IsGuideMusicPickerScreen)
        {
            if (e.Key is Key.Up or Key.W)
            {
                TryMoveGuideMusicFocus(DashboardInputAction.MoveUp);
                e.Handled = true;
            }
            else if (e.Key is Key.Down or Key.S)
            {
                TryMoveGuideMusicFocus(DashboardInputAction.MoveDown);
                e.Handled = true;
            }
            else if (e.Key is Key.Left or Key.A)
            {
                TryMoveGuideMusicFocus(DashboardInputAction.MoveLeft);
                e.Handled = true;
            }
            else if (e.Key is Key.Right or Key.D)
            {
                TryMoveGuideMusicFocus(DashboardInputAction.MoveRight);
                e.Handled = true;
            }
            else if (e.Key is Key.Enter or Key.Space)
            {
                ActivateFocusedGuideMusicControl();
                e.Handled = true;
            }
            else if (e.Key is Key.Escape or Key.Back)
            {
                if (_viewModel.HandleBack())
                {
                    FocusGuideMenu();
                    e.Handled = true;
                }
                else
                {
                    CloseGuide(true);
                    e.Handled = true;
                }
            }
            else if (e.Key is Key.X)
            {
                if (_viewModel.Dashboard.OpenMusicVisualizerFullscreenCommand.CanExecute(null))
                {
                    _viewModel.Dashboard.OpenMusicVisualizerFullscreenCommand.Execute(null);
                }

                e.Handled = true;
            }

            return;
        }

        if (e.Key is Key.Up or Key.W)
        {
            _viewModel.Move(-1);
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.S)
        {
            _viewModel.Move(1);
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.A)
        {
            if (!_viewModel.TryHandleHorizontal(-1))
            {
                _viewModel.MoveTab(-1);
            }
            e.Handled = true;
        }
        else if (e.Key is Key.Right or Key.D)
        {
            if (!_viewModel.TryHandleHorizontal(1))
            {
                _viewModel.MoveTab(1);
            }
            e.Handled = true;
        }
        else if (e.Key is Key.Q)
        {
            _viewModel.MoveTab(-1);
            e.Handled = true;
        }
        else if (e.Key is Key.E)
        {
            _viewModel.MoveTab(1);
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Space)
        {
            _viewModel.ActivateSelected();
            e.Handled = true;
        }
        else if (e.Key is Key.Escape or Key.Back)
        {
            if (_viewModel.HandleBack())
            {
                e.Handled = true;
            }
            else
            {
                CloseGuide(true);
                e.Handled = true;
            }
        }
        else if (e.Key is Key.X)
        {
            _viewModel.HandleFooterX();
            e.Handled = true;
        }
        else if (e.Key is Key.Y)
        {
            _viewModel.HandleFooterY();
            e.Handled = true;
        }

        if (e.Handled)
        {
            FocusGuideMenu();
        }
    }

    private void Window_OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_viewModel.IsFriendSearchScreen || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        foreach (var character in e.Text)
        {
            if (!char.IsControl(character))
            {
                _viewModel.AppendFriendSearchCharacter(character.ToString());
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

        if (_viewModel.IsAchievementsScreen)
        {
            FocusAchievements();
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
        var item = GuideMenu.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedIndex) as ListBoxItem;
        item?.Focus();
        AnimateSelectedListItem(item, _viewModel.SelectedIndex, ref _lastAnimatedMenuIndex);
    }

    private void FocusFriendsList()
    {
        FriendsOverlayListBox.UpdateLayout();
        FriendsOverlayListBox.Focus();
        var item = FriendsOverlayListBox.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedFriendListIndex) as ListBoxItem;
        item?.Focus();
        AnimateSelectedListItem(item, _viewModel.SelectedFriendListIndex, ref _lastAnimatedFriendListIndex);
    }

    private void FocusPartyRows()
    {
        PartyOverlayListBox.UpdateLayout();
        PartyOverlayListBox.Focus();
        var item = PartyOverlayListBox.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedPartyRowIndex) as ListBoxItem;
        item?.Focus();
        AnimateSelectedListItem(item, _viewModel.SelectedPartyRowIndex, ref _lastAnimatedPartyRowIndex);
    }

    private void FocusSearchKeys()
    {
        var button = FindSearchKeyButton(_viewModel.SelectedSearchKeyIndex);
        if (button is null)
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
        var item = FriendProfileOverlayActionsList.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedFriendProfileActionIndex) as ListBoxItem;
        item?.Focus();
        AnimateSelectedListItem(item, _viewModel.SelectedFriendProfileActionIndex, ref _lastAnimatedFriendProfileActionIndex);
    }

    private void FocusAchievements()
    {
        if (_viewModel.IsAchievementGameList)
        {
            if (_viewModel.SelectedAchievementGameIndex >= 0 && _viewModel.SelectedAchievementGameIndex < _viewModel.AchievementGameItems.Count)
            {
                AchievementsGameListBox.ScrollIntoView(_viewModel.AchievementGameItems[_viewModel.SelectedAchievementGameIndex]);
            }

            AchievementsGameListBox.UpdateLayout();
            AchievementsGameListBox.Focus();
            var gameItem = AchievementsGameListBox.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedAchievementGameIndex) as ListBoxItem;
            gameItem?.Focus();
            AnimateSelectedListItem(gameItem, _viewModel.SelectedAchievementGameIndex, ref _lastAnimatedAchievementIndex);
            return;
        }

        if (_viewModel.SelectedAchievementIndex >= 0 && _viewModel.SelectedAchievementIndex < _viewModel.AchievementItems.Count)
        {
            AchievementsOverlayListBox.ScrollIntoView(_viewModel.AchievementItems[_viewModel.SelectedAchievementIndex]);
        }

        AchievementsOverlayListBox.UpdateLayout();
        AchievementsOverlayListBox.Focus();
        var item = AchievementsOverlayListBox.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedAchievementIndex) as ListBoxItem;
        item?.BringIntoView();
        item?.Focus();
        AnimateSelectedListItem(item, _viewModel.SelectedAchievementIndex, ref _lastAnimatedAchievementIndex);
    }

    private void FocusGuideMusicOverlay()
    {
        TryFocus(FindFocusableControl(GuideMusicOverlay));
    }

    private void FocusMediaSubmenu()
    {
        MediaSubmenuList.UpdateLayout();
        MediaSubmenuList.Focus();
        var item = MediaSubmenuList.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedMediaSubmenuIndex) as ListBoxItem;
        item?.Focus();
        AnimateSelectedListItem(item, _viewModel.SelectedMediaSubmenuIndex, ref _lastAnimatedMediaSubmenuIndex);
    }

    private void FocusMediaTransport()
    {
        MediaTransportItems.UpdateLayout();
        var container = MediaTransportItems.ItemContainerGenerator.ContainerFromIndex(_viewModel.SelectedMediaControlIndex) as DependencyObject;
        var button = FindVisualChild<Button>(container);
        if (button is not null)
        {
            button.Focus();
            AnimateSelectedButton(button, _viewModel.SelectedMediaControlIndex, ref _lastAnimatedMediaControlIndex);
            return;
        }

        GuideMenu.Focus();
    }

    private void BeginOpenAnimation()
    {
        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(135))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) => _isOpening = false;
        BeginAnimation(OpacityProperty, fade);

        GuideContentOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void ForceForegroundAndCaptureInput()
    {
        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle == IntPtr.Zero)
        {
            handle = helper.EnsureHandle();
        }

        var foreground = GetForegroundWindow();
        var currentThreadId = GetCurrentThreadId();
        var foregroundThreadId = foreground != IntPtr.Zero
            ? GetWindowThreadProcessId(foreground, out _)
            : 0;
        var attached = false;

        try
        {
            if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
            {
                attached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            Activate();
            BringWindowToTop(handle);
            SetForegroundWindow(handle);
            SetActiveWindow(handle);
            SetFocus(handle);
            Focus();
            Keyboard.Focus(GuideRoot);
            Mouse.Capture(GuideRoot, CaptureMode.SubTree);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }
    }

    private static void ReleaseInputCapture()
    {
        if (Mouse.Captured is not null)
        {
            Mouse.Capture(null);
        }
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GuideViewModel.CurrentTabTitle))
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

        if (e.PropertyName == nameof(GuideViewModel.IsMediaSubmenuOpen) && _viewModel.IsMediaSubmenuOpen)
        {
            BeginMediaSubmenuOpenAnimation();
            Dispatcher.BeginInvoke(FocusGuideMenu);
        }

        if (e.PropertyName == nameof(GuideViewModel.IsFriendsListScreen) && _viewModel.IsFriendsListScreen)
        {
            var direction = _pendingCommunitySwipeDirection;
            _lastAnimatedFriendListIndex = -1;
            BeginCommunityOverlaySwitchAnimation(FriendsListOverlay, FriendsListOverlayOffset, GetCommunityOverlayStartX(18), direction);
            BeginCommunityTabStripAnimation(FriendsCommunityTabStrip, FriendsActiveCommunityTab, FriendsActiveCommunityTabOffset, direction);
            _pendingCommunitySwipeDirection = 0;
            Dispatcher.BeginInvoke(FocusGuideMenu);
        }

        if (e.PropertyName == nameof(GuideViewModel.IsPartyScreen) && _viewModel.IsPartyScreen)
        {
            var direction = _pendingCommunitySwipeDirection;
            _lastAnimatedPartyRowIndex = -1;
            BeginCommunityOverlaySwitchAnimation(PartyOverlay, PartyOverlayOffset, GetCommunityOverlayStartX(18), direction);
            BeginCommunityTabStripAnimation(PartyCommunityTabStrip, PartyActiveCommunityTab, PartyActiveCommunityTabOffset, direction);
            _pendingCommunitySwipeDirection = 0;
            Dispatcher.BeginInvoke(FocusGuideMenu);
        }

        if (e.PropertyName == nameof(GuideViewModel.IsFriendSearchScreen) && _viewModel.IsFriendSearchScreen)
        {
            _lastAnimatedSearchKeyIndex = -1;
            BeginOverlayOpenAnimation(FriendSearchOverlay, FriendSearchOverlayOffset, 18);
            Dispatcher.BeginInvoke(FocusGuideMenu);
        }

        if (e.PropertyName == nameof(GuideViewModel.IsFriendProfileScreen) && _viewModel.IsFriendProfileScreen)
        {
            _lastAnimatedFriendProfileActionIndex = -1;
            BeginOverlayOpenAnimation(FriendProfileOverlay, FriendProfileOverlayOffset, 16);
            Dispatcher.BeginInvoke(FocusGuideMenu);
        }

        if (e.PropertyName == nameof(GuideViewModel.IsFriendChatScreen) && _viewModel.IsFriendChatScreen)
        {
            BeginOverlayOpenAnimation(FriendChatOverlay, FriendChatOverlayOffset, 16);
            Dispatcher.BeginInvoke(new Action(() => FriendChatScroll.ScrollToEnd()));
            Dispatcher.BeginInvoke(FocusGuideMenu);
        }

        if (e.PropertyName == nameof(GuideViewModel.IsGuideMusicPickerScreen) && _viewModel.IsGuideMusicPickerScreen)
        {
            Dispatcher.BeginInvoke(FocusGuideMenu);
        }
    }

    private void BeginMediaSubmenuOpenAnimation()
    {
        MediaSubmenuBlade.Opacity = 0;
        MediaSubmenuOffset.X = 22;
        MediaSubmenuBlade.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(105))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        });
        MediaSubmenuOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(145))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static void BeginOverlayOpenAnimation(FrameworkElement element, TranslateTransform offset, double fromX)
    {
        element.BeginAnimation(OpacityProperty, null);
        offset.BeginAnimation(TranslateTransform.XProperty, null);
        element.Opacity = 0;
        offset.X = fromX;

        element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        });
        offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(145))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static void BeginCommunityOverlaySwitchAnimation(FrameworkElement element, TranslateTransform offset, double fromX, int direction)
    {
        if (direction == 0)
        {
            BeginOverlayOpenAnimation(element, offset, fromX);
            return;
        }

        element.BeginAnimation(OpacityProperty, null);
        offset.BeginAnimation(TranslateTransform.XProperty, null);
        element.Opacity = 1;
        offset.X = fromX;

        offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void RememberCommunitySwipeDirection(int direction)
    {
        if ((_viewModel.IsFriendsListScreen && direction < 0) ||
            (_viewModel.IsPartyScreen && direction > 0))
        {
            _pendingCommunitySwipeDirection = direction;
        }
    }

    private double GetCommunityOverlayStartX(double fallback)
    {
        if (_pendingCommunitySwipeDirection == 0)
        {
            return fallback;
        }

        return _pendingCommunitySwipeDirection < 0 ? -170 : 170;
    }

    private static void BeginCommunityTabStripAnimation(
        FrameworkElement tabStrip,
        FrameworkElement activeTab,
        TranslateTransform activeTabOffset,
        int direction)
    {
        tabStrip.BeginAnimation(OpacityProperty, null);
        tabStrip.Opacity = 0.96;
        tabStrip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(210))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        });

        activeTab.BeginAnimation(OpacityProperty, null);
        activeTabOffset.BeginAnimation(TranslateTransform.XProperty, null);

        if (direction == 0)
        {
            activeTab.Opacity = 1;
            activeTabOffset.X = 0;
            return;
        }

        activeTab.Opacity = 1;
        activeTabOffset.X = direction > 0 ? -130 : 130;
        activeTab.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        });
        activeTabOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(290))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void BeginBladeTransition(int direction)
    {
        if (direction == 0)
        {
            return;
        }

        GuideBladePanel.BeginAnimation(OpacityProperty, null);
        GuideBladeOffset.BeginAnimation(TranslateTransform.XProperty, null);
        GuideBladePanel.Opacity = 0.92;
        GuideBladeOffset.X = direction > 0 ? 26 : -26;

        GuideBladePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        });
        GuideBladeOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(155))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static void AnimateSelectedListItem(ListBoxItem? item, int selectedIndex, ref int lastAnimatedIndex)
    {
        if (item is null || selectedIndex == lastAnimatedIndex)
        {
            return;
        }

        lastAnimatedIndex = selectedIndex;
        AnimateFocusNudge(item);
    }

    private static void AnimateSelectedButton(Button? button, int selectedIndex, ref int lastAnimatedIndex)
    {
        if (button is null || selectedIndex == lastAnimatedIndex)
        {
            return;
        }

        lastAnimatedIndex = selectedIndex;
        AnimateFocusNudge(button);
    }

    private static void AnimateFocusNudge(UIElement element)
    {
        var offset = element.RenderTransform as TranslateTransform;
        if (offset is null)
        {
            offset = new TranslateTransform();
            element.RenderTransform = offset;
        }

        element.BeginAnimation(OpacityProperty, null);
        offset.BeginAnimation(TranslateTransform.XProperty, null);
        element.Opacity = 0.96;
        offset.X = 5;

        element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(85))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        });
        offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(105))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
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

    private void FriendsPartyTab_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RememberCommunitySwipeDirection(-1);
        if (_viewModel.SwitchCommunityTab(-1))
        {
            e.Handled = true;
            FocusGuideMenu();
        }
    }

    private void PartyFriendsTab_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RememberCommunitySwipeDirection(1);
        if (_viewModel.SwitchCommunityTab(1))
        {
            e.Handled = true;
            FocusGuideMenu();
        }
    }

    private void GuideMenu_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            return;
        }

        GuideMenu.SelectedItem = item.DataContext;
        _viewModel.ActivateSelected();
        FocusGuideMenu();
    }

    private void FriendsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: not null } listBox)
        {
            listBox.ScrollIntoView(listBox.SelectedItem);
        }
    }

    private void FriendsList_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not GuideFriendListItem friend)
        {
            return;
        }

        FriendsOverlayListBox.SelectedItem = friend;
        _viewModel.ActivateFriendListItem(friend);
        FocusGuideMenu();
    }

    private void FriendProfileActions_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not GuideMenuItem action)
        {
            return;
        }

        FriendProfileOverlayActionsList.SelectedItem = action;
        _viewModel.ActivateFriendProfileAction(action);
        FocusGuideMenu();
    }

    private void PartyRows_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not GuidePartyRowItem row || !row.IsSelectable)
        {
            return;
        }

        PartyOverlayListBox.SelectedItem = row;
        _viewModel.ActivatePartyRowItem(row);
        FocusGuideMenu();
    }

    private void DisableMouseWheelScroll(object sender, MouseWheelEventArgs e)
        => e.Handled = true;

    private void MediaSubmenu_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            return;
        }

        MediaSubmenuList.SelectedItem = item.DataContext;
        _viewModel.ActivateSelected();
        FocusGuideMenu();
    }

    private void GuideMusicPicker_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

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

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private bool TryMoveGuideMusicFocus(DashboardInputAction action)
    {
        var controls = GetOverlayFocusCandidates(GuideMusicOverlay);
        if (controls.Count == 0)
        {
            return false;
        }

        if (Keyboard.FocusedElement is not Control currentControl
            || !controls.Any(candidate => ReferenceEquals(candidate.Control, currentControl)))
        {
            return TryFocus(controls[0].Control);
        }

        var current = controls.First(candidate => ReferenceEquals(candidate.Control, currentControl));
        var currentCenter = GetCenter(current.Bounds);
        var direction = action switch
        {
            DashboardInputAction.MoveLeft => new Vector(-1, 0),
            DashboardInputAction.MoveRight => new Vector(1, 0),
            DashboardInputAction.MoveUp => new Vector(0, -1),
            DashboardInputAction.MoveDown => new Vector(0, 1),
            _ => new Vector(0, 0)
        };

        var next = controls
            .Where(candidate => !ReferenceEquals(candidate.Control, currentControl))
            .Select(candidate => new
            {
                Candidate = candidate,
                Center = GetCenter(candidate.Bounds)
            })
            .Select(item => new
            {
                item.Candidate,
                Delta = item.Center - currentCenter,
                Primary = action is DashboardInputAction.MoveLeft or DashboardInputAction.MoveRight
                    ? Math.Abs(item.Center.X - currentCenter.X)
                    : Math.Abs(item.Center.Y - currentCenter.Y),
                Secondary = action is DashboardInputAction.MoveLeft or DashboardInputAction.MoveRight
                    ? Math.Abs(item.Center.Y - currentCenter.Y)
                    : Math.Abs(item.Center.X - currentCenter.X)
            })
            .Where(item => Vector.Multiply(item.Delta, direction) > 1)
            .OrderBy(item => item.Secondary * 2.2 + item.Primary)
            .ThenBy(item => item.Primary)
            .FirstOrDefault();

        return next is null ? false : TryFocus(next.Candidate.Control);
    }

    private void ActivateFocusedGuideMusicControl()
    {
        if (Keyboard.FocusedElement is not Button button)
        {
            return;
        }

        if (button.Command is not null)
        {
            if (button.Command.CanExecute(button.CommandParameter))
            {
                button.Command.Execute(button.CommandParameter);
            }

            return;
        }

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static List<OverlayFocusCandidate> GetOverlayFocusCandidates(FrameworkElement overlay)
    {
        try
        {
            return FindVisualChildren<Control>(overlay)
                .Where(control => control.IsVisible && control.IsEnabled && control.Focusable)
                .Select(control => new OverlayFocusCandidate(control, GetElementBounds(control, overlay)))
                .Where(candidate => candidate.Bounds.Width > 0 && candidate.Bounds.Height > 0)
                .Where(candidate =>
                {
                    var center = GetCenter(candidate.Bounds);
                    return center.X >= 0
                           && center.Y >= 0
                           && center.X <= overlay.ActualWidth
                           && center.Y <= overlay.ActualHeight;
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static Rect GetElementBounds(FrameworkElement element, Visual relativeTo)
    {
        try
        {
            var transform = element.TransformToAncestor(relativeTo);
            return transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
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
        => new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

    private static UIElement? FindFocusableControl(DependencyObject? root)
        => FindVisualChildren<UIElement>(root)
            .FirstOrDefault(element =>
                element.IsVisible
                && element.Focusable
                && (element is not Control control || control.IsEnabled));

    private bool TryFocus(UIElement? element)
    {
        if (element is null || !element.IsVisible || !element.Focusable)
        {
            return false;
        }

        if (element is Control { IsEnabled: false })
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

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool IsPhysicalTypingKey(Key key)
        => key is >= Key.A and <= Key.Z
           or >= Key.D0 and <= Key.D9
           or >= Key.NumPad0 and <= Key.NumPad9
           or Key.Space
           or Key.OemMinus
           or Key.Subtract
           or Key.OemPeriod
           or Key.Decimal
           or Key.Oem2
           or Key.Oem7;

    private Button? FindSearchKeyButton(int index)
    {
        if (index < 0)
        {
            return null;
        }

        if (index < 40)
        {
            FriendSearchOverlayMainKeysItems.UpdateLayout();
            var container = FriendSearchOverlayMainKeysItems.ItemContainerGenerator.ContainerFromIndex(index) as DependencyObject;
            return FindVisualChild<Button>(container);
        }

        return index switch
        {
            40 => FriendSearchCapsButton,
            41 => FriendSearchBackspaceButton,
            42 => FriendSearchSpaceButton,
            43 => FriendSearchDoneButton,
            _ => null
        };
    }

    private readonly record struct OverlayFocusCandidate(Control Control, Rect Bounds);
}
