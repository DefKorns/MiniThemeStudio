using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IThemeMetadataReader _metadataReader;
    private readonly ITextureAtlasReader _textureAtlasReader;
    private readonly IThemeDraftService _themeDraftService;
    private readonly ITextureAtlasEditor _textureAtlasEditor;
    private readonly IThemeAssetManager _assetManager;
    private readonly IThemeValidator _themeValidator;
    private readonly IBaseFirmwareManager _baseFirmwareManager;
    private readonly IThemeExporter _themeExporter;
    private readonly ICharacterAnimationEditor _characterEditor;
    private readonly IThemePaletteEditor _paletteEditor;
    private readonly ISavedDraftStore _savedDraftStore;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IFFmpegAudioConverter _audioConverter;
    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileDialogs;
    private readonly IAppDialogs _appDialogs;
    private readonly IAppearanceService _appearance;
    private readonly IErrorHandler _errorHandler;

    private readonly ThemeSession _session;
    private FirmwareDefinition? _currentBase;

    public MainWindowViewModel(
        IThemeMetadataReader metadataReader,
        ITextureAtlasReader textureAtlasReader,
        IThemeDraftService themeDraftService,
        ITextureAtlasEditor textureAtlasEditor,
        IThemeAssetManager assetManager,
        IThemeValidator themeValidator,
        IBaseFirmwareManager baseFirmwareManager,
        IThemeExporter themeExporter,
        ICharacterAnimationEditor characterEditor,
        ISavedDraftStore savedDraftStore,
        IThemePaletteEditor paletteEditor,
        IAppSettingsStore settingsStore,
        IFFmpegAudioConverter audioConverter,
        IDialogService dialogService,
        IFileDialogService fileDialogs,
        IAppDialogs appDialogs,
        IAppearanceService appearance,
        IErrorHandler errorHandler,
        ThemeSession session,
        ScenesViewModel scenes)
    {
        _metadataReader = metadataReader;
        _textureAtlasReader = textureAtlasReader;
        _themeDraftService = themeDraftService;
        _textureAtlasEditor = textureAtlasEditor;
        _assetManager = assetManager;
        _themeValidator = themeValidator;
        _baseFirmwareManager = baseFirmwareManager;
        _themeExporter = themeExporter;
        _characterEditor = characterEditor;
        _savedDraftStore = savedDraftStore;
        _paletteEditor = paletteEditor;
        _settingsStore = settingsStore;
        _audioConverter = audioConverter;
        _dialogService = dialogService;
        _fileDialogs = fileDialogs;
        _appDialogs = appDialogs;
        _appearance = appearance;
        _errorHandler = errorHandler;
        _session = session;
        Scenes = scenes;
        Scenes.SpriteRequested += (_, request) => ShowSpriteInAtlas(request);

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
        _session.ContentChanged += (_, _) =>
        {
            ReloadAssets();
            RunValidation();
        };
    }

    public ScenesViewModel Scenes { get; }

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

    public string Status
    {
        get => _session.Status;
        set => _session.Status = value;
    }

    [ObservableProperty]
    private string currentBaseLabel = "No Base";

    [ObservableProperty]
    private string atlasSummary = "No packed atlas";

    [ObservableProperty]
    private string atlasImageStatus = "Not available";

    [ObservableProperty]
    private string atlasImagePath = "";

    [ObservableProperty]
    private IReadOnlyList<TextureAtlas> atlases = [];

    [ObservableProperty]
    private TextureAtlas? selectedAtlas;

    [ObservableProperty]
    private IReadOnlyList<SpriteRegion> sprites = [];

    [ObservableProperty]
    private IReadOnlyList<SpriteRegion> filteredSprites = [];

    [ObservableProperty]
    private SpriteRegion? selectedSprite;

    [ObservableProperty]
    private string spriteSearchText = "";

    [ObservableProperty]
    private int spriteZoom = 1; // 1, 2, 4, 0 (fit)

    [ObservableProperty]
    private bool canReplaceSprite;

    // Wallpapers
    [ObservableProperty]
    private IReadOnlyList<WallpaperAsset> wallpapers = [];

    [ObservableProperty]
    private WallpaperAsset? selectedWallpaper;

    [ObservableProperty]
    private bool canReplaceWallpaper;

    // images of this theme that no scene draws
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnusedImages))]
    private IReadOnlyList<ExtraAsset> unusedImages = [];

    public bool HasUnusedImages => UnusedImages.Count > 0;

    [ObservableProperty]
    private ExtraAsset? selectedUnusedImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingTextures))]
    private IReadOnlyList<MissingTexture> missingTextures = [];

    public bool HasMissingTextures => MissingTextures.Count > 0;

    [ObservableProperty]
    private MissingTexture? selectedMissingTexture;

    // Validation
    [ObservableProperty]
    private IReadOnlyList<ThemeValidationIssue> validationIssues = [];

    [ObservableProperty]
    private string validationSummary = "Theme not validated yet.";

    [ObservableProperty]
    private bool isThemeValid = true;

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

    private void MarkChanged()
    {
        _session.MarkChanged();
    }

    public string? CurrentDraftPath => _session.Draft?.DraftPath;
    public string? CurrentBaseUiPath => _session.BaseUiPath;

    public void OpenTheme(string rootPath, string? baseUiPath = null)
    {
        _session.BaseUiPath = baseUiPath ?? _baseFirmwareManager.FindCompatibleBaseUiPath(rootPath);
        OpenDraft(() => _themeDraftService.OpenExisting(rootPath), false);
    }

    public void ChangeBase(string baseUiPath)
    {
        if (_session.Draft is null)
        {
            return;
        }

        var hadUnsavedChanges = HasUnsavedChanges;
        _session.BaseUiPath = baseUiPath;
        LoadDraft(_session.Draft);
        HasUnsavedChanges = hadUnsavedChanges;
        Status = $"Using {CurrentBaseLabel} - {Sprites.Count} sprites, {Wallpapers.Count} wallpapers.";
    }

    private static bool PathsEqual(string first, string? second)
    {
        return second is not null && string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);
    }

    public void CreateTheme(NewThemeRequest request)
    {
        _session.BaseUiPath = request.BaseThemePath;
        OpenDraft(() => _themeDraftService.CreateNew(request), true);
    }

    public void SaveTheme()
    {
        if (_session.Draft is null || !HasUnsavedChanges)
        {
            return;
        }

        ValidateBeforeOutput("saving");
        _session.Draft = _themeDraftService.Save(_session.Draft);
        HasUnsavedChanges = false;
        Status = _session.Draft.LastBackupPath is null
            ? $"Saved to {_session.Draft.SavePath}"
            : $"Saved to {_session.Draft.SavePath} (previous version backed up to {_session.Draft.LastBackupPath})";
        ForgetSavedDraftAfterSave();
    }

    public bool ReplaceSelectedSprite(string replacementImagePath)
    {
        if (SelectedSprite is not { } sprite || SelectedAtlas is not { } atlas || _session.Draft is null)
        {
            Status = "Select a packed sprite first.";
            return false;
        }

        try
        {
            var draftImagePath = _assetManager.EnsureDraftCopy(
                _session.Draft.DraftPath,
                atlas.RelativeImagePath,
                atlas.ImagePath);
            if (draftImagePath != atlas.ImagePath)
            {
                var draftAtlas = atlas with { ImagePath = draftImagePath };
                Atlases = Atlases.Select(candidate => candidate == atlas ? draftAtlas : candidate).ToList();
                SelectedAtlas = draftAtlas;
                SelectedSprite = sprite;
            }

            _textureAtlasEditor.ReplaceSprite(new SpriteReplacementRequest(
                draftImagePath,
                sprite,
                replacementImagePath));
            MarkChanged();
            Status = $"Replaced {sprite.Name} in draft.";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            Status = exception.Message;
            return false;
        }
    }

    public bool ExportSelectedSprite(string destinationPath)
    {
        if (SelectedSprite is null || AtlasImagePath.Length == 0)
        {
            Status = "Select a packed sprite first.";
            return false;
        }

        try
        {
            _assetManager.ExportPackedSprite(AtlasImagePath, SelectedSprite, destinationPath);
            Status = $"Exported {SelectedSprite.Name} to {Path.GetFileName(destinationPath)}.";
            return true;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            return false;
        }
    }

    public bool ReplaceSelectedWallpaper(string replacementImagePath)
    {
        if (SelectedWallpaper is null || _session.Draft is null)
        {
            Status = "Select a wallpaper first.";
            return false;
        }

        try
        {
            var targetPng = Path.Combine(
                _session.Draft.DraftPath,
                "resources",
                "layout",
                "asset",
                "sprite",
                "wallpaper",
                $"{SelectedWallpaper.Name}.png");

            _assetManager.ReplaceWallpaper(targetPng, replacementImagePath, SelectedWallpaper.JsonPath);
            ReloadAssets();
            MarkChanged();
            Status = $"Replaced wallpaper {SelectedWallpaper.DisplayName}.";
            return true;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            return false;
        }
    }

    public bool ExportSelectedWallpaper(string destinationPath)
    {
        if (SelectedWallpaper is null)
        {
            return false;
        }

        try
        {
            _assetManager.ExportWallpaper(SelectedWallpaper, destinationPath);
            Status = $"Exported wallpaper {SelectedWallpaper.DisplayName}.";
            return true;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            return false;
        }
    }

    public bool DeleteSelectedUnusedImage()
    {
        if (SelectedUnusedImage is not { IsFromBaseUi: false } image)
        {
            return false;
        }

        return DeleteDraftImage(image, image.OverridesBase
            ? $"Removed the replacement of {image.DisplayName}; the Base image is used again."
            : $"Deleted {image.DisplayName}.");
    }

    private bool DeleteDraftImage(ExtraAsset image, string status)
    {
        if (_session.Draft is null)
        {
            return false;
        }

        try
        {
            var targetPng = Path.Combine(_session.Draft.DraftPath, "resources", image.RelativePath);
            _assetManager.DeleteExtraAsset(targetPng);
            ReloadAssets();
            RunValidation();
            MarkChanged();
            Status = status;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = exception.Message;
            return false;
        }
    }

    public string? PackagingSystem => _currentBase is null ? null : ConsoleFamilies.PackagingSystem(ConsoleFamilies.Of(_currentBase));

    public string PackagingName => _session.Draft is null
        ? ""
        : ThemeDirectoryName.FromDisplayName(Path.GetFileName(Path.TrimEndingDirectorySeparator(_session.Draft.SavePath)));

    public void ExportPackage(string outputDirectory)
    {
        if (_session.Draft is null)
        {
            return;
        }

        var system = PackagingSystem
            ?? throw new OperationFailedException("Choose the Base of this theme first (click the console badge in the header).");
        ValidateBeforeOutput("exporting");

        var previewPath = $"{Path.TrimEndingDirectorySeparator(_session.Draft.SavePath)}.png";
        var exportOptions = new ThemeExportOptions(
            ConvertBgmAudio: ConvertBgmAudio,
            FFmpegPath: _settingsStore.Current.FFmpegPath,
            BgmChannels: BgmChannels,
            IncludeBgm: IncludeBgm);
        var result = _themeExporter.ExportPackage(_session.Draft.DraftPath, outputDirectory, system, PackagingName, previewPath, _session.BaseUiPath, exportOptions);
        var summary = $"Exported {result.FileCount} changed files to {result.PackagePath} ({result.UnchangedFilesSkipped} unchanged Base files left out)";
        Status = result.IncludesPreview
            ? $"{summary}."
            : $"{summary}, without a preview (no {Path.GetFileName(previewPath)} next to the theme folder).";
    }

    // throws when the theme has validation errors, so nothing broken is saved or exported
    private void ValidateBeforeOutput(string action)
    {
        var validationOptions = new ThemeValidationOptions(
            ConvertBgmAudio: ConvertBgmAudio,
            FFmpegPath: _settingsStore.Current.FFmpegPath,
            BgmChannels: BgmChannels,
            IncludeBgm: IncludeBgm);
        var validation = _themeValidator.Validate(_session.Draft!.DraftPath, _session.BaseUiPath, validationOptions);
        ValidationIssues = validation.Issues;
        IsThemeValid = validation.IsValid;
        if (!validation.HasErrors)
        {
            return;
        }

        var errors = validation.Issues.Where(issue => issue.IsError).ToList();
        throw new OperationFailedException($"Fix {errors.Count} validation error(s) before {action}: {errors[0].Message}");
    }

    [RelayCommand]
    public void RunValidation()
    {
        if (_session.Draft is null)
        {
            return;
        }

        var validationOptions = new ThemeValidationOptions(
            ConvertBgmAudio: ConvertBgmAudio,
            FFmpegPath: _settingsStore.Current.FFmpegPath,
            BgmChannels: BgmChannels,
            IncludeBgm: IncludeBgm);
        var result = _themeValidator.Validate(_session.Draft.DraftPath, _session.BaseUiPath, validationOptions);
        ValidationIssues = result.Issues;
        IsThemeValid = result.IsValid;
        ValidationSummary = result.IsValid
            ? (result.HasWarnings ? "Validation passed with notices." : "Theme is fully valid!")
            : "Validation found errors that must be fixed.";
        Status = ValidationSummary;
    }

    public void ShowHome()
    {
        DiscardTheme();
        Atlases = [];
        SelectedAtlas = null;
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
        var metadata = _metadataReader.Read(draft.DraftPath, Path.GetFileName(draft.SavePath));
        ThemeName = metadata.Name;
        Author = metadata.Author ?? "Not specified";
        ThemePath = draft.SavePath;
        OnPropertyChanged(nameof(CurrentDraftPath));
        RequiresOverlay = metadata.RequiresOverlay;

        _currentBase = _baseFirmwareManager.GetInstalledBases()
            .FirstOrDefault(info => info.Status == BaseInstallStatus.Valid && PathsEqual(info.LocalPath, _session.BaseUiPath))?
            .Definition;
        CurrentBaseLabel = _currentBase is null ? "No Base" : ConsoleFamilies.ShortLabel(_currentBase);
        Atlases = _textureAtlasReader.ReadAll(draft.DraftPath, _session.BaseUiPath);
        SelectedAtlas = Atlases.Count > 0 ? Atlases[0] : null;
        if (SelectedAtlas is null)
        {
            OnSelectedAtlasChanged(null);
        }

        ReloadAssets();
        RunValidation();
        ShowWorkspace();
        Status = $"Draft opened - {Sprites.Count} sprites, {Wallpapers.Count} wallpapers.";
    }

    private void ReloadAssets()
    {
        if (_session.Draft is null)
        {
            Wallpapers = [];
            UnusedImages = [];
            MissingTextures = [];
            Fonts = [];
            ReloadCharacters();
            ReloadBgmTracks();
            SelectedWallpaper = null;
            SelectedFont = null;
            _session.Reload();
            return;
        }

        _session.Reload();
        var report = _session.Report;
        var selectedWallpaperName = SelectedWallpaper?.Name;

        Wallpapers = _assetManager.GetWallpapers(_session.Draft.DraftPath, _session.BaseUiPath)
            .Select(wallpaper => wallpaper with
            {
                UsedBy = report.UsageOf(report.TextureIdOf($"layout/asset/sprite/wallpaper/{wallpaper.Name}.png", wallpaper.TextureId)),
            })
            .ToList();
        SelectedWallpaper = Wallpapers.FirstOrDefault(wallpaper => wallpaper.Name == selectedWallpaperName)
            ?? (Wallpapers.Count > 0 ? Wallpapers[0] : null);

        var images = _assetManager.GetExtraAssets(_session.Draft.DraftPath, _session.BaseUiPath)
            .Select(asset => report.TextureIdOf(asset.RelativePath, asset.TextureId) is var textureId
                ? asset with { TextureId = textureId, UsedBy = report.UsageOf(textureId) }
                : asset)
            .ToList();

        // a Base image nobody draws is not part of the theme, so only the theme's own files are reported
        var selectedUnusedPath = SelectedUnusedImage?.RelativePath;
        UnusedImages = images.Where(asset => asset.UsedBy.Count == 0 && !asset.IsFromBaseUi).ToList();
        SelectedUnusedImage = UnusedImages.FirstOrDefault(asset => asset.RelativePath == selectedUnusedPath)
            ?? (UnusedImages.Count > 0 ? UnusedImages[0] : null);

        MissingTextures = report.MissingTextures;
        SelectedMissingTexture = MissingTextures.Count > 0 ? MissingTextures[0] : null;
        ReloadCharacters();
        ReloadFonts();
        ReloadPalettes();
        ReloadBgmTracks();
    }

    public bool ProvideSelectedMissingTexture(string sourceImagePath)
    {
        if (SelectedMissingTexture is not { } texture || _session.Draft is null)
        {
            Status = "Select a missing texture first.";
            return false;
        }

        try
        {
            _assetManager.ProvideMissingTexture(_session.Draft.DraftPath, texture, sourceImagePath);
            ReloadAssets();
            RunValidation();
            MarkChanged();
            Status = $"Added {Path.GetFileName(texture.ImageRelativePath)} for texture {texture.TextureId}.";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Status = exception.Message;
            return false;
        }
    }

    private void ShowSpriteInAtlas(SceneSpriteRequest request)
    {
        var atlas = Atlases.FirstOrDefault(candidate => candidate.TextureId == request.TextureId)
            ?? Atlases.FirstOrDefault(candidate => request.ImagePath is not null && PathsEqual(candidate.ImagePath, request.ImagePath));
        if (atlas?.Sprites.FirstOrDefault(sprite => sprite.Name == request.SpriteName) is not { } sprite)
        {
            Status = $"Sprite {request.SpriteName} was not found in the atlases.";
            return;
        }

        SelectedAtlas = atlas;
        SpriteSearchText = "";
        SelectedSprite = sprite;
        SelectedTabIndex = 0;
    }

    private void ApplySpriteFilter()
    {
        if (string.IsNullOrWhiteSpace(SpriteSearchText))
        {
            FilteredSprites = Sprites;
        }
        else
        {
            FilteredSprites = Sprites
                .Where(s => s.Name.Contains(SpriteSearchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void ShowWorkspace()
    {
        IsHomeVisible = false;
        IsThemeOpen = true;
    }

    partial void OnSpriteSearchTextChanged(string value)
    {
        ApplySpriteFilter();
    }

    partial void OnSelectedAtlasChanged(TextureAtlas? value)
    {
        if (value is null)
        {
            AtlasSummary = "No sprite atlas";
            AtlasImageStatus = "Not available";
            AtlasImagePath = "";
            Sprites = [];
            FilteredSprites = [];
            SelectedSprite = null;
            return;
        }

        var previousName = SelectedSprite?.Name;
        AtlasSummary = $"{value.Size.Width} x {value.Size.Height}";
        AtlasImageStatus = File.Exists(value.ImagePath) ? "Image found" : "Image missing";
        AtlasImagePath = value.ImagePath;
        Sprites = value.Sprites;
        ApplySpriteFilter();
        SelectedSprite = value.Sprites.FirstOrDefault(sprite => sprite.Name == previousName)
            ?? (value.Sprites.Count > 0 ? value.Sprites[0] : null);
    }

    partial void OnSelectedSpriteChanged(SpriteRegion? value)
    {
        CanReplaceSprite = value is not null && AtlasImagePath.Length > 0;
    }

    partial void OnAtlasImagePathChanged(string value)
    {
        CanReplaceSprite = SelectedSprite is not null && value.Length > 0;
    }

    partial void OnSelectedWallpaperChanged(WallpaperAsset? value)
    {
        CanReplaceWallpaper = value is not null;
    }
}
