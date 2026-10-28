using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

// The Sprites tab: the packed atlases of the theme and the sprites cut from them.
public sealed partial class SpritesViewModel : ViewModelBase
{
    private static readonly FileType Png = new("PNG image", "*.png");

    private readonly ThemeSession _session;
    private readonly ITextureAtlasReader _atlasReader;
    private readonly ITextureAtlasEditor _atlasEditor;
    private readonly IThemeAssetManager _assetManager;
    private readonly IFileDialogService _fileDialogs;
    private readonly IErrorHandler _errorHandler;

    public SpritesViewModel(
        ThemeSession session,
        ITextureAtlasReader atlasReader,
        ITextureAtlasEditor atlasEditor,
        IThemeAssetManager assetManager,
        IFileDialogService fileDialogs,
        IErrorHandler errorHandler)
    {
        _session = session;
        _atlasReader = atlasReader;
        _atlasEditor = atlasEditor;
        _assetManager = assetManager;
        _fileDialogs = fileDialogs;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
    }

    [ObservableProperty]
    private IReadOnlyList<TextureAtlas> atlases = [];

    [ObservableProperty]
    private TextureAtlas? selectedAtlas;

    [ObservableProperty]
    private string atlasSummary = "No packed atlas";

    [ObservableProperty]
    private string atlasImagePath = "";

    // raised when the atlas image changed on disk while its path stayed the same, so the view redraws it
    [ObservableProperty]
    private int atlasRevision;

    [ObservableProperty]
    private IReadOnlyList<SpriteRegion> sprites = [];

    [ObservableProperty]
    private IReadOnlyList<SpriteRegion> filteredSprites = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReplaceSprite))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceSpriteCommand), nameof(ExportSpriteCommand))]
    private SpriteRegion? selectedSprite;

    [ObservableProperty]
    private string spriteSearchText = "";

    public bool CanReplaceSprite => SelectedSprite is not null && AtlasImagePath.Length > 0;

    // selects the sprite a scene draws; false when no atlas has it
    public bool ShowSprite(SceneSpriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var atlas = Atlases.FirstOrDefault(candidate => candidate.TextureId == request.TextureId)
            ?? Atlases.FirstOrDefault(candidate => request.ImagePath is not null && SamePath(candidate.ImagePath, request.ImagePath));
        if (atlas?.Sprites.FirstOrDefault(sprite => sprite.Name == request.SpriteName) is not { } sprite)
        {
            _session.Status = $"Sprite {request.SpriteName} was not found in the atlases.";
            return false;
        }

        SelectedAtlas = atlas;
        SpriteSearchText = "";
        SelectedSprite = sprite;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanReplaceSprite))]
    private async Task ReplaceSpriteAsync()
    {
        if (_session.Draft is not { } draft
            || SelectedSprite is not { } sprite
            || SelectedAtlas is not { } atlas
            || await _fileDialogs.OpenFileAsync("Select replacement PNG", Png) is not { } replacementPath)
        {
            return;
        }

        string? draftImagePath = null;
        if (!await _errorHandler.TryRunAsync($"Cannot replace {sprite.Name}", () =>
        {
            // the Base atlas is copied into the draft before its first change
            draftImagePath = _assetManager.EnsureDraftCopy(draft.DraftPath, atlas.RelativeImagePath, atlas.ImagePath);
            _atlasEditor.ReplaceSprite(new SpriteReplacementRequest(draftImagePath, sprite, replacementPath));
            return Task.CompletedTask;
        }))
        {
            return;
        }

        if (draftImagePath != atlas.ImagePath)
        {
            var draftAtlas = atlas with { ImagePath = draftImagePath! };
            Atlases = Atlases.Select(candidate => candidate == atlas ? draftAtlas : candidate).ToList();
            SelectedAtlas = draftAtlas;
            SelectedSprite = sprite;
        }

        AtlasRevision++;
        _session.MarkChanged();
        _session.Status = $"Replaced {sprite.Name} in draft.";
    }

    [RelayCommand(CanExecute = nameof(CanReplaceSprite))]
    private async Task ExportSpriteAsync()
    {
        if (SelectedSprite is not { } sprite
            || await _fileDialogs.SaveFileAsync("Export sprite to PNG", $"{sprite.Name}.png", "png", Png) is not { } savePath)
        {
            return;
        }

        var atlasImagePath = AtlasImagePath;
        if (await _errorHandler.TryRunAsync($"Cannot export {sprite.Name}", () =>
        {
            _assetManager.ExportPackedSprite(atlasImagePath, sprite, savePath);
            return Task.CompletedTask;
        }))
        {
            _session.Status = $"Exported {sprite.Name} to {Path.GetFileName(savePath)}.";
        }
    }

    private void Reload()
    {
        var previousAtlas = SelectedAtlas?.Name;
        Atlases = _session.Draft is { } draft ? _atlasReader.ReadAll(draft.DraftPath, _session.BaseUiPath) : [];
        SelectedAtlas = Atlases.FirstOrDefault(atlas => atlas.Name == previousAtlas)
            ?? (Atlases.Count > 0 ? Atlases[0] : null);
        AtlasRevision++;
    }

    partial void OnSpriteSearchTextChanged(string value)
    {
        ApplySpriteFilter();
    }

    partial void OnSelectedAtlasChanged(TextureAtlas? value)
    {
        var previousName = SelectedSprite?.Name;
        AtlasSummary = value is null ? "No sprite atlas" : $"{value.Size.Width} x {value.Size.Height}";
        AtlasImagePath = value?.ImagePath ?? "";
        Sprites = value?.Sprites ?? [];
        ApplySpriteFilter();
        SelectedSprite = Sprites.FirstOrDefault(sprite => sprite.Name == previousName)
            ?? (Sprites.Count > 0 ? Sprites[0] : null);
    }

    partial void OnAtlasImagePathChanged(string value)
    {
        OnPropertyChanged(nameof(CanReplaceSprite));
        ReplaceSpriteCommand.NotifyCanExecuteChanged();
        ExportSpriteCommand.NotifyCanExecuteChanged();
    }

    private void ApplySpriteFilter()
    {
        FilteredSprites = string.IsNullOrWhiteSpace(SpriteSearchText)
            ? Sprites
            : Sprites.Where(sprite => sprite.Name.Contains(SpriteSearchText, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static bool SamePath(string first, string second)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);
    }
}
