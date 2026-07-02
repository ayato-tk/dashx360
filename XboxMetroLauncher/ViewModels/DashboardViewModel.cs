using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using XboxMetroLauncher.Input;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Services;
using XboxMetroLauncher.Utilities;
using XboxMetroLauncher.ViewModels.Tabs;

namespace XboxMetroLauncher.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private static readonly string SteamScanDebugLogPath = Path.Combine(AppPaths.LogsFolder, "steam-scan-debug.log");
    private const int ShowWindowRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public event EventHandler? FriendsOverlayRequested;

    private readonly IGameLibraryService _libraryService;
    private readonly IGameLaunchService _launchService;
    private readonly ISearchService _searchService;
    private readonly ISettingsService _settingsService;
    private readonly IProfileService _profileService;
    private readonly IFilePickerService _filePickerService;
    private readonly IImportExportService _importExportService;
    private readonly ISteamLibraryScannerService _steamLibraryScannerService;
    private readonly ISteamCommunityService _steamCommunityService;
    private readonly IThemeService _themeService;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private readonly IAudioService _audioService;
    private readonly SocialIntegrationManager _socialIntegrationManager;
    private readonly IRunningGameService _runningGameService;
    private readonly AudioAnalysisService _audioAnalysisService = new();
    private readonly MediaPlayer _musicPlayer = new();
    private readonly DispatcherTimer _musicTimer;
    private readonly List<Brush> _accentBrushes;
    private GameLibrary _library = new();
    private DashboardTabViewModel? _currentTab;
    private GameCardViewModel? _selectedGame;
    private GameCardViewModel? _featuredGame;
    private Profile _profile = new();
    private AppSettings _settings = new();
    private string _searchQuery = string.Empty;
    private string _statusMessage = "Ready";
    private bool _isSearchOverlayOpen;
    private bool _isDetailsOpen;
    private bool _isQuickMenuOpen;
    private bool _isMyGamesOpen;
    private bool _isLibraryShowingPins;
    private bool _isLibraryShowingApps;
    private bool _isLauncherSettingsOpen;
    private bool _isProfileEditorOpen;
    private bool _isThemeMenuOpen;
    private bool _isThemeCreatorOpen;
    private bool _isSteamSetupOpen;
    private bool _isMusicPlayerOpen;
    private bool _isMusicPlayerTransparent;
    private bool _isMusicVisualizerFullscreen;
    private bool _isMusicPlaying;
    private bool _isShuffleEnabled;
    private bool _isBooting = true;
    private string _clockText = string.Empty;
    private string _musicPositionText = "0:00";
    private string _musicDurationText = "0:00";
    private double _musicProgress;
    private double _musicVolume = 0.7;
    private double _visualizerBass;
    private double _visualizerMid;
    private double _visualizerTreble;
    private double _visualizerLoudness;
    private double _visualizerPeak;
    private string? _pendingTabSound;
    private GameCardViewModel? _trayGame;
    private MusicTrackViewModel? _currentMusicTrack;
    private int _musicIndex = -1;
    private readonly Random _random = new();
    private DashboardTheme? _selectedTheme;
    private string _themeNameInput = string.Empty;
    private string _themeHomePreviewPath = string.Empty;
    private string _themeGamesPreviewPath = string.Empty;
    private string _themeSettingsPreviewPath = string.Empty;
    private string _themeAppsPreviewPath = string.Empty;
    private string _steamSetupApiKey = string.Empty;
    private string _steamSetupSteamId64 = string.Empty;
    private string _steamSetupStatus = "Steam is not connected.";
    private int _libraryMenuStartIndex;
    private const int LibraryVisibleWindowSize = 6;
    private const double LibraryMenuLeftPeekOffset = 176;

    public DashboardViewModel(
        IGameLibraryService libraryService,
        IGameLaunchService launchService,
        ISearchService searchService,
        ISettingsService settingsService,
        IProfileService profileService,
        IFilePickerService filePickerService,
        IImportExportService importExportService,
        ISteamLibraryScannerService steamLibraryScannerService,
        ISteamCommunityService steamCommunityService,
        IThemeService themeService,
        IStartupRegistrationService startupRegistrationService,
        IAudioService audioService,
        SocialIntegrationManager socialIntegrationManager,
        IRunningGameService runningGameService)
    {
        _libraryService = libraryService;
        _launchService = launchService;
        _searchService = searchService;
        _settingsService = settingsService;
        _profileService = profileService;
        _filePickerService = filePickerService;
        _importExportService = importExportService;
        _steamLibraryScannerService = steamLibraryScannerService;
        _steamCommunityService = steamCommunityService;
        _themeService = themeService;
        _startupRegistrationService = startupRegistrationService;
        _audioService = audioService;
        _socialIntegrationManager = socialIntegrationManager;
        _runningGameService = runningGameService;
        _musicPlayer.Volume = _musicVolume;
        _musicPlayer.MediaOpened += (_, _) => RefreshMusicProgress();
        _musicPlayer.MediaEnded += (_, _) => NextMusicTrack();
        _audioAnalysisService.FrameReady += AudioAnalysis_OnFrameReady;
        _musicTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _musicTimer.Tick += (_, _) => RefreshMusicProgress();

        _accentBrushes =
        [
            new SolidColorBrush(Color.FromRgb(20, 156, 74)),
            new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            new SolidColorBrush(Color.FromRgb(202, 80, 16)),
            new SolidColorBrush(Color.FromRgb(116, 77, 169)),
            new SolidColorBrush(Color.FromRgb(36, 161, 156)),
            new SolidColorBrush(Color.FromRgb(190, 40, 71))
        ];

        Tabs =
        [
            new BingTabViewModel(this),
            new HomeTabViewModel(this),
            new SocialTabViewModel(this),
            new MediaTabViewModel(this),
            new GamesTabViewModel(this),
            new MusicTabViewModel(this),
            new AppsTabViewModel(this),
            new SettingsTabViewModel(this)
        ];

        Games.CollectionChanged += OnGamesChanged;
        _runningGameService.StateChanged += RunningGameService_OnStateChanged;

        SelectGameCommand = new RelayCommand(parameter => SelectGame(parameter as GameCardViewModel));
        LaunchGameCommand = new AsyncRelayCommand(parameter => LaunchGameAsync(parameter as GameCardViewModel));
        SubmitSearchCommand = new AsyncRelayCommand(SubmitSearchAsync);
        UseTrendingSearchCommand = new RelayCommand(parameter =>
        {
            SearchQuery = parameter?.ToString() ?? string.Empty;
            _ = SubmitSearchAsync();
        });
        OpenSearchCommand = new RelayCommand(OpenSearch);
        CloseSearchCommand = new RelayCommand(() => IsSearchOverlayOpen = false);
        ShowDetailsCommand = new RelayCommand(() => IsDetailsOpen = SelectedGame is not null);
        CloseDetailsCommand = new RelayCommand(() => IsDetailsOpen = false);
        BackCommand = new RelayCommand(GoBack);
        AddGameCommand = new AsyncRelayCommand(AddGameAsync);
        EditSelectedGameCommand = new AsyncRelayCommand(EditSelectedGameAsync);
        ScanFolderCommand = new AsyncRelayCommand(ScanFolderAsync);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, _ => SelectedGame is not null);
        OpenSelectedGameStoreCommand = new RelayCommand(OpenSelectedGameStore);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        ExportDataCommand = new AsyncRelayCommand(ExportDataAsync);
        ImportDataCommand = new AsyncRelayCommand(ImportDataAsync);
        ScanSteamGamesCommand = new AsyncRelayCommand(ScanSteamGamesAsync);
        OpenSteamSetupCommand = new AsyncRelayCommand(OpenSteamSetupAsync);
        CloseSteamSetupCommand = new RelayCommand(() => IsSteamSetupOpen = false);
        SaveSteamSetupCommand = new AsyncRelayCommand(SaveSteamSetupAsync);
        TestSteamSetupCommand = new AsyncRelayCommand(TestSteamSetupAsync);
        PasteSteamApiKeyCommand = new RelayCommand(() => SteamSetupApiKey = GetClipboardText());
        PasteSteamIdCommand = new RelayCommand(() => SteamSetupSteamId64 = ExtractSteamId64(GetClipboardText()));
        OpenSteamApiKeyPageCommand = new RelayCommand(() => OpenExternalUrl("https://steamcommunity.com/dev/apikey", "Opening Steam API key page"));
        OpenSteamProfileHelpCommand = new RelayCommand(() => OpenExternalUrl("https://steamid.io/lookup", "Opening SteamID lookup"));
        OpenThemeMenuCommand = new RelayCommand(OpenThemeMenu);
        CloseThemeMenuCommand = new RelayCommand(() => IsThemeMenuOpen = false);
        SelectThemeCommand = new AsyncRelayCommand(SelectThemeAsync);
        OpenThemeCreatorCommand = new RelayCommand(OpenThemeCreator);
        CloseThemeCreatorCommand = new RelayCommand(CloseThemeCreator);
        ChooseThemeHomeImageCommand = new AsyncRelayCommand(_ => ChooseThemeSectionImageAsync("home"));
        ChooseThemeGamesImageCommand = new AsyncRelayCommand(_ => ChooseThemeSectionImageAsync("games"));
        ChooseThemeSettingsImageCommand = new AsyncRelayCommand(_ => ChooseThemeSectionImageAsync("settings"));
        ChooseThemeAppsImageCommand = new AsyncRelayCommand(_ => ChooseThemeSectionImageAsync("apps"));
        SaveThemeCommand = new AsyncRelayCommand(SaveThemeAsync);
        ToggleQuickMenuCommand = new RelayCommand(() => IsQuickMenuOpen = !IsQuickMenuOpen);
        OpenMyGamesCommand = new RelayCommand(OpenMyGames);
        OpenMyAppsCommand = new RelayCommand(OpenMyApps);
        OpenMyPinsCommand = new RelayCommand(OpenMyPins);
        CloseMyGamesCommand = new RelayCommand(() => IsMyGamesOpen = false);
        OpenLauncherSettingsCommand = new RelayCommand(OpenLauncherSettings);
        CloseLauncherSettingsCommand = new RelayCommand(() => IsLauncherSettingsOpen = false);
        ChooseSelectedHomeImageCommand = new AsyncRelayCommand(ChooseSelectedHomeImageAsync);
        ChooseSelectedGameMenuImageCommand = new AsyncRelayCommand(ChooseSelectedGameMenuImageAsync);
        SaveSelectedGameCommand = new AsyncRelayCommand(SaveSelectedGameAsync);
        SetOpenTrayGameCommand = new AsyncRelayCommand(SetOpenTrayGameAsync);
        RemoveSelectedGameCommand = new AsyncRelayCommand(RemoveSelectedGameAsync);
        OpenProfileEditorCommand = new RelayCommand(OpenProfileEditor);
        CloseProfileEditorCommand = new RelayCommand(() => IsProfileEditorOpen = false);
        OpenMusicPlayerCommand = new RelayCommand(parameter => OpenMusicPlayer(parameter is bool transparent && transparent));
        CloseMusicPlayerCommand = new RelayCommand(CloseMusicPlayer);
        OpenMusicFolderCommand = new RelayCommand(OpenMusicFolder);
        OpenMusicVisualizerFullscreenCommand = new RelayCommand(OpenMusicVisualizerFullscreen);
        PlayPauseMusicCommand = new RelayCommand(ToggleMusicPlayback);
        StopMusicCommand = new RelayCommand(StopMusic);
        NextMusicCommand = new RelayCommand(NextMusicTrack);
        PreviousMusicCommand = new RelayCommand(PreviousMusicTrack);
        ToggleShuffleMusicCommand = new RelayCommand(() => IsShuffleEnabled = !IsShuffleEnabled);
        VolumeDownCommand = new RelayCommand(() => MusicVolume -= 0.05);
        VolumeUpCommand = new RelayCommand(() => MusicVolume += 0.05);
        PlaySelectedMusicCommand = new RelayCommand(parameter => PlayMusicTrack(parameter as MusicTrackViewModel));
        ChooseProfilePictureCommand = new AsyncRelayCommand(ChooseProfilePictureAsync);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync);
        ShutdownCommand = new AsyncRelayCommand(ShutdownAsync);
        OpenYouTubeCommand = new RelayCommand(OpenYouTube);
        OpenFriendsOverlayCommand = new RelayCommand(RequestFriendsOverlay);
        SwitchTabCommand = new RelayCommand(parameter =>
        {
            if (parameter is DashboardTabViewModel tab)
            {
                CurrentTab = tab;
            }
        });

        CurrentTab = Tabs[1];
        UpdateClock();
    }

    public ObservableCollection<DashboardTabViewModel> Tabs { get; }
    public ObservableCollection<GameCardViewModel> Games { get; } = [];
    public ObservableCollection<MusicTrackViewModel> MusicTracks { get; } = [];
    public ObservableCollection<DashboardTheme> AvailableThemes { get; } = [];
    public ObservableCollection<GameCardViewModel> VisibleLibraryMenuGames { get; } = [];

    public IEnumerable<GameCardViewModel> RecentGames => Games
        .OrderByDescending(game => game.Game.LastPlayed ?? DateTimeOffset.MinValue)
        .Take(8);

    public IEnumerable<GameCardViewModel> PinnedGames => Games
        .Where(game => game.Game.IsFavorite)
        .Take(8);

    public IEnumerable<GameCardViewModel> ImportedGames => Games
        .Where(game => string.Equals(game.Game.Genre, "Imported", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> LibraryPaths => _library.LibraryPaths;
    public double LibraryMenuScrollOffset => _libraryMenuStartIndex > 0 ? LibraryMenuLeftPeekOffset : 0;
    public IReadOnlyList<string> ResolutionOptions { get; } = ["720p", "1080p", "1440p", "4K"];
    public IReadOnlyList<string> GameCoverFitOptions { get; } = ["Auto", "Cover", "Fill", "Fit"];
    public IReadOnlyList<string> AddDestinationOptions { get; } = ["My Games", "My Apps"];
    public IReadOnlyList<string> SocialIntegrationOptions { get; } = ["Local"];

    public DashboardTabViewModel? CurrentTab
    {
        get => _currentTab;
        set
        {
            if (!SetProperty(ref _currentTab, value) || value is null)
            {
                return;
            }

            foreach (var tab in Tabs)
            {
                tab.IsSelected = ReferenceEquals(tab, value);
            }

            _audioService.Play(_pendingTabSound ?? "tab");
            _pendingTabSound = null;
            OnPropertyChanged(nameof(CurrentTabName));
            OnPropertyChanged(nameof(PreviousTab));
            OnPropertyChanged(nameof(NextTab));
            OnPropertyChanged(nameof(LeftPreviewContentLeft));
            OnPropertyChanged(nameof(RightPreviewContentLeft));
            OnPropertyChanged(nameof(CurrentReferenceImagePath));
            OnPropertyChanged(nameof(CurrentReferenceImageOpacity));
            OnPropertyChanged(nameof(UseLightDashboardChrome));
            OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
        }
    }

    public string CurrentTabName => CurrentTab?.Name ?? string.Empty;

    public double LeftPreviewContentLeft => CurrentTab?.Key == "settings" ? -938 : -910;

    public double RightPreviewContentLeft => CurrentTab?.Key is "bing" or "home" ? -198 : -240;

    public DashboardTabViewModel? PreviousTab
    {
        get
        {
            if (CurrentTab is null)
            {
                return null;
            }

            var index = Tabs.IndexOf(CurrentTab);
            return index > 0 ? Tabs[index - 1] : null;
        }
    }

    public DashboardTabViewModel? NextTab
    {
        get
        {
            if (CurrentTab is null)
            {
                return null;
            }

            var index = Tabs.IndexOf(CurrentTab);
            return index >= 0 && index < Tabs.Count - 1 ? Tabs[index + 1] : null;
        }
    }

    public string CurrentReferenceImagePath => string.Empty;

    public double CurrentReferenceImageOpacity => 0;

    public bool UseLightDashboardChrome => false;

    public GameCardViewModel? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                if (value is not null)
                {
                    FeaturedGame = value;
                    StatusMessage = value.Title;
                }

                RefreshVisibleLibraryMenuGames();
                OnPropertyChanged(nameof(SpotlightTitle));
                OnPropertyChanged(nameof(SpotlightSubtitle));
                OnPropertyChanged(nameof(MyGamesCountText));
                OnPropertyChanged(nameof(LibraryMenuCountText));
                OnPropertyChanged(nameof(SelectedCoverZoom));
                OnPropertyChanged(nameof(SelectedCoverOffsetX));
                OnPropertyChanged(nameof(SelectedCoverOffsetY));
            }
        }
    }

    public GameCardViewModel? FeaturedGame
    {
        get => _featuredGame;
        set
        {
            if (SetProperty(ref _featuredGame, value))
            {
                OnPropertyChanged(nameof(SpotlightTitle));
                OnPropertyChanged(nameof(SpotlightSubtitle));
            }
        }
    }

    public Profile Profile
    {
        get => _profile;
        set => SetProperty(ref _profile, value);
    }

    public AppSettings Settings
    {
        get => _settings;
        set
        {
            value.GameCoverFitMode = NormalizeGameCoverFitMode(value.GameCoverFitMode);
            value.DefaultAddDestination = NormalizeAddDestination(value.DefaultAddDestination);
            value.SocialIntegrationMode = NormalizeSocialIntegrationMode(value.SocialIntegrationMode);
            if (SetProperty(ref _settings, value))
            {
                OnPropertyChanged(nameof(OpenTrayTitle));
                OnPropertyChanged(nameof(GameCoverFitMode));
                OnPropertyChanged(nameof(DefaultAddDestination));
                OnPropertyChanged(nameof(SocialIntegrationModeDisplay));
                OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
            }
        }
    }

    public string GameCoverFitMode
    {
        get => Settings.GameCoverFitMode;
        set
        {
            value = NormalizeGameCoverFitMode(value);
            if (string.Equals(Settings.GameCoverFitMode, value, StringComparison.Ordinal))
            {
                return;
            }

            Settings.GameCoverFitMode = value;
            OnPropertyChanged();
        }
    }

    public string DefaultAddDestination
    {
        get => Settings.DefaultAddDestination;
        set
        {
            value = NormalizeAddDestination(value);
            if (string.Equals(Settings.DefaultAddDestination, value, StringComparison.Ordinal))
            {
                return;
            }

            Settings.DefaultAddDestination = value;
            OnPropertyChanged();
        }
    }

    public string SocialIntegrationModeDisplay
    {
        get => ToSocialIntegrationDisplay(Settings.SocialIntegrationMode);
        set
        {
            var normalized = ParseSocialIntegrationMode(value);
            if (Settings.SocialIntegrationMode == normalized)
            {
                return;
            }

            Settings.SocialIntegrationMode = normalized;
            OnPropertyChanged();
        }
    }

    public double SelectedCoverZoom
    {
        get => SelectedGame?.Game.CoverZoom > 0 ? SelectedGame.Game.CoverZoom : 1;
        set
        {
            if (SelectedGame is null)
            {
                return;
            }

            var zoom = Math.Clamp(value, 1, 1.8);
            if (Math.Abs(SelectedGame.Game.CoverZoom - zoom) < 0.001)
            {
                return;
            }

            SelectedGame.Game.CoverZoom = zoom;
            SelectedGame.Refresh();
            OnPropertyChanged();
        }
    }

    public double SelectedCoverOffsetX
    {
        get => SelectedGame?.Game.CoverOffsetX ?? 0;
        set
        {
            if (SelectedGame is null)
            {
                return;
            }

            var offset = Math.Clamp(value, -1, 1);
            if (Math.Abs(SelectedGame.Game.CoverOffsetX - offset) < 0.001)
            {
                return;
            }

            SelectedGame.Game.CoverOffsetX = offset;
            SelectedGame.Refresh();
            OnPropertyChanged();
        }
    }

    public double SelectedCoverOffsetY
    {
        get => SelectedGame?.Game.CoverOffsetY ?? 0;
        set
        {
            if (SelectedGame is null)
            {
                return;
            }

            var offset = Math.Clamp(value, -1, 1);
            if (Math.Abs(SelectedGame.Game.CoverOffsetY - offset) < 0.001)
            {
                return;
            }

            SelectedGame.Game.CoverOffsetY = offset;
            SelectedGame.Refresh();
            OnPropertyChanged();
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsSearchOverlayOpen
    {
        get => _isSearchOverlayOpen;
        set => SetProperty(ref _isSearchOverlayOpen, value);
    }

    public bool IsDetailsOpen
    {
        get => _isDetailsOpen;
        set
        {
            if (SetProperty(ref _isDetailsOpen, value) && value)
            {
                _ = RefreshSelectedGameDetailsAsync();
            }
        }
    }

    public bool IsQuickMenuOpen
    {
        get => _isQuickMenuOpen;
        set => SetProperty(ref _isQuickMenuOpen, value);
    }

    public bool IsMyGamesOpen
    {
        get => _isMyGamesOpen;
        set
        {
            if (SetProperty(ref _isMyGamesOpen, value))
            {
                OnPropertyChanged(nameof(IsDashboardContentHidden));
                OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
            }
        }
    }

    public bool IsLauncherSettingsOpen
    {
        get => _isLauncherSettingsOpen;
        set
        {
            if (SetProperty(ref _isLauncherSettingsOpen, value))
            {
                OnPropertyChanged(nameof(IsDashboardContentHidden));
                OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
            }
        }
    }

    public bool IsProfileEditorOpen
    {
        get => _isProfileEditorOpen;
        set
        {
            if (SetProperty(ref _isProfileEditorOpen, value))
            {
                OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
            }
        }
    }

    public bool IsThemeMenuOpen
    {
        get => _isThemeMenuOpen;
        set
        {
            if (SetProperty(ref _isThemeMenuOpen, value))
            {
                OnPropertyChanged(nameof(ThemeMenuVisibilityTitle));
            }
        }
    }

    public bool IsThemeCreatorOpen
    {
        get => _isThemeCreatorOpen;
        set
        {
            if (SetProperty(ref _isThemeCreatorOpen, value))
            {
                OnPropertyChanged(nameof(IsDashboardContentHidden));
                OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
            }
        }
    }

    public bool IsDashboardContentHidden => IsMyGamesOpen || IsLauncherSettingsOpen || IsThemeCreatorOpen;

    public bool IsSteamSetupOpen
    {
        get => _isSteamSetupOpen;
        set => SetProperty(ref _isSteamSetupOpen, value);
    }

    public bool IsMusicPlayerOpen
    {
        get => _isMusicPlayerOpen;
        set
        {
            if (SetProperty(ref _isMusicPlayerOpen, value))
            {
                EnsureAudioAnalysisState();
                OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
            }
        }
    }

    public bool IsMusicPlayerTransparent
    {
        get => _isMusicPlayerTransparent;
        private set => SetProperty(ref _isMusicPlayerTransparent, value);
    }

    public bool IsMusicVisualizerFullscreen
    {
        get => _isMusicVisualizerFullscreen;
        private set => SetProperty(ref _isMusicVisualizerFullscreen, value);
    }

    public bool IsMusicPlaying
    {
        get => _isMusicPlaying;
        set
        {
            if (SetProperty(ref _isMusicPlaying, value))
            {
                EnsureAudioAnalysisState();
                OnPropertyChanged(nameof(MusicPlayPauseText));
            }
        }
    }

    public bool IsShuffleEnabled
    {
        get => _isShuffleEnabled;
        set
        {
            if (SetProperty(ref _isShuffleEnabled, value))
            {
                OnPropertyChanged(nameof(ShuffleText));
            }
        }
    }

    public bool IsBooting
    {
        get => _isBooting;
        set => SetProperty(ref _isBooting, value);
    }

    public DashboardTheme? SelectedTheme
    {
        get => _selectedTheme;
        set => SetProperty(ref _selectedTheme, value);
    }

    public string ThemeNameInput
    {
        get => _themeNameInput;
        set => SetProperty(ref _themeNameInput, value);
    }

    public string ThemeHomePreviewPath
    {
        get => _themeHomePreviewPath;
        set => SetProperty(ref _themeHomePreviewPath, value);
    }

    public string ThemeGamesPreviewPath
    {
        get => _themeGamesPreviewPath;
        set => SetProperty(ref _themeGamesPreviewPath, value);
    }

    public string ThemeSettingsPreviewPath
    {
        get => _themeSettingsPreviewPath;
        set => SetProperty(ref _themeSettingsPreviewPath, value);
    }

    public string ThemeAppsPreviewPath
    {
        get => _themeAppsPreviewPath;
        set => SetProperty(ref _themeAppsPreviewPath, value);
    }

    public string SteamSetupApiKey
    {
        get => _steamSetupApiKey;
        set => SetProperty(ref _steamSetupApiKey, value?.Trim() ?? string.Empty);
    }

    public string SteamSetupSteamId64
    {
        get => _steamSetupSteamId64;
        set => SetProperty(ref _steamSetupSteamId64, ExtractSteamId64(value ?? string.Empty));
    }

    public string SteamSetupStatus
    {
        get => _steamSetupStatus;
        set => SetProperty(ref _steamSetupStatus, value);
    }

    public string ThemeMenuVisibilityTitle => SelectedTheme?.Name ?? DashboardTheme.BuiltInThemeName;

    public string CurrentThemeBackgroundPath
    {
        get
        {
            var sectionKey = ResolveThemeSectionKey();
            if (SelectedTheme is null || SelectedTheme.IsBuiltIn || string.IsNullOrWhiteSpace(sectionKey))
            {
                return string.Empty;
            }

            var path = SelectedTheme.GetBackgroundPath(sectionKey);
            return File.Exists(AppPaths.ResolvePath(path)) ? path : string.Empty;
        }
    }

    public string ClockText
    {
        get => _clockText;
        set => SetProperty(ref _clockText, value);
    }

    public MusicTrackViewModel? CurrentMusicTrack
    {
        get => _currentMusicTrack;
        set
        {
            if (SetProperty(ref _currentMusicTrack, value))
            {
                foreach (var track in MusicTracks)
                {
                    track.IsPlaying = ReferenceEquals(track, value);
                }

                OnPropertyChanged(nameof(CurrentMusicTitle));
                OnPropertyChanged(nameof(MusicTrackCountText));
            }
        }
    }

    public string CurrentMusicTitle => CurrentMusicTrack?.Title ?? "No music found";
    public string MusicTrackCountText => MusicTracks.Count == 0 ? "0 of 0" : $"{Math.Max(1, _musicIndex + 1)} of {MusicTracks.Count}";
    public string MusicPlayPauseText => IsMusicPlaying ? "Pause" : "Play";
    public string ShuffleText => IsShuffleEnabled ? "Shuffle On" : "Shuffle";

    public string MusicPositionText
    {
        get => _musicPositionText;
        set => SetProperty(ref _musicPositionText, value);
    }

    public string MusicDurationText
    {
        get => _musicDurationText;
        set => SetProperty(ref _musicDurationText, value);
    }

    public double MusicProgress
    {
        get => _musicProgress;
        set => SetProperty(ref _musicProgress, value);
    }

    public double MusicVolume
    {
        get => _musicVolume;
        set
        {
            var volume = Math.Clamp(value, 0, 1);
            if (SetProperty(ref _musicVolume, volume))
            {
                _musicPlayer.Volume = volume;
                OnPropertyChanged(nameof(MusicVolumeText));
            }
        }
    }

    public string MusicVolumeText => $"{Math.Round(MusicVolume * 100)}%";

    public double VisualizerBass
    {
        get => _visualizerBass;
        private set => SetProperty(ref _visualizerBass, value);
    }

    public double VisualizerMid
    {
        get => _visualizerMid;
        private set => SetProperty(ref _visualizerMid, value);
    }

    public double VisualizerTreble
    {
        get => _visualizerTreble;
        private set => SetProperty(ref _visualizerTreble, value);
    }

    public double VisualizerLoudness
    {
        get => _visualizerLoudness;
        private set => SetProperty(ref _visualizerLoudness, value);
    }

    public double VisualizerPeak
    {
        get => _visualizerPeak;
        private set => SetProperty(ref _visualizerPeak, value);
    }

    public string SpotlightTitle => FeaturedGame?.Title ?? "Xbox Metro Launcher";
    public string SpotlightSubtitle => FeaturedGame?.Subtitle ?? "Press Y to search or E to move across the dashboard.";
    public GameCardViewModel? TrayGame
    {
        get => _trayGame;
        set
        {
            if (SetProperty(ref _trayGame, value))
            {
                OnPropertyChanged(nameof(OpenTrayTitle));
                OnPropertyChanged(nameof(OpenTrayCoverArtPath));
            }
        }
    }

    public string OpenTrayTitle => TrayGame?.Title ?? "Open Tray";
    public string OpenTrayCoverArtPath => TrayGame?.BackgroundArtPath ?? string.Empty;
    public string MyGamesCountText
    {
        get
        {
            var games = Games.Where(game => !IsAppEntry(game.Game)).ToList();
            var count = games.Count;
            if (count == 0)
            {
                return "0 of 17";
            }

            var selected = SelectedGame is null ? 1 : Math.Max(1, games.IndexOf(SelectedGame) + 1);
            return $"{selected} of {count}";
        }
    }

    public string LibraryMenuTitle => _isLibraryShowingPins ? "My Pins" : _isLibraryShowingApps ? "My Apps" : "My Games";

    public string LibraryMenuFilterText => _isLibraryShowingPins ? "pinned games" : _isLibraryShowingApps ? "all apps" : "all games";

    public string LibraryMenuXHintText => " Game Details";
    public string LibraryMenuYHintText => " Pin";

    public IEnumerable<GameCardViewModel> LibraryMenuGames
        => GetLibraryMenuGames();

    public string LibraryMenuCountText
    {
        get
        {
            var visibleGames = LibraryMenuGames.ToList();
            if (visibleGames.Count == 0)
            {
                return _isLibraryShowingPins || _isLibraryShowingApps ? "0 of 0" : "0 of 17";
            }

            var selected = SelectedGame is null ? 1 : visibleGames.IndexOf(SelectedGame) + 1;
            if (selected <= 0)
            {
                selected = 1;
            }

            return $"{selected} of {visibleGames.Count}";
        }
    }

    public bool HasRunningLaunchedGame => _runningGameService.HasRunningGame;

    public string RunningLaunchedGameTitle => _runningGameService.RunningGameTitle;

    public GameMetadata? RunningLaunchedGame => _runningGameService.CurrentGame;

    public string RunningGameFooterActionText => _runningGameService.State switch
    {
        RunningGameState.Launching => "Finding Game...",
        RunningGameState.None => "No Game Running",
        _ => "Close Game"
    };

    public bool IsAudioAnalysisRunning => _audioAnalysisService.IsRunning;

    public bool IsMusicProgressTimerActive => _musicTimer.IsEnabled;

    public ICommand SelectGameCommand { get; }
    public ICommand LaunchGameCommand { get; }
    public ICommand SubmitSearchCommand { get; }
    public ICommand UseTrendingSearchCommand { get; }
    public ICommand OpenSearchCommand { get; }
    public ICommand CloseSearchCommand { get; }
    public ICommand ShowDetailsCommand { get; }
    public ICommand CloseDetailsCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand AddGameCommand { get; }
    public ICommand EditSelectedGameCommand { get; }
    public ICommand ScanFolderCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand OpenSelectedGameStoreCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ExportDataCommand { get; }
    public ICommand ImportDataCommand { get; }
    public ICommand ScanSteamGamesCommand { get; }
    public ICommand OpenSteamSetupCommand { get; }
    public ICommand CloseSteamSetupCommand { get; }
    public ICommand SaveSteamSetupCommand { get; }
    public ICommand TestSteamSetupCommand { get; }
    public ICommand PasteSteamApiKeyCommand { get; }
    public ICommand PasteSteamIdCommand { get; }
    public ICommand OpenSteamApiKeyPageCommand { get; }
    public ICommand OpenSteamProfileHelpCommand { get; }
    public ICommand OpenThemeMenuCommand { get; }
    public ICommand CloseThemeMenuCommand { get; }
    public ICommand SelectThemeCommand { get; }
    public ICommand OpenThemeCreatorCommand { get; }
    public ICommand CloseThemeCreatorCommand { get; }
    public ICommand ChooseThemeHomeImageCommand { get; }
    public ICommand ChooseThemeGamesImageCommand { get; }
    public ICommand ChooseThemeSettingsImageCommand { get; }
    public ICommand ChooseThemeAppsImageCommand { get; }
    public ICommand SaveThemeCommand { get; }
    public ICommand ToggleQuickMenuCommand { get; }
    public ICommand OpenMyGamesCommand { get; }
    public ICommand OpenMyAppsCommand { get; }
    public ICommand OpenMyPinsCommand { get; }
    public ICommand CloseMyGamesCommand { get; }
    public ICommand OpenLauncherSettingsCommand { get; }
    public ICommand CloseLauncherSettingsCommand { get; }
    public ICommand ChooseSelectedHomeImageCommand { get; }
    public ICommand ChooseSelectedGameMenuImageCommand { get; }
    public ICommand SaveSelectedGameCommand { get; }
    public ICommand SetOpenTrayGameCommand { get; }
    public ICommand RemoveSelectedGameCommand { get; }
    public ICommand OpenProfileEditorCommand { get; }
    public ICommand CloseProfileEditorCommand { get; }
    public ICommand OpenMusicPlayerCommand { get; }
    public ICommand CloseMusicPlayerCommand { get; }
    public ICommand OpenMusicFolderCommand { get; }
    public ICommand OpenMusicVisualizerFullscreenCommand { get; }
    public ICommand PlayPauseMusicCommand { get; }
    public ICommand StopMusicCommand { get; }
    public ICommand NextMusicCommand { get; }
    public ICommand PreviousMusicCommand { get; }
    public ICommand ToggleShuffleMusicCommand { get; }
    public ICommand VolumeDownCommand { get; }
    public ICommand VolumeUpCommand { get; }
    public ICommand PlaySelectedMusicCommand { get; }
    public ICommand ChooseProfilePictureCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand ShutdownCommand { get; }
    public ICommand OpenYouTubeCommand { get; }
    public ICommand OpenFriendsOverlayCommand { get; }
    public ICommand SwitchTabCommand { get; }

    public async Task InitializeAsync()
    {
        await ReloadSavedDataAsync();
        await _settingsService.SaveAsync(Settings);
    }

    private async Task ReloadSavedDataAsync()
    {
        Settings = await _settingsService.LoadAsync();
        await LoadThemesAsync();
        Profile = await _profileService.LoadAsync();
        EnsureProfileDefaults();
        Settings.ThemeName = NormalizeThemeName(Settings.ThemeName);
        ApplySelectedTheme(Settings.ThemeName);
        Settings.SocialIntegrationMode = SocialIntegrationMode.LocalOnly;
        Settings.DiscordUserId = string.Empty;
        Settings.DiscordDisplayName = string.Empty;
        Settings.DiscordAvatarPathOrUrl = string.Empty;
        Settings.DiscordAccessTokenEncrypted = string.Empty;
        Settings.DiscordGrantedScopes = string.Empty;
        Settings.DiscordTokenType = string.Empty;
        _library = await _libraryService.LoadAsync();
        foreach (var game in _library.Games)
        {
            game.Title ??= string.Empty;
            game.Platform ??= string.Empty;
            game.Genre ??= string.Empty;
            game.MultiplayerInfo ??= string.Empty;
            game.CoOpInfo ??= string.Empty;
            game.ExecutablePath ??= string.Empty;
            game.Arguments ??= string.Empty;
            game.WorkingDirectory ??= string.Empty;
            game.CoverArtPath ??= string.Empty;
            game.BackgroundArtPath ??= string.Empty;
            game.LaunchType = string.IsNullOrWhiteSpace(game.LaunchType) ? "Exe" : game.LaunchType;
            game.SteamAppId ??= string.Empty;
            game.InstallPath ??= string.Empty;
            game.LaunchCommand ??= string.Empty;
        }

        _library.Games = _library.Games
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        SyncGamesCollectionFromLibrary();

        SelectedGame = Games.FirstOrDefault(game => game.Game.IsFavorite) ?? Games.FirstOrDefault();
        FeaturedGame = SelectedGame;
        TrayGame = Games.FirstOrDefault(game => string.Equals(game.Game.Id, Settings.OpenTrayGameId, StringComparison.OrdinalIgnoreCase));
        RefreshDerivedLists();
        LoadMusicLibrary();
        ResetPendingThemeDraft();
    }

    public void UpdateClock()
        => ClockText = DateTime.Now.ToString("h:mm tt  ddd, MMM d");

    public void HandleInput(DashboardInputAction action)
    {
        switch (action)
        {
            case DashboardInputAction.PreviousTab:
                MoveTab(-1);
                break;
            case DashboardInputAction.NextTab:
                MoveTab(1);
                break;
            case DashboardInputAction.Back:
                GoBack();
                break;
            case DashboardInputAction.Details:
                if (IsMusicPlayerOpen)
                {
                    OpenMusicVisualizerFullscreen();
                }
                else if (IsMyGamesOpen)
                {
                    IsDetailsOpen = SelectedGame is not null;
                    _audioService.Play("select");
                }
                else
                {
                    IsDetailsOpen = SelectedGame is not null;
                    _audioService.Play("select");
                }
                break;
            case DashboardInputAction.Search:
                if (IsDetailsOpen)
                {
                    _ = SetOpenTrayGameAsync(null);
                }
                else if (IsMyGamesOpen)
                {
                    _ = ToggleFavoriteAsync(null);
                }
                else
                {
                    OpenSearch();
                }
                break;
            case DashboardInputAction.Options:
                IsQuickMenuOpen = !IsQuickMenuOpen;
                break;
            case DashboardInputAction.Activate:
                break;
            default:
                _audioService.Play("focus");
                break;
        }
    }

    public void MoveTab(int delta)
    {
        if (CurrentTab is null)
        {
            CurrentTab = Tabs[1];
            return;
        }

        var index = Tabs.IndexOf(CurrentTab);
        var next = Math.Clamp(index + delta, 0, Tabs.Count - 1);
        if (next == index)
        {
            return;
        }

        _pendingTabSound = delta < 0 ? "page-left" : "page-right";
        CurrentTab = Tabs[next];
    }

    public void SelectGame(GameCardViewModel? game)
    {
        if (game is null)
        {
            return;
        }

        SelectedGame = game;
        _audioService.Play("focus");
    }

    private void OpenMyGames()
        => OpenLibraryMenu(showPins: false, showApps: false);

    private void OpenMyApps()
        => OpenLibraryMenu(showPins: false, showApps: true);

    private void OpenMyPins()
        => OpenLibraryMenu(showPins: true, showApps: false);

    private void OpenLibraryMenu(bool showPins, bool showApps)
    {
        _isLibraryShowingPins = showPins;
        _isLibraryShowingApps = showApps;

        var visibleGames = LibraryMenuGames.ToList();
        if (visibleGames.Count > 0 && (SelectedGame is null || !visibleGames.Contains(SelectedGame)))
        {
            SelectedGame = visibleGames.FirstOrDefault();
        }

        IsMyGamesOpen = true;
        IsLauncherSettingsOpen = false;
        IsProfileEditorOpen = false;
        IsThemeMenuOpen = false;
        IsThemeCreatorOpen = false;
        IsSteamSetupOpen = false;
        IsMusicPlayerOpen = false;
        IsQuickMenuOpen = false;
        IsDetailsOpen = false;
        OnPropertyChanged(nameof(LibraryMenuTitle));
        OnPropertyChanged(nameof(LibraryMenuFilterText));
        OnPropertyChanged(nameof(LibraryMenuGames));
        RefreshVisibleLibraryMenuGames();
        OnPropertyChanged(nameof(LibraryMenuCountText));
        OnPropertyChanged(nameof(LibraryMenuXHintText));
        OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
        _audioService.Play("select");
    }

    private void OpenLauncherSettings()
    {
        IsLauncherSettingsOpen = true;
        IsMyGamesOpen = false;
        IsProfileEditorOpen = false;
        IsThemeMenuOpen = false;
        IsSteamSetupOpen = false;
        IsMusicPlayerOpen = false;
        IsQuickMenuOpen = false;
        IsDetailsOpen = false;
        OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
        _audioService.Play("select");
    }

    private void OpenProfileEditor()
    {
        IsProfileEditorOpen = true;
        IsMyGamesOpen = false;
        IsLauncherSettingsOpen = false;
        IsThemeMenuOpen = false;
        IsThemeCreatorOpen = false;
        IsSteamSetupOpen = false;
        IsMusicPlayerOpen = false;
        IsQuickMenuOpen = false;
        IsDetailsOpen = false;
        OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
        _audioService.Play("select");
    }

    private void OpenThemeMenu()
    {
        IsThemeMenuOpen = true;
        IsThemeCreatorOpen = false;
        IsSteamSetupOpen = false;
        IsMyGamesOpen = false;
        IsLauncherSettingsOpen = false;
        IsProfileEditorOpen = false;
        IsMusicPlayerOpen = false;
        IsQuickMenuOpen = false;
        IsDetailsOpen = false;
        _audioService.Play("select");
    }

    private void OpenThemeCreator()
    {
        IsThemeCreatorOpen = true;
        IsThemeMenuOpen = false;
        IsSteamSetupOpen = false;
        IsMyGamesOpen = false;
        IsProfileEditorOpen = false;
        IsMusicPlayerOpen = false;
        IsQuickMenuOpen = false;
        IsDetailsOpen = false;
        if (!IsLauncherSettingsOpen)
        {
            IsLauncherSettingsOpen = true;
        }

        ResetPendingThemeDraft();
        _audioService.Play("select");
    }

    private void CloseThemeCreator()
    {
        IsThemeCreatorOpen = false;
        _audioService.Play("back");
    }

    private void OpenMusicPlayer(bool transparent = false)
    {
        IsMusicPlayerTransparent = transparent;
        IsMusicVisualizerFullscreen = false;
        LoadMusicLibrary();
        IsMusicPlayerOpen = true;
        IsMyGamesOpen = false;
        IsLauncherSettingsOpen = false;
        IsProfileEditorOpen = false;
        IsThemeMenuOpen = false;
        IsThemeCreatorOpen = false;
        IsSteamSetupOpen = false;
        IsQuickMenuOpen = false;
        IsDetailsOpen = false;
        OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
        _audioService.Play("select");
    }

    private void CloseMusicPlayer()
    {
        IsMusicVisualizerFullscreen = false;
        IsMusicPlayerOpen = false;
        IsMusicPlayerTransparent = false;
        OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
        _audioService.Play("back");
    }

    private void OpenMusicFolder()
    {
        var folder = GetMusicFolder();
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
    }

    private void OpenMusicVisualizerFullscreen()
    {
        if (!IsMusicPlayerOpen || IsMusicVisualizerFullscreen)
        {
            return;
        }

        IsMusicVisualizerFullscreen = true;
        _audioService.Play("select");
    }

    private async Task LaunchGameAsync(GameCardViewModel? card)
    {
        card ??= SelectedGame;
        if (card is null)
        {
            StatusMessage = "No game selected";
            return;
        }

        try
        {
            SelectedGame = card;
            _runningGameService.BeginLaunch(card.Game, DateTimeOffset.UtcNow);

            if (Settings.MinimizeOnGameLaunch && Application.Current?.MainWindow is { } window)
            {
                window.WindowState = WindowState.Minimized;
            }

            var launchResult = await _launchService.LaunchAsync(card.Game);
            _runningGameService.Track(card.Game, launchResult.TrackedProcess);
            await BringLaunchedGameToForegroundAsync(launchResult.TrackedProcess);
            card.Game.LastPlayed = DateTimeOffset.Now;
            if (card.Game.Playtime < TimeSpan.Zero)
            {
                card.Game.Playtime = TimeSpan.Zero;
            }
            await PersistLibraryAsync();
            StatusMessage = $"Launching {card.Title}";
            _audioService.Play("select");
        }
        catch (Exception ex)
        {
            _runningGameService.Clear();
            StatusMessage = ex.Message;
        }
    }

    private static async Task BringLaunchedGameToForegroundAsync(Process? process)
    {
        if (process is null)
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (process.HasExited)
                {
                    return;
                }

                process.Refresh();
                var handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, ShowWindowRestore);
                    SetForegroundWindow(handle);
                    return;
                }
            }
            catch
            {
                return;
            }

            await Task.Delay(250);
        }
    }

    private async Task SubmitSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            StatusMessage = "Type a Bing search first";
            return;
        }

        await _searchService.SearchWebAsync(SearchQuery, Settings.BingSearchBaseUrl);
        StatusMessage = $"Searching Bing for {SearchQuery}";
        IsSearchOverlayOpen = false;
        _audioService.Play("select");
    }

    private void OpenSearch()
    {
        CurrentTab = Tabs.First(tab => tab.Key == "bing");
        IsSearchOverlayOpen = true;
        _audioService.Play("select");
    }

    private void RequestFriendsOverlay()
    {
        IsQuickMenuOpen = false;
        IsDetailsOpen = false;
        FriendsOverlayRequested?.Invoke(this, EventArgs.Empty);
    }

    public Task<RunningGameCloseResult> CloseRunningGameAsync(bool forceKill, CancellationToken cancellationToken = default)
        => _runningGameService.CloseAsync(forceKill, cancellationToken);

    private void GoBack()
    {
        if (IsSearchOverlayOpen)
        {
            IsSearchOverlayOpen = false;
            _audioService.Play("back");
            return;
        }

        if (IsDetailsOpen)
        {
            IsDetailsOpen = false;
            _audioService.Play("back");
            return;
        }

        if (IsThemeCreatorOpen)
        {
            IsThemeCreatorOpen = false;
            _audioService.Play("back");
            return;
        }

        if (IsSteamSetupOpen)
        {
            IsSteamSetupOpen = false;
            _audioService.Play("back");
            return;
        }

        if (IsThemeMenuOpen)
        {
            IsThemeMenuOpen = false;
            _audioService.Play("back");
            return;
        }

        if (IsMyGamesOpen)
        {
            IsMyGamesOpen = false;
            OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
            _audioService.Play("back");
            return;
        }

        if (IsLauncherSettingsOpen)
        {
            IsLauncherSettingsOpen = false;
            OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
            _audioService.Play("back");
            return;
        }

        if (IsProfileEditorOpen)
        {
            IsProfileEditorOpen = false;
            _audioService.Play("back");
            return;
        }

        if (IsMusicPlayerOpen)
        {
            if (IsMusicVisualizerFullscreen)
            {
                IsMusicVisualizerFullscreen = false;
            }
            else
            {
                IsMusicPlayerOpen = false;
            }

            _audioService.Play("back");
            return;
        }

        if (IsQuickMenuOpen)
        {
            IsQuickMenuOpen = false;
            _audioService.Play("back");
            return;
        }

        CurrentTab = Tabs[1];
        _audioService.Play("back");
    }

    private async Task AddGameAsync()
    {
        var executable = _filePickerService.PickExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        var destination = NormalizeAddDestination(Settings.DefaultAddDestination);
        var game = new GameMetadata
        {
            Title = Path.GetFileNameWithoutExtension(executable).Replace("_", " "),
            LaunchType = "Exe",
            ExecutablePath = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty,
            Platform = "PC",
            Genre = destination == "My Apps" ? "App" : "Manual"
        };

        _library.Games.Add(game);
        var card = new GameCardViewModel(game, _accentBrushes[Games.Count % _accentBrushes.Count]);
        Games.Add(card);
        SortGamesByTitle(game.Id);
        await PersistLibraryAsync();
        OnPropertyChanged(nameof(MyGamesCountText));
        StatusMessage = $"Added {game.Title} to {destination}";
    }

    private async Task EditSelectedGameAsync(object? _)
    {
        if (SelectedGame is null)
        {
            return;
        }

        var executable = _filePickerService.PickExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        SelectedGame.Game.ExecutablePath = executable;
        SelectedGame.Game.WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty;
        await PersistLibraryAsync();
        StatusMessage = $"Updated {SelectedGame.Title}";
    }

    private async Task ChooseSelectedHomeImageAsync(object? _)
    {
        if (SelectedGame is null)
        {
            StatusMessage = "Choose a game first";
            return;
        }

        var image = _filePickerService.PickImage(GetCustomCoverFolder("Home Screen Cover"));
        if (string.IsNullOrWhiteSpace(image))
        {
            return;
        }

        SelectedGame.Game.BackgroundArtPath = CopyCustomArtwork(image, "Home Screen Cover", SelectedGame.Title);
        SelectedGame.Refresh();
        if (ReferenceEquals(TrayGame, SelectedGame))
        {
            OnPropertyChanged(nameof(OpenTrayCoverArtPath));
        }

        await PersistLibraryAsync();
        StatusMessage = $"Updated Home image for {SelectedGame.Title}";
    }

    private async Task ChooseSelectedGameMenuImageAsync(object? _)
    {
        if (SelectedGame is null)
        {
            StatusMessage = "Choose a game first";
            return;
        }

        var image = _filePickerService.PickImage(GetCustomCoverFolder("Game Menu Cover"));
        if (string.IsNullOrWhiteSpace(image))
        {
            return;
        }

        SelectedGame.Game.CoverArtPath = CopyCustomArtwork(image, "Game Menu Cover", SelectedGame.Title);
        SelectedGame.Refresh();

        await PersistLibraryAsync();
        StatusMessage = $"Updated My Games image for {SelectedGame.Title}";
    }

    private async Task SaveSelectedGameAsync(object? _)
    {
        if (SelectedGame is null)
        {
            StatusMessage = "Choose a game first";
            return;
        }

        SelectedGame.Refresh();
        if (ReferenceEquals(TrayGame, SelectedGame))
        {
            OnPropertyChanged(nameof(OpenTrayTitle));
        }

        SortGamesByTitle(SelectedGame.Game.Id);
        await PersistLibraryAsync();
        StatusMessage = $"Saved {SelectedGame.Title}";
    }

    private async Task SetOpenTrayGameAsync(object? _)
    {
        if (SelectedGame is null)
        {
            StatusMessage = "Choose a game first";
            return;
        }

        TrayGame = SelectedGame;
        Settings.OpenTrayGameId = SelectedGame.Game.Id;
        await SaveSettingsAsync();
        StatusMessage = $"{SelectedGame.Title} is now on Open Tray";
    }

    private async Task RemoveSelectedGameAsync()
    {
        if (SelectedGame is null)
        {
            StatusMessage = "Choose a game first";
            return;
        }

        var removed = SelectedGame;
        _library.Games.Remove(removed.Game);
        Games.Remove(removed);

        if (string.Equals(Settings.OpenTrayGameId, removed.Game.Id, StringComparison.OrdinalIgnoreCase))
        {
            Settings.OpenTrayGameId = string.Empty;
            TrayGame = null;
            await _settingsService.SaveAsync(Settings);
        }

        SelectedGame = Games.FirstOrDefault();
        FeaturedGame = SelectedGame;
        SortGamesByTitle(SelectedGame?.Game.Id);
        await PersistLibraryAsync();
        StatusMessage = $"Removed {removed.Title} from My Games";
    }

    private async Task ChooseProfilePictureAsync(object? _)
    {
        var image = _filePickerService.PickImage(Path.Combine(AppPaths.AppFolder, "Assets", "Profile"));
        if (string.IsNullOrWhiteSpace(image))
        {
            return;
        }

        Profile = new Profile
        {
            Gamertag = Profile.Gamertag,
            GamerPicturePath = image,
            Gamerscore = Profile.Gamerscore,
            OnlineStatus = Profile.OnlineStatus,
            Motto = Profile.Motto,
            Description = Profile.Description
        };
        await _profileService.SaveAsync(Profile);
        StatusMessage = "Profile picture updated";
    }

    private async Task SaveProfileAsync()
    {
        EnsureProfileDefaults();
        await _profileService.SaveAsync(Profile);
        OnPropertyChanged(nameof(Profile));
        StatusMessage = "Profile saved";
    }

    private async Task ShutdownAsync()
    {
        await _settingsService.SaveAsync(Settings);
        await _profileService.SaveAsync(Profile);
        Application.Current.Shutdown();
    }

    private async Task OpenSteamSetupAsync()
    {
        var config = await _steamCommunityService.LoadConfigAsync();
        SteamSetupApiKey = config.SteamApiKey;
        SteamSetupSteamId64 = config.SteamId64;
        SteamSetupStatus = _steamCommunityService.IsConfigured
            ? "Steam is connected. Use Test Connection to check it."
            : "Paste your Steam Web API key and SteamID64.";
        IsSteamSetupOpen = true;
        _audioService.Play("select");
    }

    private async Task SaveSteamSetupAsync()
    {
        var config = BuildSteamSetupConfig();
        if (string.IsNullOrWhiteSpace(config.SteamApiKey) || string.IsNullOrWhiteSpace(config.SteamId64))
        {
            SteamSetupStatus = "Steam API key and SteamID64 are both required.";
            _audioService.Play("back");
            return;
        }

        await _steamCommunityService.SaveConfigAsync(config);
        SteamSetupStatus = "Steam setup saved.";
        StatusMessage = "Steam setup saved";
        _audioService.Play("select");
    }

    private async Task TestSteamSetupAsync()
    {
        SteamSetupStatus = "Testing Steam connection...";
        var result = await _steamCommunityService.TestConnectionAsync(BuildSteamSetupConfig());
        SteamSetupStatus = result.Message;
        StatusMessage = result.Success ? $"Steam connected: {result.DisplayName}" : result.Message;
        _audioService.Play(result.Success ? "select" : "back");
    }

    private SteamCommunityConfig BuildSteamSetupConfig()
        => new()
        {
            SteamApiKey = SteamSetupApiKey.Trim(),
            SteamId64 = ExtractSteamId64(SteamSetupSteamId64)
        };

    private void OpenYouTube()
    {
        OpenExternalUrl("https://www.youtube.com", "Opening YouTube");
    }

    private void OpenSelectedGameStore()
    {
        if (SelectedGame is null ||
            !string.Equals(SelectedGame.Game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(SelectedGame.Game.SteamAppId))
        {
            StatusMessage = "Steam store is only available for Steam games";
            return;
        }

        OpenExternalUrl($"steam://store/{SelectedGame.Game.SteamAppId}", $"Opening {SelectedGame.Title} in Steam");
    }

    private void OpenExternalUrl(string url, string statusMessage)
    {
        try
        {
            if (Application.Current?.MainWindow is { } window)
            {
                window.WindowState = WindowState.Minimized;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            StatusMessage = statusMessage;
            _audioService.Play("select");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private static string GetClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractSteamId64(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.All(char.IsDigit))
        {
            return trimmed;
        }

        const string marker = "/profiles/";
        var markerIndex = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var start = markerIndex + marker.Length;
            var digits = new string(trimmed.Skip(start).TakeWhile(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(digits))
            {
                return digits;
            }
        }

        return trimmed;
    }

    private static string GetCustomCoverFolder(string folderName)
        => EnsureDirectory(Path.Combine(AppPaths.AppFolder, "Assets", "Custom Files", "CoverArt", folderName));

    private static string GetMusicFolder()
        => EnsureDirectory(AppPaths.FindFolder(
            Path.Combine("Assets", "Custom Files", "Music Files"),
            folder => Directory.EnumerateFiles(folder).Any(IsSupportedMusicFile)));

    private static bool IsSupportedMusicFile(string path)
        => Path.GetExtension(path) is { } extension
           && (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".wma", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase));

    private void AudioAnalysis_OnFrameReady(object? sender, AudioAnalysisFrame frame)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyAudioAnalysis(frame);
            return;
        }

        dispatcher.BeginInvoke(new Action(() => ApplyAudioAnalysis(frame)), DispatcherPriority.Render);
    }

    private void ApplyAudioAnalysis(AudioAnalysisFrame frame)
    {
        VisualizerBass = frame.Bass;
        VisualizerMid = frame.Mid;
        VisualizerTreble = frame.Treble;
        VisualizerLoudness = frame.Loudness;
        VisualizerPeak = frame.Peak;
    }

    private void EnsureAudioAnalysisState()
    {
        if (IsMusicPlaying)
        {
            _audioAnalysisService.Start();
        }
        else
        {
            _audioAnalysisService.Stop();
        }

        OnPropertyChanged(nameof(IsAudioAnalysisRunning));
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CopyCustomArtwork(string sourcePath, string folderName, string title)
    {
        var folder = GetCustomCoverFolder(folderName);
        var fullSource = Path.GetFullPath(sourcePath);
        if (string.Equals(Path.GetDirectoryName(fullSource), folder, StringComparison.OrdinalIgnoreCase))
        {
            return fullSource;
        }

        var extension = Path.GetExtension(fullSource);
        var fileName = MakeSafeFileName(title);
        var destination = Path.Combine(folder, $"{fileName}{extension}");
        var count = 2;
        while (File.Exists(destination))
        {
            destination = Path.Combine(folder, $"{fileName} {count++}{extension}");
        }

        File.Copy(fullSource, destination);
        return destination;
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "cover" : safe;
    }

    private void LoadMusicLibrary()
    {
        var folder = GetMusicFolder();
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3",
            ".wav",
            ".wma",
            ".m4a",
            ".aac"
        };

        var selectedPath = CurrentMusicTrack?.Path;
        var files = Directory.EnumerateFiles(folder)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        MusicTracks.Clear();
        foreach (var file in files)
        {
            MusicTracks.Add(new MusicTrackViewModel(file));
        }

        _musicIndex = selectedPath is null
            ? -1
            : MusicTracks.ToList().FindIndex(track => string.Equals(track.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        CurrentMusicTrack = _musicIndex >= 0 ? MusicTracks[_musicIndex] : null;
        OnPropertyChanged(nameof(MusicTrackCountText));
    }

    public void EnsureMusicLibraryLoaded()
    {
        LoadMusicLibrary();
    }

    private void PlayMusicTrack(MusicTrackViewModel? track)
    {
        if (track is null)
        {
            if (CurrentMusicTrack is not null)
            {
                _musicPlayer.Play();
                _musicTimer.Start();
                IsMusicPlaying = true;
            }

            return;
        }

        var index = MusicTracks.IndexOf(track);
        if (index < 0 || !File.Exists(track.Path))
        {
            return;
        }

        _musicIndex = index;
        CurrentMusicTrack = track;
        _musicPlayer.Open(new Uri(track.Path, UriKind.Absolute));
        _musicPlayer.Volume = MusicVolume;
        _musicPlayer.Play();
        _musicTimer.Start();
        IsMusicPlaying = true;
        StatusMessage = $"Playing {track.Title}";
        RefreshMusicProgress();
    }

    private void ToggleMusicPlayback()
    {
        if (CurrentMusicTrack is null)
        {
            if (MusicTracks.Count == 0)
            {
                LoadMusicLibrary();
            }

            PlayMusicTrack(MusicTracks.FirstOrDefault());
            return;
        }

        if (IsMusicPlaying)
        {
            _musicPlayer.Pause();
            _musicTimer.Stop();
            IsMusicPlaying = false;
            return;
        }

        _musicPlayer.Play();
        _musicTimer.Start();
        IsMusicPlaying = true;
    }

    private void StopMusic()
    {
        _musicPlayer.Stop();
        _musicTimer.Stop();
        IsMusicPlaying = false;
        MusicProgress = 0;
        MusicPositionText = "0:00";
    }

    private void NextMusicTrack()
    {
        if (MusicTracks.Count == 0)
        {
            LoadMusicLibrary();
        }

        if (MusicTracks.Count == 0)
        {
            return;
        }

        var next = IsShuffleEnabled
            ? _random.Next(MusicTracks.Count)
            : (_musicIndex + 1 + MusicTracks.Count) % MusicTracks.Count;
        PlayMusicTrack(MusicTracks[next]);
    }

    private void PreviousMusicTrack()
    {
        if (MusicTracks.Count == 0)
        {
            LoadMusicLibrary();
        }

        if (MusicTracks.Count == 0)
        {
            return;
        }

        var previous = (_musicIndex - 1 + MusicTracks.Count) % MusicTracks.Count;
        PlayMusicTrack(MusicTracks[previous]);
    }

    private void RefreshMusicProgress()
    {
        var position = _musicPlayer.Position;
        MusicPositionText = FormatTime(position);

        if (_musicPlayer.NaturalDuration.HasTimeSpan)
        {
            var duration = _musicPlayer.NaturalDuration.TimeSpan;
            MusicDurationText = FormatTime(duration);
            MusicProgress = duration.TotalSeconds <= 0 ? 0 : Math.Clamp(position.TotalSeconds / duration.TotalSeconds * 100, 0, 100);
        }
        else
        {
            MusicDurationText = "0:00";
            MusicProgress = 0;
        }
    }

    private static string FormatTime(TimeSpan value)
        => value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"m\:ss");

    private async Task ScanFolderAsync()
    {
        var folder = _filePickerService.PickFolder();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (!_library.LibraryPaths.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            _library.LibraryPaths.Add(folder);
        }

        var scanned = await _libraryService.ScanFolderAsync(folder);
        var knownPaths = _library.Games
            .Select(game => game.ExecutablePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var destination = NormalizeAddDestination(Settings.DefaultAddDestination);
        foreach (var game in scanned.Where(game => knownPaths.Add(game.ExecutablePath)))
        {
            if (destination == "My Apps")
            {
                game.Genre = "App";
            }

            _library.Games.Add(game);
            Games.Add(new GameCardViewModel(game, _accentBrushes[Games.Count % _accentBrushes.Count]));
            added++;
        }

        SortGamesByTitle(SelectedGame?.Game.Id);
        await PersistLibraryAsync();
        OnPropertyChanged(nameof(MyGamesCountText));
        StatusMessage = added == 1 ? $"Imported 1 item to {destination}" : $"Imported {added} items to {destination}";
    }

    private async Task ToggleFavoriteAsync(object? _)
    {
        if (SelectedGame is null)
        {
            return;
        }

        var toggledGame = SelectedGame;
        toggledGame.Game.IsFavorite = !toggledGame.Game.IsFavorite;
        await PersistLibraryAsync();
        RefreshDerivedLists();

        if (toggledGame.Game.IsFavorite)
        {
            _audioService.Play("select");
        }

        if (_isLibraryShowingPins && toggledGame.Game.IsFavorite == false)
        {
            SelectedGame = LibraryMenuGames.FirstOrDefault();
        }

        StatusMessage = toggledGame.Game.IsFavorite ? "Pinned to Home" : "Removed from pins";
    }

    private async Task RefreshSelectedGameDetailsAsync()
    {
        var selected = SelectedGame;
        if (selected is null
            || !string.Equals(selected.Game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(selected.Game.SteamAppId))
        {
            selected?.Refresh();
            return;
        }

        try
        {
            var details = await _steamCommunityService.LoadGameDetailsAsync(selected.Game.SteamAppId);
            var changed = false;

            if (details.Playtime is { } playtime && playtime != selected.Game.Playtime)
            {
                selected.Game.Playtime = playtime;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(details.Genre)
                && !string.Equals(selected.Game.Genre, details.Genre, StringComparison.Ordinal))
            {
                selected.Game.Genre = details.Genre;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(details.Rating)
                && !string.Equals(selected.Game.Rating, details.Rating, StringComparison.Ordinal))
            {
                selected.Game.Rating = details.Rating;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(details.MultiplayerInfo)
                && !string.Equals(selected.Game.MultiplayerInfo, details.MultiplayerInfo, StringComparison.Ordinal))
            {
                selected.Game.MultiplayerInfo = details.MultiplayerInfo;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(details.CoOpInfo)
                && !string.Equals(selected.Game.CoOpInfo, details.CoOpInfo, StringComparison.Ordinal))
            {
                selected.Game.CoOpInfo = details.CoOpInfo;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(details.StoreScreenshotPath)
                && !string.Equals(selected.Game.StoreScreenshotPath, details.StoreScreenshotPath, StringComparison.Ordinal))
            {
                selected.Game.StoreScreenshotPath = details.StoreScreenshotPath;
                changed = true;
            }

            if (Math.Abs(selected.Game.ReviewStarRating - details.ReviewStarRating) > 0.01)
            {
                selected.Game.ReviewStarRating = details.ReviewStarRating;
                changed = true;
            }

            if (selected.Game.ReviewCount != details.ReviewCount)
            {
                selected.Game.ReviewCount = details.ReviewCount;
                changed = true;
            }

            selected.Refresh();
            if (changed)
            {
                await PersistLibraryAsync();
            }
        }
        catch
        {
            selected.Refresh();
        }
    }

    private async Task SaveSettingsAsync()
    {
        await _settingsService.SaveAsync(Settings);
        await PersistLibraryAsync();
        _startupRegistrationService.SetLaunchOnStartup(Settings.LaunchOnWindowsStartup);
        StatusMessage = "Settings saved";
    }

    private async Task ExportDataAsync()
    {
        var path = _filePickerService.PickSaveJsonFile();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await _importExportService.ExportAsync(_library, Profile, Settings, path);
            StatusMessage = "Backup exported";
            MessageBox.Show(
                $"Dashboard data exported successfully.{Environment.NewLine}{Environment.NewLine}{path}",
                "Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.LogException(ex, "DashboardViewModel.ExportDataAsync");
            MessageBox.Show(
                $"Export failed.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Export Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ImportDataAsync()
    {
        var path = _filePickerService.PickJsonFile();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var result = await _importExportService.ImportAsync(path);
        if (!result.Success)
        {
            MessageBox.Show(
                result.Message,
                "Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        await ReloadSavedDataAsync();
        _startupRegistrationService.SetLaunchOnStartup(Settings.LaunchOnWindowsStartup);
        StatusMessage = "Backup imported";

        var successMessage = result.SafetyBackupPath is null
            ? result.Message
            : $"{result.Message}{Environment.NewLine}{Environment.NewLine}Safety backup created:{Environment.NewLine}{result.SafetyBackupPath}";

        MessageBox.Show(
            successMessage,
            "Import Complete",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task ScanSteamGamesAsync()
    {
        try
        {
            var result = await _steamLibraryScannerService.ScanAsync(_library);
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                StatusMessage = result.Message;
            }

            if (result.Added > 0 || result.Updated > 0)
            {
                SyncGamesCollectionFromLibrary();
                RefreshDerivedLists();
                SortGamesByTitle(SelectedGame?.Game.Id);
                await PersistLibraryAsync();
            }

            WriteSteamScanDebugReport(result);

            MessageBox.Show(
                string.IsNullOrWhiteSpace(result.Message)
                    ? $"Steam scan complete.{Environment.NewLine}{Environment.NewLine}Added: {result.Added}{Environment.NewLine}Updated: {result.Updated}{Environment.NewLine}Skipped: {result.Skipped}"
                    : $"{result.Message}{Environment.NewLine}{Environment.NewLine}Added: {result.Added}{Environment.NewLine}Updated: {result.Updated}{Environment.NewLine}Skipped: {result.Skipped}",
                "Scan Steam Games",
                MessageBoxButton.OK,
                result.Added > 0 || result.Updated > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            App.LogException(ex, "DashboardViewModel.ScanSteamGamesAsync");
            StatusMessage = "Steam scan failed";
            MessageBox.Show(
                $"Steam scan failed.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Scan Steam Games",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task LoadThemesAsync()
    {
        var themes = await _themeService.LoadThemesAsync();
        AvailableThemes.Clear();
        foreach (var theme in themes)
        {
            AvailableThemes.Add(theme);
        }
    }

    private async Task SelectThemeAsync(object? parameter)
    {
        var theme = parameter as DashboardTheme
            ?? SelectedTheme
            ?? AvailableThemes.FirstOrDefault(themeItem => themeItem.IsBuiltIn)
            ?? new DashboardTheme { Name = DashboardTheme.BuiltInThemeName, IsBuiltIn = true };

        ApplySelectedTheme(theme.Name);
        Settings.ThemeName = theme.Name;
        await _settingsService.SaveAsync(Settings);
        IsThemeMenuOpen = false;
        OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
        StatusMessage = theme.IsBuiltIn ? "Xbox 360 theme restored" : $"Theme selected: {theme.Name}";
        _audioService.Play("select");
    }

    private async Task ChooseThemeSectionImageAsync(string sectionKey)
    {
        var image = _filePickerService.PickImage();
        if (string.IsNullOrWhiteSpace(image))
        {
            return;
        }

        switch (sectionKey)
        {
            case "home":
                ThemeHomePreviewPath = image;
                break;
            case "games":
                ThemeGamesPreviewPath = image;
                break;
            case "settings":
                ThemeSettingsPreviewPath = image;
                break;
            case "apps":
                ThemeAppsPreviewPath = image;
                break;
        }

        await Task.CompletedTask;
        StatusMessage = "Theme preview updated";
    }

    private async Task SaveThemeAsync(object? _)
    {
        if (string.IsNullOrWhiteSpace(ThemeNameInput))
        {
            StatusMessage = "Enter a theme name first";
            return;
        }

        var createdTheme = await _themeService.CreateThemeAsync(
            ThemeNameInput,
            ThemeHomePreviewPath,
            ThemeGamesPreviewPath,
            ThemeSettingsPreviewPath,
            ThemeAppsPreviewPath);

        await LoadThemesAsync();
        ApplySelectedTheme(createdTheme.Name);
        Settings.ThemeName = createdTheme.Name;
        await _settingsService.SaveAsync(Settings);
        IsThemeCreatorOpen = false;
        OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
        ResetPendingThemeDraft();
        StatusMessage = $"Created theme: {createdTheme.Name}";
        _audioService.Play("select");
    }

    private void ApplySelectedTheme(string? themeName)
    {
        var normalizedName = NormalizeThemeName(themeName);
        SelectedTheme = AvailableThemes.FirstOrDefault(theme => string.Equals(theme.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
            ?? AvailableThemes.FirstOrDefault(theme => theme.IsBuiltIn)
            ?? new DashboardTheme { Name = DashboardTheme.BuiltInThemeName, IsBuiltIn = true };
        OnPropertyChanged(nameof(ThemeMenuVisibilityTitle));
        OnPropertyChanged(nameof(CurrentThemeBackgroundPath));
    }

    private static string NormalizeThemeName(string? themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName)
            || string.Equals(themeName, "Metro Green", StringComparison.OrdinalIgnoreCase))
        {
            return DashboardTheme.BuiltInThemeName;
        }

        return themeName.Trim();
    }

    private string ResolveThemeSectionKey()
    {
        if (IsLauncherSettingsOpen)
        {
            return "settings";
        }

        if (IsMyGamesOpen)
        {
            return _isLibraryShowingApps ? "apps" : "games";
        }

        return CurrentTab?.Key switch
        {
            "bing" => "home",
            "home" => "home",
            "social" => "home",
            "video" => "home",
            "games" => "home",
            "music" => "home",
            "apps" => "home",
            "settings" => "home",
            _ => "home"
        };
    }

    private void ResetPendingThemeDraft()
    {
        ThemeNameInput = string.Empty;
        ThemeHomePreviewPath = string.Empty;
        ThemeGamesPreviewPath = string.Empty;
        ThemeSettingsPreviewPath = string.Empty;
        ThemeAppsPreviewPath = string.Empty;
    }

    private void EnsureProfileDefaults()
    {
        var defaultPicturePath = Path.Combine(AppPaths.AppFolder, "Assets", "Profile", "profilepicture.jpg");

        if (string.IsNullOrWhiteSpace(Profile.Gamertag))
        {
            Profile.Gamertag = "MetroPilot";
        }

        if (string.IsNullOrWhiteSpace(Profile.GamerPicturePath) || IsOldDefaultProfilePicture(Profile.GamerPicturePath))
        {
            Profile.GamerPicturePath = defaultPicturePath;
        }

        if (string.IsNullOrWhiteSpace(Profile.OnlineStatus))
        {
            Profile.OnlineStatus = "Online";
        }

        if (string.IsNullOrWhiteSpace(Profile.Motto))
        {
            Profile.Motto = "(No motto)";
        }

        if (string.IsNullOrWhiteSpace(Profile.Description))
        {
            Profile.Description = "(No bio)";
        }
    }

    private static bool IsOldDefaultProfilePicture(string path)
        => path.EndsWith(Path.Combine("Assets", "Art", "profilepicture.jpg"), StringComparison.OrdinalIgnoreCase)
           && !File.Exists(path);

    private static string NormalizeGameCoverFitMode(string? mode)
        => mode is "Cover" or "Fill" or "Fit" ? mode : "Auto";

    private static string NormalizeAddDestination(string? destination)
        => string.Equals(destination, "My Apps", StringComparison.OrdinalIgnoreCase) ? "My Apps" : "My Games";

    private static SocialIntegrationMode NormalizeSocialIntegrationMode(SocialIntegrationMode mode)
        => SocialIntegrationMode.LocalOnly;

    private static string ToSocialIntegrationDisplay(SocialIntegrationMode mode)
        => "Local";

    private static SocialIntegrationMode ParseSocialIntegrationMode(string? mode)
        => SocialIntegrationMode.LocalOnly;

    private static bool IsAppEntry(GameMetadata game)
        => string.Equals(game.Genre, "App", StringComparison.OrdinalIgnoreCase);

    private void SortGamesByTitle(string? selectedGameId = null)
    {
        selectedGameId ??= SelectedGame?.Game.Id;

        _library.Games = _library.Games
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var sortedCards = Games
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Games.Clear();
        foreach (var game in sortedCards)
        {
            Games.Add(game);
        }

        SelectedGame = Games.FirstOrDefault(game => string.Equals(game.Game.Id, selectedGameId, StringComparison.OrdinalIgnoreCase))
            ?? Games.FirstOrDefault();
        FeaturedGame = SelectedGame;
    }

    private async Task PersistLibraryAsync()
    {
        await _libraryService.SaveAsync(_library);
        RefreshDerivedLists();
    }

    private void SyncGamesCollectionFromLibrary()
    {
        Games.Clear();
        var index = 0;
        foreach (var game in _library.Games)
        {
            Games.Add(new GameCardViewModel(game, _accentBrushes[index++ % _accentBrushes.Count]));
        }
    }

    private void WriteSteamScanDebugReport(SteamGameScanResult result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SteamScanDebugLogPath)!);
            var builder = new StringBuilder();
            builder.AppendLine("[STEAM SCAN]");
            builder.AppendLine($"added: {result.Added}");
            builder.AppendLine($"updated: {result.Updated}");
            builder.AppendLine($"skipped: {result.Skipped}");
            builder.AppendLine($"message: {result.Message}");
            builder.AppendLine($"saved library game count: {_library.Games.Count}");
            builder.AppendLine($"loaded Games menu count: {Games.Count}");
            builder.AppendLine($"my games visible count: {Games.Count(game => !IsAppEntry(game.Game))}");
            builder.AppendLine($"library file path: {Path.Combine(AppPaths.AppFolder, "UserData", "library.json")}");
            File.WriteAllText(SteamScanDebugLogPath, builder.ToString());
        }
        catch
        {
        }
    }

    private void RefreshDerivedLists()
    {
        OnPropertyChanged(nameof(RecentGames));
        OnPropertyChanged(nameof(PinnedGames));
        OnPropertyChanged(nameof(ImportedGames));
        OnPropertyChanged(nameof(LibraryPaths));
        OnPropertyChanged(nameof(MyGamesCountText));
        OnPropertyChanged(nameof(LibraryMenuGames));
        RefreshVisibleLibraryMenuGames();
        OnPropertyChanged(nameof(LibraryMenuCountText));
    }

    private IEnumerable<GameCardViewModel> GetLibraryMenuGames()
        => _isLibraryShowingPins
            ? Games.Where(game => game.Game.IsFavorite)
            : _isLibraryShowingApps
                ? Games.Where(game => IsAppEntry(game.Game))
                : Games.Where(game => !IsAppEntry(game.Game));

    private void RefreshVisibleLibraryMenuGames()
    {
        var allGames = GetLibraryMenuGames().ToList();
        if (allGames.Count == 0)
        {
            _libraryMenuStartIndex = 0;
            VisibleLibraryMenuGames.Clear();
            return;
        }

        var selectedIndex = SelectedGame is null ? 0 : allGames.IndexOf(SelectedGame);
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        var windowSize = Math.Min(LibraryVisibleWindowSize, allGames.Count);
        var maxStart = Math.Max(0, allGames.Count - windowSize);

        _libraryMenuStartIndex = Math.Clamp(_libraryMenuStartIndex, 0, maxStart);
        if (selectedIndex < _libraryMenuStartIndex || selectedIndex >= _libraryMenuStartIndex + windowSize)
        {
            if (selectedIndex >= _libraryMenuStartIndex + windowSize && selectedIndex > maxStart)
            {
                _libraryMenuStartIndex = Math.Min(_libraryMenuStartIndex + 1, maxStart);
            }
            else
            {
                var pageStart = selectedIndex / windowSize * windowSize;
                _libraryMenuStartIndex = Math.Min(pageStart, maxStart);
            }
        }

        var includeStartIndex = Math.Max(0, _libraryMenuStartIndex - 1);
        var requestedCount = windowSize + 1;
        if (_libraryMenuStartIndex > 0)
        {
            requestedCount++;
        }

        var windowGames = allGames
            .Skip(includeStartIndex)
            .Take(Math.Min(requestedCount, allGames.Count - includeStartIndex))
            .ToList();

        if (VisibleLibraryMenuGames.Count == windowGames.Count
            && VisibleLibraryMenuGames.Zip(windowGames).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            OnPropertyChanged(nameof(LibraryMenuScrollOffset));
            return;
        }

        VisibleLibraryMenuGames.Clear();
        foreach (var game in windowGames)
        {
            VisibleLibraryMenuGames.Add(game);
        }

        OnPropertyChanged(nameof(LibraryMenuScrollOffset));
    }

    private void OnGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshDerivedLists();

    private void RunningGameService_OnStateChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            HandleRunningGameStateChangedOnUiThread();
            return;
        }

        dispatcher.BeginInvoke(new Action(HandleRunningGameStateChangedOnUiThread), DispatcherPriority.Background);
    }

    private void HandleRunningGameStateChangedOnUiThread()
    {
        OnPropertyChanged(nameof(HasRunningLaunchedGame));
        OnPropertyChanged(nameof(RunningLaunchedGameTitle));
        OnPropertyChanged(nameof(RunningGameFooterActionText));

        if (!_runningGameService.ConsumePlaytimeUpdate())
        {
            return;
        }

        foreach (var card in Games)
        {
            card.Refresh();
        }

        _ = PersistLibraryAsync();
    }
}
