using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Firmware;
using MiniThemeStudio.Infrastructure.Themes;

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
    private readonly IThemeResourceIndexer _resourceIndexer;
    private readonly IThemeExporter _themeExporter;
    private readonly ICharacterAnimationEditor _characterEditor;
    private readonly ISavedDraftStore _savedDraftStore;

    private ThemeDraft? _draft;
    private string? _currentBaseUiPath;
    private FirmwareDefinition? _currentBase;

    public MainWindowViewModel(
        IThemeMetadataReader metadataReader,
        ITextureAtlasReader textureAtlasReader,
        IThemeDraftService themeDraftService,
        ITextureAtlasEditor textureAtlasEditor,
        IThemeAssetManager? assetManager = null,
        IThemeValidator? themeValidator = null,
        IBaseFirmwareManager? baseFirmwareManager = null,
        IThemeResourceIndexer? resourceIndexer = null,
        IThemeExporter? themeExporter = null,
        ICharacterAnimationEditor? characterEditor = null,
        ISavedDraftStore? savedDraftStore = null)
    {
        _metadataReader = metadataReader;
        _textureAtlasReader = textureAtlasReader;
        _themeDraftService = themeDraftService;
        _textureAtlasEditor = textureAtlasEditor;
        _assetManager = assetManager ?? new ThemeAssetManager();
        _themeValidator = themeValidator ?? new ThemeValidator();
        _baseFirmwareManager = baseFirmwareManager ?? new BaseFirmwareManager(FirmwareRegistry.CreateDefault());
        _resourceIndexer = resourceIndexer ?? new ThemeResourceIndexer();
        _themeExporter = themeExporter ?? new ThemeExporter();
        _characterEditor = characterEditor ?? new CharacterAnimationEditor();
        _savedDraftStore = savedDraftStore ?? new SavedDraftStore();
    }

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
    private string status = "Ready";

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

    // Extra assets (Characters, poles, standalone elements)
    [ObservableProperty]
    private IReadOnlyList<ExtraAsset> extraAssets = [];

    [ObservableProperty]
    private ExtraAsset? selectedExtraAsset;

    [ObservableProperty]
    private bool canReplaceExtraAsset;

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

    [ObservableProperty]
    private bool hasUnsavedChanges;

    public string? CurrentDraftPath => _draft?.DraftPath;
    public string? CurrentBaseUiPath => _currentBaseUiPath;

    public bool OpenTheme(string rootPath, string? baseUiPath = null)
    {
        _currentBaseUiPath = baseUiPath ?? _baseFirmwareManager.FindCompatibleBaseUiPath(rootPath);
        return OpenDraft(() => _themeDraftService.OpenExisting(rootPath), false);
    }

    public bool ChangeBase(string baseUiPath)
    {
        if (_draft is null)
        {
            return false;
        }

        var hadUnsavedChanges = HasUnsavedChanges;
        _currentBaseUiPath = baseUiPath;
        LoadDraft(_draft);
        HasUnsavedChanges = hadUnsavedChanges;
        Status = $"Using {CurrentBaseLabel} - {Sprites.Count} sprites, {Wallpapers.Count} wallpapers.";
        return true;
    }

    private static bool PathsEqual(string first, string? second)
    {
        return second is not null && string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);
    }

    public bool CreateTheme(NewThemeRequest request)
    {
        _currentBaseUiPath = request.BaseThemePath;
        return OpenDraft(() => _themeDraftService.CreateNew(request), true);
    }

    public bool SaveTheme()
    {
        if (_draft is null || !HasUnsavedChanges)
        {
            return false;
        }

        if (!ValidateBeforeOutput("saving"))
        {
            return false;
        }

        try
        {
            _draft = _themeDraftService.Save(_draft);
            HasUnsavedChanges = false;
            Status = _draft.LastBackupPath is null
                ? $"Saved to {_draft.SavePath}"
                : $"Saved to {_draft.SavePath} (previous version backed up to {_draft.LastBackupPath})";
            ForgetSavedDraftAfterSave();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException)
        {
            Status = exception.Message;
            return false;
        }
    }

    public bool ReplaceSelectedSprite(string replacementImagePath)
    {
        if (SelectedSprite is not { } sprite || SelectedAtlas is not { } atlas || _draft is null)
        {
            Status = "Select a packed sprite first.";
            return false;
        }

        try
        {
            var draftImagePath = _assetManager.EnsureDraftCopy(
                _draft.DraftPath,
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
            HasUnsavedChanges = true;
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
        if (SelectedWallpaper is null || _draft is null)
        {
            Status = "Select a wallpaper first.";
            return false;
        }

        try
        {
            var targetPng = Path.Combine(
                _draft.DraftPath,
                "resources",
                "layout",
                "asset",
                "sprite",
                "wallpaper",
                $"{SelectedWallpaper.Name}.png");

            _assetManager.ReplaceWallpaper(targetPng, replacementImagePath, SelectedWallpaper.JsonPath);
            ReloadAssets();
            HasUnsavedChanges = true;
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

    public bool AddExtraAsset(string relativeDestPath, string sourceImagePath)
    {
        if (_draft is null)
        {
            return false;
        }

        try
        {
            _assetManager.AddExtraAsset(_draft.DraftPath, relativeDestPath, sourceImagePath);
            ReloadAssets();
            HasUnsavedChanges = true;
            Status = $"Added {Path.GetFileName(relativeDestPath)}. No scene uses it yet, so it will not appear on the console.";
            return true;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            return false;
        }
    }

    public bool ReplaceSelectedExtraAsset(string replacementImagePath)
    {
        if (SelectedExtraAsset is null || _draft is null)
        {
            return false;
        }

        try
        {
            var targetPng = Path.Combine(_draft.DraftPath, "resources", SelectedExtraAsset.RelativePath);
            _assetManager.ReplaceExtraAsset(targetPng, replacementImagePath, SelectedExtraAsset.JsonPath);
            ReloadAssets();
            HasUnsavedChanges = true;
            Status = $"Replaced element {SelectedExtraAsset.DisplayName}.";
            return true;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            return false;
        }
    }

    public bool DeleteSelectedExtraAsset()
    {
        if (SelectedExtraAsset is null || _draft is null || SelectedExtraAsset.IsFromBaseUi)
        {
            return false;
        }

        try
        {
            var targetPng = Path.Combine(_draft.DraftPath, "resources", SelectedExtraAsset.RelativePath);
            _assetManager.DeleteExtraAsset(targetPng);
            ReloadAssets();
            HasUnsavedChanges = true;
            Status = $"Deleted element {SelectedExtraAsset.DisplayName}.";
            return true;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            return false;
        }
    }

    public bool ExportSelectedExtraAsset(string destinationPath)
    {
        if (SelectedExtraAsset is null)
        {
            return false;
        }

        try
        {
            _assetManager.ExportExtraAsset(SelectedExtraAsset, destinationPath);
            Status = $"Exported element {SelectedExtraAsset.DisplayName}.";
            return true;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            return false;
        }
    }

    public string? PackagingSystem => _currentBase is null ? null : ConsoleFamilies.PackagingSystem(ConsoleFamilies.Of(_currentBase));

    public string PackagingName => _draft is null
        ? ""
        : ThemeDirectoryName.FromDisplayName(Path.GetFileName(Path.TrimEndingDirectorySeparator(_draft.SavePath)));

    public bool ExportPackage(string outputDirectory)
    {
        if (!CanPackage(out var system) || !ValidateBeforeOutput("exporting"))
        {
            return false;
        }

        try
        {
            var previewPath = $"{Path.TrimEndingDirectorySeparator(_draft!.SavePath)}.png";
            var result = _themeExporter.ExportPackage(_draft.DraftPath, outputDirectory, system, PackagingName, previewPath, _currentBaseUiPath);
            var summary = $"Exported {result.FileCount} changed files to {result.PackagePath} ({result.UnchangedFilesSkipped} unchanged Base files left out)";
            Status = result.IncludesPreview
                ? $"{summary}."
                : $"{summary}, without a preview (no {Path.GetFileName(previewPath)} next to the theme folder).";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            Status = exception.Message;
            return false;
        }
    }

    private bool CanPackage(out string system)
    {
        system = PackagingSystem ?? "";
        if (_draft is null)
        {
            return false;
        }

        if (system.Length == 0)
        {
            Status = "Choose the Base of this theme first (click the console badge in the header).";
            return false;
        }

        return true;
    }

    private bool ValidateBeforeOutput(string action)
    {
        var validation = _themeValidator.Validate(_draft!.DraftPath, _currentBaseUiPath);
        ValidationIssues = validation.Issues;
        IsThemeValid = validation.IsValid;
        if (!validation.HasErrors)
        {
            return true;
        }

        var errors = validation.Issues.Where(issue => issue.IsError).ToList();
        Status = $"Fix {errors.Count} validation error(s) before {action}: {errors[0].Message}";
        return false;
    }

    public void RunValidation()
    {
        if (_draft is null)
        {
            return;
        }

        var result = _themeValidator.Validate(_draft.DraftPath, _currentBaseUiPath);
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
        IsThemeOpen = false;
        IsHomeVisible = true;
        Status = "Draft discarded";
    }

    public void DiscardTheme()
    {
        if (_draft is not null)
        {
            _themeDraftService.Discard(_draft);
            _draft = null;
        }

        HasUnsavedChanges = false;
    }

    private bool OpenDraft(Func<ThemeDraft> createDraft, bool isNewTheme)
    {
        DiscardTheme();

        try
        {
            _draft = createDraft();
            LoadDraft(_draft);
            HasUnsavedChanges = isNewTheme;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException)
        {
            DiscardTheme();
            Status = exception.Message;
            return false;
        }
    }

    private void LoadDraft(ThemeDraft draft)
    {
        var metadata = _metadataReader.Read(draft.DraftPath, Path.GetFileName(draft.SavePath));
        ThemeName = metadata.Name;
        Author = metadata.Author ?? "Not specified";
        ThemePath = draft.SavePath;
        RequiresOverlay = metadata.RequiresOverlay;

        _currentBase = _baseFirmwareManager.GetInstalledBases()
            .FirstOrDefault(info => info.Status == BaseInstallStatus.Valid && PathsEqual(info.LocalPath, _currentBaseUiPath))?
            .Definition;
        CurrentBaseLabel = _currentBase is null ? "No Base" : ConsoleFamilies.ShortLabel(_currentBase);
        Atlases = _textureAtlasReader.ReadAll(draft.DraftPath, _currentBaseUiPath);
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
        if (_draft is null)
        {
            Wallpapers = [];
            ExtraAssets = [];
            MissingTextures = [];
            ReloadCharacters();
            SelectedWallpaper = null;
            SelectedExtraAsset = null;
            return;
        }

        var report = _resourceIndexer.Analyze(_draft.DraftPath, _currentBaseUiPath);
        var selectedWallpaperName = SelectedWallpaper?.Name;
        var selectedExtraPath = SelectedExtraAsset?.RelativePath;

        Wallpapers = _assetManager.GetWallpapers(_draft.DraftPath, _currentBaseUiPath)
            .Select(wallpaper => wallpaper with { UsedBy = report.UsageOf(wallpaper.TextureId) })
            .ToList();
        SelectedWallpaper = Wallpapers.FirstOrDefault(wallpaper => wallpaper.Name == selectedWallpaperName)
            ?? (Wallpapers.Count > 0 ? Wallpapers[0] : null);

        ExtraAssets = _assetManager.GetExtraAssets(_draft.DraftPath, _currentBaseUiPath)
            .Select(asset => asset with { UsedBy = report.UsageOf(asset.TextureId) })
            .ToList();
        SelectedExtraAsset = ExtraAssets.FirstOrDefault(asset => asset.RelativePath == selectedExtraPath)
            ?? (ExtraAssets.Count > 0 ? ExtraAssets[0] : null);

        MissingTextures = report.MissingTextures;
        SelectedMissingTexture = MissingTextures.Count > 0 ? MissingTextures[0] : null;
        ReloadCharacters();
    }

    public bool ProvideSelectedMissingTexture(string sourceImagePath)
    {
        if (SelectedMissingTexture is not { } texture || _draft is null)
        {
            Status = "Select a missing texture first.";
            return false;
        }

        try
        {
            _assetManager.ProvideMissingTexture(_draft.DraftPath, texture, sourceImagePath);
            ReloadAssets();
            RunValidation();
            HasUnsavedChanges = true;
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

    partial void OnSelectedExtraAssetChanged(ExtraAsset? value)
    {
        CanReplaceExtraAsset = value is not null;
    }
}
