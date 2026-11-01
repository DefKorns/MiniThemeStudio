using System;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

// The window: the theme being edited and its metadata, the workspace tabs, and opening, saving and exporting
// (the commands are in MainWindowViewModel.Workflow.cs, saved drafts in MainWindowViewModel.Drafts.cs).
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IThemeMetadataReader _metadataReader;
    private readonly IThemeDraftService _themeDraftService;
    private readonly IBaseFirmwareManager _baseFirmwareManager;
    private readonly IThemeExporter _themeExporter;
    private readonly ISavedDraftStore _savedDraftStore;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileDialogs;
    private readonly IAppDialogs _appDialogs;
    private readonly IAppearanceService _appearance;
    private readonly IErrorHandler _errorHandler;
    private readonly ThemeSession _session;
    private FirmwareDefinition? _currentBase;

    public MainWindowViewModel(
        IThemeMetadataReader metadataReader,
        IThemeDraftService themeDraftService,
        IBaseFirmwareManager baseFirmwareManager,
        IThemeExporter themeExporter,
        ISavedDraftStore savedDraftStore,
        IAppSettingsStore settingsStore,
        IDialogService dialogService,
        IFileDialogService fileDialogs,
        IAppDialogs appDialogs,
        IAppearanceService appearance,
        IErrorHandler errorHandler,
        ThemeSession session,
        ScenesViewModel scenesTab,
        SpritesViewModel spritesTab,
        WallpapersViewModel wallpapersTab,
        CharactersViewModel charactersTab,
        FontsViewModel fontsTab,
        ColorsViewModel colorsTab,
        AudioViewModel audioTab,
        ValidationViewModel validationTab,
        EditingMode mode)
    {
        Mode = mode;

        // the tabs of advanced editing go away with it, so the window leaves them for the first tab
        Mode.PropertyChanged += (_, _) =>
        {
            if (Mode.IsSimple && SelectedTabIndex is ScenesTabIndex or ColorsTabIndex)
            {
                SelectedTabIndex = 0;
            }
        };
        _metadataReader = metadataReader;
        _themeDraftService = themeDraftService;
        _baseFirmwareManager = baseFirmwareManager;
        _themeExporter = themeExporter;
        _savedDraftStore = savedDraftStore;
        _settingsStore = settingsStore;
        _dialogService = dialogService;
        _fileDialogs = fileDialogs;
        _appDialogs = appDialogs;
        _appearance = appearance;
        _errorHandler = errorHandler;
        _session = session;
        ScenesTab = scenesTab;
        SpritesTab = spritesTab;
        WallpapersTab = wallpapersTab;
        CharactersTab = charactersTab;
        FontsTab = fontsTab;
        ColorsTab = colorsTab;
        AudioTab = audioTab;
        ValidationTab = validationTab;

        ScenesTab.SpriteRequested += (_, request) =>
        {
            if (SpritesTab.ShowSprite(request))
            {
                SelectedTabIndex = 0;
            }
        };

        // the session holds the state the window binds to; its changes are raised here too
        _session.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName is nameof(ThemeSession.HasUnsavedChanges) or nameof(ThemeSession.IsDraftSaved))
            {
                OnPropertyChanged(nameof(ShowUnsavedChangesBadge));
                OnPropertyChanged(nameof(ShowDraftSavedBadge));
                OnPropertyChanged(nameof(HasUnsavedWork));
            }
        };

        // a tab wrote to the draft: every tab reloads and the theme is validated again
        _session.ContentChanged += (_, _) =>
        {
            _session.Reload();
            ValidationTab.Run();
        };
    }

    // the order of the tabs in the window
    private const int ScenesTabIndex = 2;
    private const int ColorsTabIndex = 5;

    // simple or advanced editing: which tabs, and which parts of them, show
    public EditingMode Mode { get; }

    public ScenesViewModel ScenesTab { get; }

    public SpritesViewModel SpritesTab { get; }

    public WallpapersViewModel WallpapersTab { get; }

    public CharactersViewModel CharactersTab { get; }

    public FontsViewModel FontsTab { get; }

    public ColorsViewModel ColorsTab { get; }

    public AudioViewModel AudioTab { get; }

    public ValidationViewModel ValidationTab { get; }

    // the workspace tab shown; 0 is Sprites
    [ObservableProperty]
    private int selectedTabIndex;

    [ObservableProperty]
    private bool isHomeVisible = true;

    [ObservableProperty]
    private bool isThemeOpen;

    [ObservableProperty]
    private string themeName = "No theme open";

    [ObservableProperty]
    private string author = "Not specified";

    [ObservableProperty]
    private string themePath = "";

    [ObservableProperty]
    private bool requiresOverlay;

    [ObservableProperty]
    private string currentBaseLabel = "No Base";

    public string Status
    {
        get => _session.Status;
        set => _session.Status = value;
    }

    public bool HasUnsavedChanges
    {
        get => _session.HasUnsavedChanges;
        set => _session.HasUnsavedChanges = value;
    }

    // the changes not yet saved to the theme are kept in a saved draft
    public bool IsDraftSaved
    {
        get => _session.IsDraftSaved;
        set => _session.IsDraftSaved = value;
    }

    public bool ShowUnsavedChangesBadge => HasUnsavedChanges && !IsDraftSaved;

    public bool ShowDraftSavedBadge => HasUnsavedChanges && IsDraftSaved;

    // changes that would be lost on closing: not in the theme and not in a saved draft
    public bool HasUnsavedWork => HasUnsavedChanges && !IsDraftSaved;

    public string? CurrentDraftPath => _session.Draft?.DraftPath;

    public string? CurrentBaseUiPath => _session.BaseUiPath;

    public string? PackagingSystem => _currentBase is null ? null : ConsoleFamilies.PackagingSystem(ConsoleFamilies.Of(_currentBase));

    public string PackagingName => _session.Draft is null
        ? ""
        : ThemeDirectoryName.FromDisplayName(Path.GetFileName(Path.TrimEndingDirectorySeparator(_session.Draft.SavePath)));

    public void OpenTheme(string rootPath, string? baseUiPath = null)
    {
        _session.BaseUiPath = baseUiPath ?? _baseFirmwareManager.FindCompatibleBaseUiPath(rootPath);
        OpenDraft(() => _themeDraftService.OpenExisting(rootPath), false);
    }

    public void CreateTheme(NewThemeRequest request)
    {
        _session.BaseUiPath = request.BaseThemePath;
        OpenDraft(() => _themeDraftService.CreateNew(request), true);
    }

    public void ChangeBase(string baseUiPath)
    {
        if (_session.Draft is null)
        {
            return;
        }

        bool hadUnsavedChanges = HasUnsavedChanges;
        _session.BaseUiPath = baseUiPath;
        LoadDraft(_session.Draft);
        HasUnsavedChanges = hadUnsavedChanges;
        Status = $"Using {CurrentBaseLabel} - {SpritesTab.Sprites.Count} sprites, {WallpapersTab.Wallpapers.Count} wallpapers.";
    }

    public void SaveTheme()
    {
        if (_session.Draft is null || !HasUnsavedChanges)
        {
            return;
        }

        ValidationTab.EnsureValidFor("saving");
        _session.Draft = _themeDraftService.Save(_session.Draft);
        HasUnsavedChanges = false;
        Status = _session.Draft.LastBackupPath is null
            ? $"Saved to {_session.Draft.SavePath}"
            : $"Saved to {_session.Draft.SavePath} (previous version backed up to {_session.Draft.LastBackupPath})";
        ForgetSavedDraftAfterSave();
    }

    public void ExportPackage(string outputDirectory)
    {
        if (_session.Draft is null)
        {
            return;
        }

        string system = PackagingSystem
            ?? throw new OperationFailedException("Choose the Base of this theme first (click the console badge in the header).");
        ValidationTab.EnsureValidFor("exporting");

        string previewPath = $"{Path.TrimEndingDirectorySeparator(_session.Draft.SavePath)}.png";
        ThemePackageResult result = _themeExporter.ExportPackage(_session.Draft.DraftPath, outputDirectory, system, PackagingName, previewPath, _session.BaseUiPath, AudioTab.ExportOptions);
        string summary = $"Exported {result.FileCount} changed files to {result.PackagePath} ({result.UnchangedFilesSkipped} unchanged Base files left out)";
        Status = result.IncludesPreview
            ? $"{summary}."
            : $"{summary}, without a preview (no {Path.GetFileName(previewPath)} next to the theme folder).";
    }

    [RelayCommand]
    private void RunValidation()
    {
        ValidationTab.Run();
    }

    public void ShowHome()
    {
        DiscardTheme();
        _session.Reload();
        IsThemeOpen = false;
        IsHomeVisible = true;
        Status = "Draft discarded";
    }

    public void DiscardTheme()
    {
        if (_session.Draft is not null)
        {
            _themeDraftService.Discard(_session.Draft);
            _session.Draft = null;
        }

        OnPropertyChanged(nameof(CurrentDraftPath));
        HasUnsavedChanges = false;
        IsDraftSaved = false;
    }

    private void OpenDraft(Func<ThemeDraft> createDraft, bool isNewTheme)
    {
        DiscardTheme();

        try
        {
            _session.Draft = createDraft();
            LoadDraft(_session.Draft);
            HasUnsavedChanges = isNewTheme;
        }
        catch
        {
            // a draft that failed to load is not left half open
            DiscardTheme();
            throw;
        }
    }

    private void LoadDraft(ThemeDraft draft)
    {
        ThemeMetadata metadata = _metadataReader.Read(draft.DraftPath, Path.GetFileName(draft.SavePath));
        ThemeName = metadata.Name;
        Author = metadata.Author ?? "Not specified";
        ThemePath = draft.SavePath;
        OnPropertyChanged(nameof(CurrentDraftPath));
        RequiresOverlay = metadata.RequiresOverlay;

        _currentBase = _baseFirmwareManager.GetInstalledBases()
            .FirstOrDefault(info => info.Status == BaseInstallStatus.Valid && PathsEqual(info.LocalPath, _session.BaseUiPath))?
            .Definition;
        _session.BaseDefinition = _currentBase;
        CurrentBaseLabel = _currentBase is null ? "No Base" : ConsoleFamilies.ShortLabel(_currentBase);
        _session.Reload();
        ValidationTab.Run();
        IsHomeVisible = false;
        IsThemeOpen = true;
        Status = $"Draft opened - {SpritesTab.Sprites.Count} sprites, {WallpapersTab.Wallpapers.Count} wallpapers.";
    }

    private static bool PathsEqual(string first, string? second)
    {
        return second is not null && string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);
    }
}
