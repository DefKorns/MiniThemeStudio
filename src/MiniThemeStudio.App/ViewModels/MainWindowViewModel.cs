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

    private ThemeDraft? _draft;
    private string? _currentBaseUiPath;

    public MainWindowViewModel(
        IThemeMetadataReader metadataReader,
        ITextureAtlasReader textureAtlasReader,
        IThemeDraftService themeDraftService,
        ITextureAtlasEditor textureAtlasEditor,
        IThemeAssetManager? assetManager = null,
        IThemeValidator? themeValidator = null,
        IBaseFirmwareManager? baseFirmwareManager = null)
    {
        _metadataReader = metadataReader;
        _textureAtlasReader = textureAtlasReader;
        _themeDraftService = themeDraftService;
        _textureAtlasEditor = textureAtlasEditor;
        _assetManager = assetManager ?? new ThemeAssetManager();
        _themeValidator = themeValidator ?? new ThemeValidator();
        _baseFirmwareManager = baseFirmwareManager ?? new BaseFirmwareManager(FirmwareRegistry.CreateDefault());
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

    public bool OpenTheme(string rootPath, string? atlasDefinitionRootPath = null)
    {
        _currentBaseUiPath = atlasDefinitionRootPath ?? _baseFirmwareManager.FindCompatibleBaseUiPath(rootPath);
        return OpenDraft(
            () => _themeDraftService.OpenExisting(rootPath),
            false,
            _currentBaseUiPath);
    }

    public bool CreateTheme(NewThemeRequest request)
    {
        _currentBaseUiPath = request.BaseThemePath;
        return OpenDraft(() => _themeDraftService.CreateNew(request), true, null);
    }

    public bool SaveTheme()
    {
        if (_draft is null || !HasUnsavedChanges)
        {
            return false;
        }

        // Validate before save
        var validation = _themeValidator.Validate(_draft.DraftPath, _currentBaseUiPath);
        ValidationIssues = validation.Issues;
        IsThemeValid = validation.IsValid;

        if (validation.HasErrors)
        {
            Status = "Validation failed: Fix errors before saving.";
            return false;
        }

        try
        {
            _draft = _themeDraftService.Save(_draft);
            HasUnsavedChanges = false;
            Status = $"Saved to {_draft.SavePath}";
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
            Status = $"Added element {Path.GetFileName(relativeDestPath)}.";
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

    private bool OpenDraft(
        Func<ThemeDraft> createDraft,
        bool isNewTheme,
        string? atlasDefinitionRootPath)
    {
        DiscardTheme();

        try
        {
            _draft = createDraft();
            LoadDraft(_draft, atlasDefinitionRootPath);
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

    private void LoadDraft(ThemeDraft draft, string? atlasDefinitionRootPath)
    {
        var metadata = _metadataReader.Read(draft.DraftPath, Path.GetFileName(draft.SavePath));
        ThemeName = metadata.Name;
        Author = metadata.Author ?? "Not specified";
        ThemePath = draft.SavePath;
        RequiresOverlay = metadata.RequiresOverlay;

        Atlases = _textureAtlasReader.ReadAll(draft.DraftPath, atlasDefinitionRootPath);
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
            SelectedWallpaper = null;
            SelectedExtraAsset = null;
            return;
        }

        Wallpapers = _assetManager.GetWallpapers(_draft.DraftPath, _currentBaseUiPath);
        SelectedWallpaper = Wallpapers.Count > 0 ? Wallpapers[0] : null;

        ExtraAssets = _assetManager.GetExtraAssets(_draft.DraftPath, _currentBaseUiPath);
        SelectedExtraAsset = ExtraAssets.Count > 0 ? ExtraAssets[0] : null;
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
