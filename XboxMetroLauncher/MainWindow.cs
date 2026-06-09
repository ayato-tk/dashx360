using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using XboxMetroLauncher.Controls;
using XboxMetroLauncher.Input;
using XboxMetroLauncher.Services;
using XboxMetroLauncher.Themes;
using XboxMetroLauncher.Utilities;
using XboxMetroLauncher.ViewModels;
using XboxMetroLauncher.ViewModels.Tabs;
using XboxMetroLauncher.Views;

namespace XboxMetroLauncher;

public class MainWindow : Window, IComponentConnector
{
	private readonly record struct FocusCandidate(System.Windows.Controls.Button Button, Rect Bounds);

	private readonly record struct OverlayFocusCandidate(System.Windows.Controls.Control Control, Rect Bounds);

	private readonly DashboardViewModel _viewModel;

	private readonly ControllerInputService _controllerInputService;

	private readonly GlobalHotkeyService _guideHotkeyService;

	private readonly IAudioService _audioService;

	private readonly IFriendsService _friendsService;

	private readonly SocialIntegrationManager _socialIntegrationManager;

	private readonly DiscordPartyService _discordPartyService;

	private readonly DispatcherTimer _clockTimer;

	private readonly DispatcherTimer _performanceDebugTimer;

	private readonly Dictionary<string, System.Windows.Controls.Button> _lastFocusedButtonByTab = new Dictionary<string, System.Windows.Controls.Button>();

	private System.Windows.Forms.WebBrowser? _bootBrowser;

	private DispatcherTimer? _bootStateTimer;

	private DateTime _bootStartedAt;

	private int _lastTabIndex = 1;

	private const double FocusZoneLeft = 64.0;

	private const double FocusZoneRight = 1120.0;

	private bool _bootSkipped;

	private bool _isAnimatingTab;

	private bool _isFocusUpdateQueued;

	private int _queuedTabStep;

	private object? _lastRenderedTab;

	private GuideWindow? _guideWindow;

	private GuideViewModel? _guideViewModel;

	private UIElement? _guideReturnFocusElement;

	private bool _restoreFocusAfterGuideClose;

	private nint _guideReturnWindowHandle;

	private bool _guideRestoreExternalWindow;

	private string _appliedThemeBackgroundPath = string.Empty;

	private string _appliedBingBackgroundPath = string.Empty;

	private static readonly string BingBackgroundRelativePath = Path.Combine("Assets", "References", "penguin_bing_background.png");

	private static readonly string PerformanceDebugLogPath = Path.Combine(AppPaths.LogsFolder, "performance-debug.log");

	internal Grid AudioHost;

	internal Grid RootGrid;

	internal Grid ThemeBackgroundLayer;

	internal Image ThemeBackgroundImage;

	internal Grid BingBackgroundLayer;

	internal Image BingBackgroundImage;

	internal Grid ContentFrame;

	internal ContentControl ContentHost;

	internal TranslateTransform ContentSlide;

	internal Canvas TabTransitionLayer;

	internal Canvas TabTransitionStrip;

	internal TranslateTransform TabTransitionSlide;

	internal ContentControl TransitionLeftHost;

	internal ContentControl TransitionCenterHost;

	internal ContentControl TransitionRightHost;

	internal Canvas AdjacentPreviewLayer;

	internal TranslateTransform PreviousPreviewOffset;

	internal Image PreviousPreviewImage;

	internal Canvas PreviousPreviewLiveLayer;

	internal ContentControl PreviousPreviewLiveHost;

	internal TranslateTransform NextPreviewOffset;

	internal Image NextPreviewImage;

	internal Canvas NextPreviewLiveLayer;

	internal ContentControl NextPreviewLiveHost;

	internal Canvas DebugGuides;

	internal System.Windows.Controls.TextBox SearchOverlayTextBox;

	internal Grid MyGamesOverlay;

	internal Grid MyGamesStrip;

	internal TranslateTransform MyGamesStripTranslate;

	internal Grid LauncherSettingsOverlay;

	internal ScrollViewer LauncherSettingsLeftScrollViewer;

	internal ScrollViewer LauncherSettingsRightScrollViewer;

	internal Grid ThemeMenuOverlay;

	internal Grid ThemeCreatorOverlay;

	internal System.Windows.Controls.Button ChooseThemeHomeBackgroundButton;

	internal Grid MusicPlayerOverlay;

	internal Grid MusicPlayerContentGrid;

	internal Grid ProfileEditorOverlay;

	internal Grid BootOverlay;

	internal WindowsFormsHost BootVideoHost;

	private bool _contentLoaded;

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool BringWindowToTop(nint hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindow(nint hWnd);

	public MainWindow()
	{
		InitializeComponent();
		string writableAppDataPath = GetWritableAppDataPath();
		MigrateLegacyUserData(writableAppDataPath);
		JsonStore jsonStore = new JsonStore(writableAppDataPath);
		_friendsService = new FriendsService(jsonStore);
		_socialIntegrationManager = new SocialIntegrationManager(localService: new LocalSocialIntegrationService(_friendsService), friendsService: _friendsService);
		_discordPartyService = new DiscordPartyService();
		ISettingsService settingsService = new SettingsService(jsonStore);
		IProfileService profileService = new ProfileService(jsonStore);
		IGameLibraryService libraryService = new JsonGameLibraryService(jsonStore);
		IImportExportService importExportService = new ImportExportService(libraryService, profileService, settingsService, writableAppDataPath);
		IRunningGameService runningGameService = new RunningGameService();
		DashboardViewModel viewModel = null;
		_viewModel = new DashboardViewModel(audioService: _audioService = new AudioService(() => viewModel?.Settings.PlayUiSounds ?? true, AudioHost), libraryService: libraryService, launchService: new GameLaunchService(), searchService: new SearchService(), settingsService: settingsService, profileService: profileService, filePickerService: new WindowsFilePickerService(), importExportService: importExportService, steamLibraryScannerService: new SteamLibraryScannerService(), themeService: new ThemeService(), startupRegistrationService: new RegistryStartupRegistrationService(), socialIntegrationManager: _socialIntegrationManager, runningGameService: runningGameService);
		viewModel = _viewModel;
		base.DataContext = _viewModel;
		_lastRenderedTab = _viewModel.CurrentTab;
		_controllerInputService = new ControllerInputService(HandleInputAction, () => _viewModel.Settings.EnableControllerInput);
		_guideHotkeyService = new GlobalHotkeyService();
		_guideHotkeyService.HotkeyPressed += delegate
		{
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				HandleInputAction(DashboardInputAction.Guide);
			}, (DispatcherPriority)10, Array.Empty<object>());
		};
		_clockTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(15.0)
		};
		_clockTimer.Tick += delegate
		{
			_viewModel.UpdateClock();
		};
		_performanceDebugTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(20.0)
		};
		_performanceDebugTimer.Tick += delegate
		{
			WritePerformanceDebugReport();
		};
		_viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
		_viewModel.FriendsOverlayRequested += ViewModel_OnFriendsOverlayRequested;
		ApplyDisplaySettings();
	}

	private static string GetWritableAppDataPath()
	{
		return AppPaths.UserDataFolder;
	}

	private static void MigrateLegacyUserData(string targetRoot)
	{
		if (!Directory.Exists(targetRoot))
		{
			Directory.CreateDirectory(targetRoot);
		}
		if (Directory.EnumerateFiles(targetRoot, "*.json", SearchOption.TopDirectoryOnly).Any())
		{
			return;
		}
		foreach (string item in AppPaths.LegacyDataRoots())
		{
			if (!Directory.Exists(item))
			{
				continue;
			}
			List<string> list = Directory.EnumerateFiles(item, "*.json", SearchOption.TopDirectoryOnly).ToList();
			if (list.Count == 0)
			{
				continue;
			}
			{
				foreach (string item2 in list)
				{
					string text = Path.Combine(targetRoot, Path.GetFileName(item2));
					if (!File.Exists(text))
					{
						File.Copy(item2, text, overwrite: false);
					}
				}
				break;
			}
		}
	}

	private async void Window_OnLoaded(object sender, RoutedEventArgs e)
	{
		try
		{
			ApplyDisplaySettings();
			_controllerInputService.Start();
			_guideHotkeyService.Register(this);
			_clockTimer.Start();
			_performanceDebugTimer.Start();
			StartBootVideo();
			await _viewModel.InitializeAsync();
			_viewModel.Settings.PropertyChanged += Settings_OnPropertyChanged;
			UpdateThemeBackgroundVisual(animate: false);
			UpdateBingBackgroundVisual(animate: false);
			ApplyDisplaySettings();
			UpdateAdjacentPreviewSnapshots();
			WritePerformanceDebugReport();
			FocusFirstButton();
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.Window_OnLoaded");
		}
	}

	private void Window_OnClosing(object? sender, CancelEventArgs e)
	{
		_viewModel.Settings.PropertyChanged -= Settings_OnPropertyChanged;
		_viewModel.FriendsOverlayRequested -= ViewModel_OnFriendsOverlayRequested;
		_guideWindow?.Close();
		_guideViewModel?.Dispose();
		_guideHotkeyService.Dispose();
		_controllerInputService.Dispose();
		_clockTimer.Stop();
		_performanceDebugTimer.Stop();
		CleanupBootBrowser();
		WritePerformanceDebugReport();
	}

	private void Settings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		string propertyName = e.PropertyName;
		if (propertyName == "DisplayResolution" || propertyName == "StartFullscreen")
		{
			ApplyDisplaySettings();
		}
	}

	private void ApplyDisplaySettings()
	{
		var (val, val2) = _viewModel.Settings.DisplayResolution switch
		{
			"720p" => (1280.0, 720.0), 
			"1440p" => (2560.0, 1440.0), 
			"4K" => (3840.0, 2160.0), 
			_ => (1920.0, 1080.0), 
		};
		if (_viewModel.Settings.StartFullscreen)
		{
			base.WindowStyle = WindowStyle.None;
			base.ResizeMode = ResizeMode.NoResize;
			base.WindowState = WindowState.Maximized;
			return;
		}
		base.WindowState = WindowState.Normal;
		base.WindowStyle = WindowStyle.SingleBorderWindow;
		base.ResizeMode = ResizeMode.CanResize;
		Rect workArea = SystemParameters.WorkArea;
		base.Width = Math.Min(val, workArea.Width);
		base.Height = Math.Min(val2, workArea.Height);
		base.Left = workArea.Left + Math.Max(0.0, (workArea.Width - base.Width) / 2.0);
		base.Top = workArea.Top + Math.Max(0.0, (workArea.Height - base.Height) / 2.0);
	}

	private void Window_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		DashboardInputAction action;
		if (_viewModel.IsBooting)
		{
			SkipBootIntro();
			e.Handled = true;
		}
		else if (DashboardInputRouter.TryMapKey(e, out action))
		{
			e.Handled = true;
			HandleInputAction(action);
		}
	}

	private void Window_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_viewModel.IsBooting)
		{
			SkipBootIntro();
			e.Handled = true;
		}
	}

	private void HandleInputAction(DashboardInputAction action)
	{
		try
		{
			HandleInputActionCore(action);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.HandleInputAction");
			_isAnimatingTab = false;
			_isFocusUpdateQueued = false;
		}
	}

	private void HandleInputActionCore(DashboardInputAction action)
	{
		if (action == DashboardInputAction.Guide)
		{
			GuideWindow? guideWindow = _guideWindow;
			if (guideWindow == null || !guideWindow.IsTransitioning)
			{
				GuideWindow? guideWindow2 = _guideWindow;
				if (guideWindow2 != null && guideWindow2.IsGuideOpen)
				{
					HideGuide();
				}
				else
				{
					ShowGuide();
				}
			}
			return;
		}
		GuideWindow? guideWindow3 = _guideWindow;
		if (guideWindow3 != null && guideWindow3.IsGuideOpen)
		{
			_guideWindow.HandleInput(action);
		}
		else
		{
			if (base.WindowState == WindowState.Minimized || !base.IsVisible || !base.IsActive)
			{
				return;
			}
			if (_viewModel.IsBooting)
			{
				SkipBootIntro();
				return;
			}
			bool flag = IsOverlayOpen();
			if (flag)
			{
				bool flag2 = (uint)(action - 12) <= 1u;
				flag = flag2;
			}
			if (flag)
			{
				return;
			}
			flag = _isAnimatingTab;
			if (flag)
			{
				bool flag2 = ((uint)action <= 1u || (uint)(action - 12) <= 1u);
				flag = flag2;
			}
			if (flag)
			{
				if (!IsOverlayOpen())
				{
					flag = (action == DashboardInputAction.MoveLeft || action == DashboardInputAction.PreviousTab);
					_queuedTabStep = ((!flag) ? 1 : (-1));
				}
				return;
			}
			if ((uint)(action - 12) <= 1u)
			{
				RememberFocusedButton();
			}
			if ((uint)action <= 3u)
			{
				if (TryRestoreOverlayFocus())
				{
					_viewModel.HandleInput(action);
					return;
				}
				bool flag3 = TryMoveDashboardFocus(action);
				if (!flag3 && action == DashboardInputAction.MoveLeft)
				{
					if (!IsOverlayOpen())
					{
						_viewModel.MoveTab(-1);
					}
				}
				else if (!flag3 && action == DashboardInputAction.MoveRight && !IsOverlayOpen())
				{
					_viewModel.MoveTab(1);
				}
				_viewModel.HandleInput(action);
			}
			else
			{
				if (action == DashboardInputAction.Activate && TryRestoreOverlayFocus())
				{
					return;
				}
				if (action == DashboardInputAction.Activate && Keyboard.FocusedElement is System.Windows.Controls.TextBox textBox)
				{
					textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
					if ((_viewModel.IsSearchOverlayOpen || _viewModel.CurrentTab?.Key == "bing") && _viewModel.SubmitSearchCommand.CanExecute(null))
					{
						_viewModel.SubmitSearchCommand.Execute(null);
					}
					return;
				}
				if (action == DashboardInputAction.Activate && DashboardInputRouter.ActivateFocusedElement())
				{
					_viewModel.HandleInput(action);
					return;
				}
				_viewModel.HandleInput(action);
				if (action != DashboardInputAction.Search)
				{
					return;
				}
				((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)delegate
				{
					try
					{
						SearchOverlayTextBox.Focus();
						SearchOverlayTextBox.SelectAll();
					}
					catch
					{
					}
				}, (DispatcherPriority)4, Array.Empty<object>());
			}
		}
	}

	private void ShowGuide()
	{
		try
		{
			EnsureGuideWindow();
			if (_guideWindow != null && !_guideWindow.IsTransitioning && !_guideWindow.IsGuideOpen)
			{
				RememberGuideReturnFocus();
				_guideWindow.Open();
			}
		}
		catch (InvalidOperationException)
		{
			EnsureGuideWindow();
			if (_guideWindow != null && !_guideWindow.IsTransitioning && !_guideWindow.IsGuideOpen)
			{
				RememberGuideReturnFocus();
				_guideWindow.Open();
			}
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.ShowGuide");
		}
	}

	private void EnsureGuideWindow()
	{
		if (_guideWindow != null && _guideWindow.IsLoaded && PresentationSource.FromVisual(_guideWindow) != null)
		{
			return;
		}
		if (_guideWindow != null)
		{
			_guideWindow.HiddenCompleted -= GuideWindow_OnHiddenCompleted;
			_guideWindow.Closed -= GuideWindow_OnClosed;
			try
			{
				_guideWindow.Close();
			}
			catch
			{
			}
		}
		_guideViewModel?.Dispose();
		_guideViewModel = new GuideViewModel(_viewModel, this, HideGuide, _audioService, _friendsService, _socialIntegrationManager, _discordPartyService);
		_guideWindow = new GuideWindow(_guideViewModel);
		_guideWindow.HiddenCompleted += GuideWindow_OnHiddenCompleted;
		_guideWindow.Closed += GuideWindow_OnClosed;
	}

	private void HideGuide()
	{
		try
		{
			if (_guideWindow != null && !_guideWindow.IsTransitioning && _guideWindow.IsGuideOpen)
			{
				_restoreFocusAfterGuideClose = _guideWindow.CloseGuide();
				if (!_guideRestoreExternalWindow)
				{
					Activate();
				}
			}
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.HideGuide");
		}
	}

	private void ViewModel_OnFriendsOverlayRequested(object? sender, EventArgs e)
	{
		try
		{
			OpenGuideFriendsOverlay();
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.ViewModel_OnFriendsOverlayRequested");
		}
	}

	private void OpenGuideFriendsOverlay()
	{
		EnsureGuideWindow();
		if (_guideWindow != null && _guideViewModel != null && !_guideWindow.IsTransitioning)
		{
			RememberGuideReturnFocus();
			_guideViewModel.OpenFriendsOverlayFromDashboard();
			_guideWindow.Open();
		}
	}

	private void RememberGuideReturnFocus()
	{
		nint handle = new WindowInteropHelper(this).Handle;
		nint foregroundWindow = GetForegroundWindow();
		_guideRestoreExternalWindow = foregroundWindow != IntPtr.Zero && foregroundWindow != handle;
		_guideReturnWindowHandle = (_guideRestoreExternalWindow ? foregroundWindow : IntPtr.Zero);
		_guideReturnFocusElement = (_guideRestoreExternalWindow ? null : (Keyboard.FocusedElement as UIElement));
		RememberFocusedButton();
	}

	private void GuideWindow_OnHiddenCompleted(object? sender, EventArgs e)
	{
		if (_guideRestoreExternalWindow && RestoreExternalWindowAfterGuide())
		{
			_restoreFocusAfterGuideClose = false;
			_guideRestoreExternalWindow = false;
			_guideReturnWindowHandle = IntPtr.Zero;
			_guideReturnFocusElement = null;
			return;
		}
		Activate();
		if (!_restoreFocusAfterGuideClose)
		{
			_guideRestoreExternalWindow = false;
			_guideReturnWindowHandle = IntPtr.Zero;
		}
		else
		{
			_restoreFocusAfterGuideClose = false;
			RestoreFocusAfterGuide();
		}
	}

	private void GuideWindow_OnClosed(object? sender, EventArgs e)
	{
		if (_guideWindow != null)
		{
			_guideWindow.HiddenCompleted -= GuideWindow_OnHiddenCompleted;
			_guideWindow.Closed -= GuideWindow_OnClosed;
		}
		_guideViewModel?.Dispose();
		_guideViewModel = null;
		_guideWindow = null;
	}

	private void RestoreFocusAfterGuide()
	{
		((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)delegate
		{
			try
			{
				if (IsOverlayOpen())
				{
					FocusFirstButton();
				}
				else if (_guideReturnFocusElement != null && TryFocus(_guideReturnFocusElement))
				{
					RememberFocusedButton();
				}
				else
				{
					FocusFirstButton();
				}
			}
			catch (Exception exception)
			{
				App.LogException(exception, "MainWindow.RestoreFocusAfterGuide");
			}
			finally
			{
				_guideReturnFocusElement = null;
				_guideRestoreExternalWindow = false;
				_guideReturnWindowHandle = IntPtr.Zero;
			}
		}, (DispatcherPriority)5, Array.Empty<object>());
	}

	private bool RestoreExternalWindowAfterGuide()
	{
		if (_guideReturnWindowHandle == IntPtr.Zero || !IsWindow(_guideReturnWindowHandle))
		{
			return false;
		}
		BringWindowToTop(_guideReturnWindowHandle);
		return SetForegroundWindow(_guideReturnWindowHandle);
	}

	public void PrepareGuideReturnToDashboard()
	{
		_guideRestoreExternalWindow = false;
		_guideReturnWindowHandle = IntPtr.Zero;
		_guideReturnFocusElement = null;
		_restoreFocusAfterGuideClose = false;
	}

	private void StartBootVideo()
	{
		string text = AppPaths.FindFile(Path.Combine("Assets", "Boot", "Boot Screen.mp4"));
		if (!File.Exists(text))
		{
			SkipBootIntro();
		}
		else if (!EnsureBootBrowser())
		{
			SkipBootIntro();
		}
		else
		{
			StartBrowserBootPlayback(text);
		}
	}

	private bool EnsureBootBrowser()
	{
		if (_bootBrowser != null)
		{
			return true;
		}
		try
		{
			System.Windows.Forms.WebBrowser webBrowser = new System.Windows.Forms.WebBrowser
			{
				Dock = DockStyle.Fill,
				AllowWebBrowserDrop = false,
				IsWebBrowserContextMenuEnabled = false,
				ScrollBarsEnabled = false,
				WebBrowserShortcutsEnabled = false
			};
			BootVideoHost.Child = webBrowser;
			_bootBrowser = webBrowser;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void StartBrowserBootPlayback(string bootVideoPath)
	{
		try
		{
			string absoluteUri = new Uri(bootVideoPath).AbsoluteUri;
			_bootBrowser.DocumentText = "<!doctype html>\n<html>\n<head>\n    <meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />\n    <style>\n        html, body {\n            width: 100%;\n            height: 100%;\n            margin: 0;\n            overflow: hidden;\n            background: #fff;\n        }\n        video {\n            width: 100vw;\n            height: 100vh;\n            object-fit: contain;\n            background: #fff;\n            display: block;\n        }\n    </style>\n</head>\n<body>\n    <video id=\"boot\" src=\"" + absoluteUri + "\" autoplay muted playsinline></video>\n    <script>\n        var boot = document.getElementById('boot');\n        boot.play();\n    </script>\n</body>\n</html>";
			StartBootAudio();
			_bootStartedAt = DateTime.UtcNow;
			if (_bootStateTimer == null)
			{
				_bootStateTimer = new DispatcherTimer
				{
					Interval = TimeSpan.FromMilliseconds(250.0)
				};
			}
			_bootStateTimer.Tick -= BootStateTimer_OnTick;
			_bootStateTimer.Tick += BootStateTimer_OnTick;
			_bootStateTimer.Start();
		}
		catch
		{
			SkipBootIntro();
		}
	}

	private void StartBootAudio()
	{
		if (!File.Exists(AppPaths.FindFile(Path.Combine("Assets", "Audio", "Sounds", "02. Startup (2010).mp3"))))
		{
			return;
		}
		try
		{
			_audioService.Play("startup");
		}
		catch
		{
		}
	}

	private void BootStateTimer_OnTick(object? sender, EventArgs e)
	{
		try
		{
			if ((bool?)_bootBrowser?.Document?.InvokeScript("eval", new object[1] { "document.getElementById('boot') && document.getElementById('boot').ended" }) == true || DateTime.UtcNow - _bootStartedAt > TimeSpan.FromSeconds(12.0))
			{
				SkipBootIntro();
			}
		}
		catch
		{
			if (DateTime.UtcNow - _bootStartedAt > TimeSpan.FromSeconds(12.0))
			{
				SkipBootIntro();
			}
		}
	}

	private void SkipBootIntro()
	{
		if (!_bootSkipped)
		{
			_bootSkipped = true;
			DispatcherTimer? bootStateTimer = _bootStateTimer;
			if (bootStateTimer != null)
			{
				bootStateTimer.Stop();
			}
			try
			{
				_bootBrowser?.Document?.InvokeScript("eval", new object[1] { "var v=document.getElementById('boot'); if(v){v.pause(); v.currentTime=0;}" });
			}
			catch
			{
			}
			try
			{
				_audioService.Stop("startup");
			}
			catch
			{
			}
			CleanupBootBrowser();
			_viewModel.IsBooting = false;
			QueueFocusFirstButton();
		}
	}

	private void CleanupBootBrowser()
	{
		DispatcherTimer? bootStateTimer = _bootStateTimer;
		if (bootStateTimer != null)
		{
			bootStateTimer.Stop();
		}
		if (_bootStateTimer != null)
		{
			_bootStateTimer.Tick -= BootStateTimer_OnTick;
		}
		if (_bootBrowser == null)
		{
			BootVideoHost.Child = null;
			return;
		}
		try
		{
			_bootBrowser.Stop();
		}
		catch
		{
		}
		try
		{
			_bootBrowser.DocumentText = "<html><body></body></html>";
		}
		catch
		{
		}
		try
		{
			BootVideoHost.Child = null;
		}
		catch
		{
		}
		try
		{
			_bootBrowser.Dispose();
		}
		catch
		{
		}
		_bootBrowser = null;
	}

	private void MusicFullscreenHint_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_viewModel.OpenMusicVisualizerFullscreenCommand.CanExecute(null))
		{
			_viewModel.OpenMusicVisualizerFullscreenCommand.Execute(null);
			e.Handled = true;
		}
	}

	private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "CurrentTab")
		{
			AnimateTabChange();
			UpdateThemeBackgroundVisual();
			UpdateBingBackgroundVisual();
			QueueFocusFirstButton();
			return;
		}
		bool flag;
		switch (e.PropertyName)
		{
		case "IsMyGamesOpen":
		case "IsLauncherSettingsOpen":
		case "IsProfileEditorOpen":
		case "IsThemeMenuOpen":
		case "IsThemeCreatorOpen":
		case "IsMusicPlayerOpen":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)new Action(AnimateActiveOverlayIn), (DispatcherPriority)6, Array.Empty<object>());
			UpdateThemeBackgroundVisual();
			UpdateBingBackgroundVisual();
			QueueFocusFirstButton();
		}
		else if (e.PropertyName == "CurrentThemeBackgroundPath")
		{
			UpdateThemeBackgroundVisual();
		}
	}

	private void UpdateThemeBackgroundVisual(bool animate = true)
	{
		try
		{
			string currentThemeBackgroundPath = _viewModel.CurrentThemeBackgroundPath;
			if (string.Equals(_appliedThemeBackgroundPath, currentThemeBackgroundPath, StringComparison.OrdinalIgnoreCase) && ThemeBackgroundLayer.Visibility == Visibility.Visible == !string.IsNullOrWhiteSpace(currentThemeBackgroundPath))
			{
				return;
			}
			ThemeBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, null);
			if (string.IsNullOrWhiteSpace(currentThemeBackgroundPath))
			{
				_appliedThemeBackgroundPath = string.Empty;
				if (!animate)
				{
					ThemeBackgroundLayer.Opacity = 0.0;
					ThemeBackgroundLayer.Visibility = Visibility.Collapsed;
					ThemeBackgroundImage.Source = null;
					return;
				}
				DoubleAnimation doubleAnimation = new DoubleAnimation(ThemeBackgroundLayer.Opacity, 0.0, TimeSpan.FromMilliseconds(260.0))
				{
					EasingFunction = new SineEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				doubleAnimation.Completed += delegate
				{
					ThemeBackgroundLayer.Visibility = Visibility.Collapsed;
					ThemeBackgroundImage.Source = null;
				};
				ThemeBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
				return;
			}
			BitmapSource decodedImage = ImageCacheService.GetDecodedImage(AppPathResolver.Resolve(currentThemeBackgroundPath), 1920);
			if (decodedImage == null)
			{
				_appliedThemeBackgroundPath = string.Empty;
				ThemeBackgroundLayer.Opacity = 0.0;
				ThemeBackgroundLayer.Visibility = Visibility.Collapsed;
				ThemeBackgroundImage.Source = null;
				return;
			}
			_appliedThemeBackgroundPath = currentThemeBackgroundPath;
			ThemeBackgroundLayer.Visibility = Visibility.Visible;
			ThemeBackgroundImage.Source = decodedImage;
			if (!animate)
			{
				ThemeBackgroundLayer.Opacity = 1.0;
				return;
			}
			ThemeBackgroundLayer.Opacity = 0.0;
			ThemeBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320.0))
			{
				EasingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.UpdateThemeBackgroundVisual");
		}
	}

	private void UpdateBingBackgroundVisual(bool animate = true)
	{
		try
		{
			bool flag = string.Equals(_viewModel.CurrentTab?.Key, "bing", StringComparison.OrdinalIgnoreCase);
			if (!flag && string.IsNullOrEmpty(_appliedBingBackgroundPath) && BingBackgroundLayer.Visibility != Visibility.Visible)
			{
				return;
			}
			string text = (flag ? AppPaths.ResolvePath(BingBackgroundRelativePath) : string.Empty);
			bool flag2 = BingBackgroundLayer.Visibility == Visibility.Visible;
			if (string.Equals(_appliedBingBackgroundPath, text, StringComparison.OrdinalIgnoreCase) && flag2 == flag)
			{
				return;
			}
			BingBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, null);
			if (string.IsNullOrWhiteSpace(text))
			{
				_appliedBingBackgroundPath = string.Empty;
				if (!animate)
				{
					BingBackgroundLayer.Opacity = 0.0;
					BingBackgroundLayer.Visibility = Visibility.Collapsed;
					BingBackgroundImage.Source = null;
					return;
				}
				DoubleAnimation doubleAnimation = new DoubleAnimation(BingBackgroundLayer.Opacity, 0.0, TimeSpan.FromMilliseconds(300.0))
				{
					EasingFunction = new SineEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				doubleAnimation.Completed += delegate
				{
					BingBackgroundLayer.Visibility = Visibility.Collapsed;
					BingBackgroundImage.Source = null;
				};
				BingBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
				return;
			}
			BitmapSource decodedImage = ImageCacheService.GetDecodedImage(text, 1920);
			if (decodedImage == null)
			{
				_appliedBingBackgroundPath = string.Empty;
				BingBackgroundLayer.Opacity = 0.0;
				BingBackgroundLayer.Visibility = Visibility.Collapsed;
				BingBackgroundImage.Source = null;
				return;
			}
			_appliedBingBackgroundPath = text;
			BingBackgroundLayer.Visibility = Visibility.Visible;
			BingBackgroundImage.Source = decodedImage;
			if (!animate)
			{
				BingBackgroundLayer.Opacity = 1.0;
				return;
			}
			BingBackgroundLayer.Opacity = 0.0;
			BingBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320.0))
			{
				EasingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.UpdateBingBackgroundVisual");
		}
	}

	private void AnimateTabChange()
	{
		if (_viewModel.Tabs.Count == 0)
		{
			return;
		}
		int num = ((_viewModel.CurrentTab == null) ? _lastTabIndex : _viewModel.Tabs.IndexOf(_viewModel.CurrentTab));
		if (num < 0)
		{
			num = Math.Clamp(_lastTabIndex, 0, _viewModel.Tabs.Count - 1);
		}
		object lastRenderedTab = _lastRenderedTab;
		DashboardTabViewModel currentTab = _viewModel.CurrentTab;
		if (lastRenderedTab == currentTab)
		{
			_lastTabIndex = num;
			return;
		}
		int num2 = ((num >= _lastTabIndex) ? 1 : (-1));
		_lastTabIndex = num;
		_lastRenderedTab = currentTab;
		_isAnimatingTab = true;
		_queuedTabStep = 0;
		try
		{
			ContentSlide.BeginAnimation(TranslateTransform.XProperty, null);
			ContentHost.BeginAnimation(UIElement.OpacityProperty, null);
			TabTransitionSlide.BeginAnimation(TranslateTransform.XProperty, null);
			TabTransitionLayer.BeginAnimation(UIElement.OpacityProperty, null);
			AdjacentPreviewLayer.BeginAnimation(UIElement.OpacityProperty, null);
			PreviousPreviewOffset.BeginAnimation(TranslateTransform.XProperty, null);
			NextPreviewOffset.BeginAnimation(TranslateTransform.XProperty, null);
			PrepareTabTransitionStrip(lastRenderedTab, currentTab, num2);
			PrepareLiveAdjacentPreviews();
			ContentHost.Opacity = 0.0;
			ContentSlide.X = 0.0;
			TabTransitionLayer.Visibility = Visibility.Visible;
			TabTransitionLayer.Opacity = 1.0;
			AdjacentPreviewLayer.Opacity = 0.3;
			PreviousPreviewOffset.X = ((num2 > 0) ? 18 : 10);
			NextPreviewOffset.X = ((num2 > 0) ? 10 : (-8));
			double num3 = -1280.0;
			double toValue = ((num2 > 0) ? (-2560.0) : 0.0);
			TabTransitionSlide.X = num3;
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(385.0);
			DoubleAnimation doubleAnimation = new DoubleAnimation(num3, toValue, timeSpan)
			{
				EasingFunction = new QuinticEase
				{
					EasingMode = EasingMode.EaseInOut
				}
			};
			doubleAnimation.Completed += delegate
			{
				FinishTabAnimation();
			};
			TabTransitionSlide.BeginAnimation(TranslateTransform.XProperty, doubleAnimation);
			AdjacentPreviewLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.3, 0.36, timeSpan)
			{
				EasingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseInOut
				}
			});
			PreviousPreviewOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, timeSpan)
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
			NextPreviewOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, timeSpan)
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		}
		catch
		{
			_isAnimatingTab = false;
		}
	}

	private void PrepareTabTransitionStrip(object? oldTab, object? newTab, int direction)
	{
		if (direction > 0)
		{
			TransitionLeftHost.Content = null;
			TransitionCenterHost.Content = oldTab;
			TransitionRightHost.Content = newTab;
		}
		else
		{
			TransitionLeftHost.Content = newTab;
			TransitionCenterHost.Content = oldTab;
			TransitionRightHost.Content = null;
		}
	}

	private object? GetTabNear(object? tab, int step)
	{
		if (!(tab is DashboardTabViewModel item))
		{
			return null;
		}
		int num = _viewModel.Tabs.IndexOf(item) + step;
		if (num < 0 || num >= _viewModel.Tabs.Count)
		{
			return null;
		}
		return _viewModel.Tabs[num];
	}

	private void FinishTabAnimation()
	{
		_isAnimatingTab = false;
		TabTransitionLayer.Visibility = Visibility.Collapsed;
		TabTransitionLayer.Opacity = 0.0;
		TabTransitionSlide.X = -1280.0;
		PreviousPreviewOffset.X = 0.0;
		NextPreviewOffset.X = 0.0;
		TransitionLeftHost.Content = null;
		TransitionCenterHost.Content = null;
		TransitionRightHost.Content = null;
		ContentHost.Opacity = 1.0;
		AdjacentPreviewLayer.Opacity = 0.36;
		PreviousPreviewLiveHost.Content = null;
		NextPreviewLiveHost.Content = null;
		PreviousPreviewLiveLayer.Visibility = Visibility.Collapsed;
		NextPreviewLiveLayer.Visibility = Visibility.Collapsed;
		PreviousPreviewImage.Visibility = Visibility.Visible;
		NextPreviewImage.Visibility = Visibility.Visible;
		UpdateAdjacentPreviewSnapshots();
		if (_queuedTabStep == 0 || IsOverlayOpen())
		{
			_queuedTabStep = 0;
			return;
		}
		int step = _queuedTabStep;
		_queuedTabStep = 0;
		((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)delegate
		{
			_viewModel.MoveTab(step);
		}, (DispatcherPriority)5, Array.Empty<object>());
	}

	private void AnimateActiveOverlayIn()
	{
		if (GetActiveOverlay() is FrameworkElement { IsVisible: not false } frameworkElement)
		{
			AnimateOverlayIn(frameworkElement);
		}
	}

	private static void AnimateOverlayIn(FrameworkElement overlay)
	{
		overlay.BeginAnimation(UIElement.OpacityProperty, null);
		TranslateTransform translateTransform = overlay.RenderTransform as TranslateTransform;
		if (translateTransform == null)
		{
			translateTransform = (TranslateTransform)(overlay.RenderTransform = new TranslateTransform());
		}
		overlay.Opacity = 0.0;
		translateTransform.Y = 14.0;
		overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180.0))
		{
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
		translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14.0, 0.0, TimeSpan.FromMilliseconds(240.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
	}

	private void UpdateAdjacentPreviewSnapshots()
	{
		PreviousPreviewImage.Source = ((_viewModel.PreviousTab == null) ? null : CreatePreviewSnapshot(_viewModel.PreviousTab, 0.0 - _viewModel.LeftPreviewContentLeft));
		NextPreviewImage.Source = ((_viewModel.NextTab == null) ? null : CreatePreviewSnapshot(_viewModel.NextTab, 0.0 - _viewModel.RightPreviewContentLeft));
	}

	private void PrepareLiveAdjacentPreviews()
	{
		PreviousPreviewLiveHost.Content = _viewModel.PreviousTab;
		NextPreviewLiveHost.Content = _viewModel.NextTab;
		bool flag = _viewModel.PreviousTab != null;
		bool flag2 = _viewModel.NextTab != null;
		PreviousPreviewLiveLayer.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		NextPreviewLiveLayer.Visibility = ((!flag2) ? Visibility.Collapsed : Visibility.Visible);
		PreviousPreviewImage.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		NextPreviewImage.Visibility = (flag2 ? Visibility.Collapsed : Visibility.Visible);
	}

	private BitmapSource? CreatePreviewSnapshot(object? tab, double cropX)
	{
		if (tab == null)
		{
			return null;
		}
		try
		{
			ContentPresenter contentPresenter = new ContentPresenter();
			contentPresenter.Content = tab;
			contentPresenter.Width = 1280.0;
			contentPresenter.Height = 502.0;
			contentPresenter.Measure(new Size(1280.0, 502.0));
			contentPresenter.Arrange(new Rect(0.0, 0.0, 1280.0, 502.0));
			contentPresenter.UpdateLayout();
			VisualBrush brush = new VisualBrush(contentPresenter)
			{
				Stretch = Stretch.Fill,
				AlignmentX = AlignmentX.Left,
				AlignmentY = AlignmentY.Top,
				ViewboxUnits = BrushMappingMode.Absolute,
				Viewbox = new Rect(Math.Clamp(cropX, 0.0, 1176.0), 0.0, 104.0, 502.0),
				ViewportUnits = BrushMappingMode.Absolute,
				Viewport = new Rect(0.0, 0.0, 104.0, 502.0)
			};
			DrawingVisual drawingVisual = new DrawingVisual();
			using (DrawingContext drawingContext = drawingVisual.RenderOpen())
			{
				drawingContext.DrawRectangle(brush, null, new Rect(0.0, 0.0, 104.0, 502.0));
			}
			RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(104, 502, 96.0, 96.0, PixelFormats.Pbgra32);
			renderTargetBitmap.Render(drawingVisual);
			((Freezable)renderTargetBitmap).Freeze();
			return renderTargetBitmap;
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.CreatePreviewSnapshot");
			return null;
		}
	}

	private void QueueFocusFirstButton()
	{
		if (_isFocusUpdateQueued)
		{
			return;
		}
		_isFocusUpdateQueued = true;
		((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)delegate
		{
			try
			{
				_isFocusUpdateQueued = false;
				FocusFirstButton();
			}
			catch (Exception exception)
			{
				_isFocusUpdateQueued = false;
				App.LogException(exception, "MainWindow.QueueFocusFirstButton");
			}
		}, (DispatcherPriority)4, Array.Empty<object>());
	}

	private void FocusFirstButton()
	{
		if (_viewModel.IsMyGamesOpen)
		{
			System.Windows.Controls.Control control = (from candidate in GetLibraryGameButtons()
				select candidate.Control).FirstOrDefault();
			TryFocus(control ?? FindFocusableControl((DependencyObject?)(object)MyGamesOverlay));
			return;
		}
		if (_viewModel.IsLauncherSettingsOpen)
		{
			if (_viewModel.IsThemeCreatorOpen)
			{
				TryFocus(ChooseThemeHomeBackgroundButton ?? FindFocusableControl((DependencyObject?)(object)ThemeCreatorOverlay));
			}
			else
			{
				TryFocus(FindFocusableControl((DependencyObject?)(object)LauncherSettingsOverlay));
			}
			return;
		}
		if (_viewModel.IsThemeMenuOpen)
		{
			TryFocus(FindFocusableControl((DependencyObject?)(object)ThemeMenuOverlay));
			return;
		}
		if (_viewModel.IsProfileEditorOpen)
		{
			TryFocus(FindFocusableControl((DependencyObject?)(object)ProfileEditorOverlay));
			return;
		}
		if (_viewModel.IsMusicPlayerOpen)
		{
			TryFocus(FindFocusableControl((DependencyObject?)(object)MusicPlayerOverlay));
			return;
		}
		List<FocusCandidate> dashboardFocusCandidates = GetDashboardFocusCandidates();
		if (dashboardFocusCandidates.Count > 0)
		{
			FocusDefaultButton(dashboardFocusCandidates);
		}
		else
		{
			TryFocus(FindVisualChild<System.Windows.Controls.Button>((DependencyObject?)(object)ContentHost));
		}
	}

	private bool TryMoveDashboardFocus(DashboardInputAction action)
	{
		if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
		{
			return false;
		}
		if (_viewModel.IsLauncherSettingsOpen && DashboardInputRouter.TryAdjustFocusedSetting(action))
		{
			return true;
		}
		if (_viewModel.IsMusicPlayerOpen)
		{
			return TryMoveOverlayFocus(MusicPlayerOverlay, action);
		}
		if (_viewModel.IsMyGamesOpen)
		{
			return TryMoveMyGamesFocus(action);
		}
		if (_viewModel.IsLauncherSettingsOpen)
		{
			if (_viewModel.IsThemeCreatorOpen)
			{
				return TryMoveOverlayFocus(ThemeCreatorOverlay, action);
			}
			return TryMoveOverlayFocus(LauncherSettingsOverlay, action);
		}
		if (_viewModel.IsProfileEditorOpen)
		{
			return SafeMoveFocus(action);
		}
		if (_viewModel.IsThemeMenuOpen)
		{
			return TryMoveOverlayFocus(ThemeMenuOverlay, action);
		}
		List<FocusCandidate> dashboardFocusCandidates = GetDashboardFocusCandidates();
		if (dashboardFocusCandidates.Count == 0)
		{
			return SafeMoveFocus(action);
		}
		IInputElement focusedElement = Keyboard.FocusedElement;
		System.Windows.Controls.Button currentButton = focusedElement as System.Windows.Controls.Button;
		if (currentButton == null || !dashboardFocusCandidates.Any((FocusCandidate candidate) => candidate.Button == currentButton))
		{
			return FocusDefaultButton(dashboardFocusCandidates);
		}
		Point currentCenter = GetCenter(dashboardFocusCandidates.First((FocusCandidate candidate) => candidate.Button == currentButton).Bounds);
		Vector direction = (Vector)(action switch
		{
			DashboardInputAction.MoveLeft => new Vector(-1.0, 0.0), 
			DashboardInputAction.MoveRight => new Vector(1.0, 0.0), 
			DashboardInputAction.MoveUp => new Vector(0.0, -1.0), 
			DashboardInputAction.MoveDown => new Vector(0.0, 1.0), 
			_ => new Vector(0.0, 0.0), 
		});
		var anon = (from item in (from candidate in dashboardFocusCandidates
				where candidate.Button != currentButton
				select new
				{
					Candidate = candidate,
					Center = GetCenter(candidate.Bounds)
				}).Select(item =>
			{
				FocusCandidate candidate = item.Candidate;
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
			orderby item.Secondary * 2.4 + item.Primary, item.Primary
			select item).FirstOrDefault();
		if (anon == null)
		{
			RememberFocusedButton();
			return false;
		}
		if (!TryFocus(anon.Candidate.Button))
		{
			return false;
		}
		RememberFocusedButton();
		return true;
	}

	private List<FocusCandidate> GetDashboardFocusCandidates()
	{
		try
		{
			return (from button in FindVisualChildren<System.Windows.Controls.Button>((DependencyObject?)(object)ContentHost)
				where button.IsVisible && button.IsEnabled && button.Focusable
				select new FocusCandidate(button, GetElementBounds(button, ContentHost))).Where(delegate(FocusCandidate candidate)
			{
				Rect bounds = candidate.Bounds;
				if (bounds.Width > 0.0)
				{
					bounds = candidate.Bounds;
					return bounds.Height > 0.0;
				}
				return false;
			}).Where(delegate(FocusCandidate candidate)
			{
				Point center = GetCenter(candidate.Bounds);
				return center.X >= 64.0 && center.X <= 1120.0;
			}).ToList();
		}
		catch
		{
			return new List<FocusCandidate>();
		}
	}

	private void RememberFocusedButton()
	{
		if (_viewModel.CurrentTab != null && Keyboard.FocusedElement is System.Windows.Controls.Button value)
		{
			_lastFocusedButtonByTab[_viewModel.CurrentTab.Key] = value;
		}
	}

	private bool IsOverlayOpen()
	{
		if (!_viewModel.IsMyGamesOpen && !_viewModel.IsLauncherSettingsOpen && !_viewModel.IsProfileEditorOpen && !_viewModel.IsThemeMenuOpen && !_viewModel.IsThemeCreatorOpen && !_viewModel.IsMusicPlayerOpen && !_viewModel.IsSearchOverlayOpen && !_viewModel.IsDetailsOpen)
		{
			return _viewModel.IsQuickMenuOpen;
		}
		return true;
	}

	private bool TryRestoreOverlayFocus()
	{
		DependencyObject activeOverlay = GetActiveOverlay();
		if (activeOverlay == null || IsFocusInside(activeOverlay))
		{
			return false;
		}
		FocusFirstButton();
		return true;
	}

	private DependencyObject? GetActiveOverlay()
	{
		if (_viewModel.IsMyGamesOpen)
		{
			return (DependencyObject?)(object)MyGamesOverlay;
		}
		if (_viewModel.IsLauncherSettingsOpen)
		{
			if (_viewModel.IsThemeCreatorOpen)
			{
				return (DependencyObject?)(object)ThemeCreatorOverlay;
			}
			return (DependencyObject?)(object)LauncherSettingsOverlay;
		}
		if (_viewModel.IsProfileEditorOpen)
		{
			return (DependencyObject?)(object)ProfileEditorOverlay;
		}
		if (_viewModel.IsThemeMenuOpen)
		{
			return (DependencyObject?)(object)ThemeMenuOverlay;
		}
		if (_viewModel.IsMusicPlayerOpen)
		{
			return (DependencyObject?)(object)MusicPlayerOverlay;
		}
		return null;
	}

	private static bool IsFocusInside(DependencyObject overlay)
	{
		IInputElement focusedElement = Keyboard.FocusedElement;
		DependencyObject val = (DependencyObject)((focusedElement is DependencyObject) ? focusedElement : null);
		if (val == null)
		{
			return false;
		}
		for (DependencyObject val2 = val; val2 != null; val2 = VisualTreeHelper.GetParent(val2))
		{
			if (val2 == overlay)
			{
				return true;
			}
		}
		return false;
	}

	private static bool SafeMoveFocus(DashboardInputAction action)
	{
		try
		{
			return DashboardInputRouter.MoveFocus(action);
		}
		catch
		{
			return false;
		}
	}

	private bool FocusDefaultButton(IReadOnlyCollection<FocusCandidate> buttons)
	{
		if (_viewModel.CurrentTab != null && _lastFocusedButtonByTab.TryGetValue(_viewModel.CurrentTab.Key, out System.Windows.Controls.Button remembered) && buttons.Any((FocusCandidate candidate) => candidate.Button == remembered) && remembered.IsVisible && remembered.IsEnabled)
		{
			return TryFocus(remembered);
		}
		return TryFocus(buttons.OrderBy(delegate(FocusCandidate candidate)
		{
			Point center = GetCenter(candidate.Bounds);
			return Math.Abs(center.Y - 210.0);
		}).ThenBy(delegate(FocusCandidate candidate)
		{
			Point center = GetCenter(candidate.Bounds);
			return Math.Abs(center.X - 560.0);
		}).FirstOrDefault()
			.Button);
		}

		private bool TryMoveOverlayFocus(FrameworkElement overlay, DashboardInputAction action)
		{
			List<OverlayFocusCandidate> overlayFocusCandidates = GetOverlayFocusCandidates(overlay);
			if (overlayFocusCandidates.Count == 0)
			{
				return SafeMoveFocus(action);
			}
			System.Windows.Controls.Control currentControl = GetFocusedOverlayControl(overlay);
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
			if (anon == null)
			{
				return SafeMoveFocus(action);
			}
			return TryFocus(anon.Candidate.Control);
		}

		private static System.Windows.Controls.Control? GetFocusedOverlayControl(FrameworkElement overlay)
		{
			IInputElement focusedElement = Keyboard.FocusedElement;
			DependencyObject val = (DependencyObject)((focusedElement is DependencyObject) ? focusedElement : null);
			if (val == null)
			{
				return null;
			}
			for (DependencyObject val2 = val; val2 != null; val2 = GetParentObject(val2))
			{
				if ((object)val2 == overlay)
				{
					return null;
				}
				if (val2 is System.Windows.Controls.Control result)
				{
					return result;
				}
			}
			return null;
		}

		private bool TryMoveMyGamesFocus(DashboardInputAction action)
		{
			if ((uint)action > 1u)
			{
				return true;
			}
			List<GameCardViewModel> list = _viewModel.LibraryMenuGames.ToList();
			if (list.Count == 0)
			{
				return true;
			}
			GameCardViewModel gameCardViewModel = _viewModel.SelectedGame ?? list.FirstOrDefault();
			if (gameCardViewModel == null)
			{
				_viewModel.SelectGame(list[0]);
				((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)delegate
				{
					FocusLibraryGameButton(_viewModel.SelectedGame);
				}, (DispatcherPriority)3, Array.Empty<object>());
				return true;
			}
			int num = list.IndexOf(gameCardViewModel);
			if (num < 0)
			{
				num = 0;
			}
			int index = ((action == DashboardInputAction.MoveRight) ? Math.Min(num + 1, list.Count - 1) : Math.Max(num - 1, 0));
			GameCardViewModel nextGame = list[index];
			AnimateMyGamesStripShift(action);
			_viewModel.SelectGame(nextGame);
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				FocusLibraryGameButton(nextGame);
			}, (DispatcherPriority)3, Array.Empty<object>());
			return true;
		}

		private void AnimateMyGamesStripShift(DashboardInputAction action)
		{
			if (MyGamesStripTranslate != null)
			{
				double x = ((action == DashboardInputAction.MoveRight) ? 18.0 : (-18.0));
				MyGamesStripTranslate.BeginAnimation(TranslateTransform.XProperty, null);
				MyGamesStripTranslate.X = x;
				DoubleAnimation animation = new DoubleAnimation
				{
					To = 0.0,
					Duration = TimeSpan.FromMilliseconds(180.0),
					EasingFunction = new CubicEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				MyGamesStripTranslate.BeginAnimation(TranslateTransform.XProperty, animation);
			}
		}

		private void FocusLibraryGameButton(GameCardViewModel? game, int remainingRetries = 4)
		{
			if (game == null)
			{
				return;
			}
			try
			{
				MyGamesOverlay.UpdateLayout();
			}
			catch
			{
			}
			System.Windows.Controls.Button element = (from candidate in GetLibraryGameButtons()
				select (System.Windows.Controls.Button)candidate.Control).FirstOrDefault((System.Windows.Controls.Button candidate) => candidate.CommandParameter == game);
			if (TryFocus(element))
			{
				return;
			}
			if (remainingRetries > 0)
			{
				((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)delegate
				{
					FocusLibraryGameButton(game, remainingRetries - 1);
				}, (DispatcherPriority)7, Array.Empty<object>());
			}
			else if (!TryFocus(element))
			{
				FocusFirstButton();
			}
		}

		private List<OverlayFocusCandidate> GetLibraryGameButtons()
		{
			return (from button in FindVisualChildren<System.Windows.Controls.Button>((DependencyObject?)(object)MyGamesOverlay)
				where button.IsVisible && button.IsEnabled && button.Focusable && button.CommandParameter is GameCardViewModel
				select new OverlayFocusCandidate(button, GetElementBounds(button, MyGamesOverlay))).Where(delegate(OverlayFocusCandidate candidate)
			{
				Rect bounds = candidate.Bounds;
				if (bounds.Width > 0.0)
				{
					bounds = candidate.Bounds;
					return bounds.Height > 0.0;
				}
				return false;
			}).OrderBy(delegate(OverlayFocusCandidate candidate)
			{
				Rect bounds = candidate.Bounds;
				return bounds.Left;
			}).ToList();
		}

		private static List<OverlayFocusCandidate> GetOverlayFocusCandidates(FrameworkElement overlay)
		{
			try
			{
				return (from control in FindVisualChildren<System.Windows.Controls.Control>((DependencyObject?)(object)overlay)
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
					if (center.X >= -200.0 && center.X <= overlay.ActualWidth + 200.0)
					{
						Rect bounds = candidate.Bounds;
						if (bounds.Bottom >= -200.0)
						{
							bounds = candidate.Bounds;
							return bounds.Top <= overlay.ActualHeight + 1200.0;
						}
					}
					return false;
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

		private bool TryFocus(UIElement? element)
		{
			if (element == null || !element.IsVisible || !element.Focusable)
			{
				return false;
			}
			if (element is System.Windows.Controls.Control && !element.IsEnabled)
			{
				return false;
			}
			try
			{
				bool num = element.Focus();
				if (num && element is FrameworkElement frameworkElement)
				{
					frameworkElement.BringIntoView();
				}
				if (num && element is System.Windows.Controls.Button { CommandParameter: GameCardViewModel commandParameter })
				{
					_viewModel.SelectGame(commandParameter);
				}
				return num;
			}
			catch
			{
				return false;
			}
		}

		private static UIElement? FindFocusableControl(DependencyObject? root)
		{
			return FindVisualChildren<UIElement>(root).FirstOrDefault((UIElement element) => element is System.Windows.Controls.Control && element.IsVisible && element.IsEnabled && element.Focusable);
		}

		private static T? FindVisualChild<T>(DependencyObject? root) where T : DependencyObject
		{
			if (root == null)
			{
				return default(T);
			}
			int childrenCount;
			try
			{
				childrenCount = VisualTreeHelper.GetChildrenCount(root);
			}
			catch
			{
				return default(T);
			}
			for (int i = 0; i < childrenCount; i++)
			{
				DependencyObject child;
				try
				{
					child = VisualTreeHelper.GetChild(root, i);
				}
				catch
				{
					continue;
				}
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

		private static DependencyObject? GetParentObject(DependencyObject current)
		{
			if (current is Visual || current is Visual3D)
			{
				return VisualTreeHelper.GetParent(current);
			}
			if (current is FrameworkContentElement frameworkContentElement)
			{
				return frameworkContentElement.Parent;
			}
			return null;
		}

		private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? root) where T : DependencyObject
		{
			if (root == null)
			{
				yield break;
			}
			int childCount;
			try
			{
				childCount = VisualTreeHelper.GetChildrenCount(root);
			}
			catch
			{
				yield break;
			}
			for (int i = 0; i < childCount; i++)
			{
				DependencyObject child;
				try
				{
					child = VisualTreeHelper.GetChild(root, i);
				}
				catch
				{
					continue;
				}
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

		private void WritePerformanceDebugReport()
		{
			try
			{
				ImageCacheService.ImageCacheSnapshot snapshot = ImageCacheService.GetSnapshot();
				Process currentProcess = Process.GetCurrentProcess();
				bool num = _clockTimer.IsEnabled;
				DispatcherTimer? bootStateTimer = _bootStateTimer;
				int value = (num ? 1 : 0) + ((bootStateTimer != null && bootStateTimer.IsEnabled) ? 1 : 0) + (_performanceDebugTimer.IsEnabled ? 1 : 0) + (_viewModel.IsMusicProgressTimerActive ? 1 : 0) + (_controllerInputService.IsRunning ? 1 : 0) + (_guideViewModel?.ActiveTimerCount ?? 0);
				string[] contents = new string[13]
				{
					"[PERFORMANCE]",
					$"timestamp: {DateTime.Now:O}",
					$"ram working set: {(double)currentProcess.WorkingSet64 / 1024.0 / 1024.0:0.0} MB",
					$"ram private bytes: {(double)currentProcess.PrivateMemorySize64 / 1024.0 / 1024.0:0.0} MB",
					$"loaded image count: {snapshot.LoadedImageCount}",
					$"loaded cover count: {snapshot.LoadedCoverCount}",
					$"visible my games tiles: {_viewModel.VisibleLibraryMenuGames.Count}",
					$"largest cached image: {snapshot.LargestPixelWidth}x{snapshot.LargestPixelHeight}",
					$"active timers: {value}",
					"visualizer running: " + ((MusicVisualizer.ActiveRendererCount > 0) ? "yes" : "no"),
					$"visualizer instances active: {MusicVisualizer.ActiveRendererCount}",
					"audio analysis running: " + (_viewModel.IsAudioAnalysisRunning ? "yes" : "no"),
					"music playing: " + (_viewModel.IsMusicPlaying ? "yes" : "no")
				};
				Directory.CreateDirectory(Path.GetDirectoryName(PerformanceDebugLogPath));
				File.WriteAllLines(PerformanceDebugLogPath, contents);
			}
			catch (Exception exception)
			{
				App.LogException(exception, "MainWindow.WritePerformanceDebugReport");
			}
		}

		[DebuggerNonUserCode]
		[GeneratedCode("PresentationBuildTasks", "8.0.26.0")]
		public void InitializeComponent()
		{
			if (!_contentLoaded)
			{
				_contentLoaded = true;
				Uri resourceLocator = new Uri("/XboxMetroLauncher;V1.0.0.0;component/mainwindow.xaml", UriKind.Relative);
				System.Windows.Application.LoadComponent(this, resourceLocator);
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
				((MainWindow)target).PreviewKeyDown += Window_OnPreviewKeyDown;
				((MainWindow)target).PreviewMouseDown += Window_OnPreviewMouseDown;
				((MainWindow)target).Loaded += Window_OnLoaded;
				((MainWindow)target).Closing += Window_OnClosing;
				break;
			case 2:
				AudioHost = (Grid)target;
				break;
			case 3:
				RootGrid = (Grid)target;
				break;
			case 4:
				ThemeBackgroundLayer = (Grid)target;
				break;
			case 5:
				ThemeBackgroundImage = (Image)target;
				break;
			case 6:
				BingBackgroundLayer = (Grid)target;
				break;
			case 7:
				BingBackgroundImage = (Image)target;
				break;
			case 8:
				ContentFrame = (Grid)target;
				break;
			case 9:
				ContentHost = (ContentControl)target;
				break;
			case 10:
				ContentSlide = (TranslateTransform)target;
				break;
			case 11:
				TabTransitionLayer = (Canvas)target;
				break;
			case 12:
				TabTransitionStrip = (Canvas)target;
				break;
			case 13:
				TabTransitionSlide = (TranslateTransform)target;
				break;
			case 14:
				TransitionLeftHost = (ContentControl)target;
				break;
			case 15:
				TransitionCenterHost = (ContentControl)target;
				break;
			case 16:
				TransitionRightHost = (ContentControl)target;
				break;
			case 17:
				AdjacentPreviewLayer = (Canvas)target;
				break;
			case 18:
				PreviousPreviewOffset = (TranslateTransform)target;
				break;
			case 19:
				PreviousPreviewImage = (Image)target;
				break;
			case 20:
				PreviousPreviewLiveLayer = (Canvas)target;
				break;
			case 21:
				PreviousPreviewLiveHost = (ContentControl)target;
				break;
			case 22:
				NextPreviewOffset = (TranslateTransform)target;
				break;
			case 23:
				NextPreviewImage = (Image)target;
				break;
			case 24:
				NextPreviewLiveLayer = (Canvas)target;
				break;
			case 25:
				NextPreviewLiveHost = (ContentControl)target;
				break;
			case 26:
				DebugGuides = (Canvas)target;
				break;
			case 27:
				SearchOverlayTextBox = (System.Windows.Controls.TextBox)target;
				break;
			case 28:
				MyGamesOverlay = (Grid)target;
				break;
			case 29:
				MyGamesStrip = (Grid)target;
				break;
			case 30:
				MyGamesStripTranslate = (TranslateTransform)target;
				break;
			case 31:
				LauncherSettingsOverlay = (Grid)target;
				break;
			case 32:
				LauncherSettingsLeftScrollViewer = (ScrollViewer)target;
				break;
			case 33:
				LauncherSettingsRightScrollViewer = (ScrollViewer)target;
				break;
			case 34:
				ThemeMenuOverlay = (Grid)target;
				break;
			case 35:
				ThemeCreatorOverlay = (Grid)target;
				break;
			case 36:
				ChooseThemeHomeBackgroundButton = (System.Windows.Controls.Button)target;
				break;
			case 37:
				MusicPlayerOverlay = (Grid)target;
				break;
			case 38:
				MusicPlayerContentGrid = (Grid)target;
				break;
			case 39:
				((Grid)target).MouseLeftButtonUp += MusicFullscreenHint_OnMouseLeftButtonUp;
				break;
			case 40:
				ProfileEditorOverlay = (Grid)target;
				break;
			case 41:
				BootOverlay = (Grid)target;
				break;
			case 42:
				BootVideoHost = (WindowsFormsHost)target;
				break;
			default:
				_contentLoaded = true;
				break;
			}
		}
	}
