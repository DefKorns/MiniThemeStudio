using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Themes;
using SkiaSharp;

namespace MiniThemeStudio.App.ViewModels;

public sealed record AnimationFrameView(double Time, string TextureId, string Name, Bitmap? Thumbnail, bool IsLoopFrame)
{
    public string TimeText => Time.ToString("0.000 's'", CultureInfo.InvariantCulture);

    public double Opacity => IsLoopFrame ? 1 : 0.45;
}

// one image a character is drawn with (mario_run01 is "Run 1"), as the simple editing lists them
public sealed record CharacterFrameRow(CharacterTexture Texture, string Label, Bitmap? Thumbnail)
{
    public string SizeText => string.Create(CultureInfo.InvariantCulture, $"{Texture.Width} × {Texture.Height}");
}

// one picture of a character sheet of the NES and Famicom UIs, cut from the sheet for its tile
public sealed record SheetFrameView(CharacterSheet Sheet, CharacterSheetFrame Frame, string Label, Bitmap? Thumbnail)
{
    public string SizeText => string.Create(CultureInfo.InvariantCulture, $"up to {Frame.SlotWidth} × {Frame.SlotHeight}");
}

public sealed record SheetRowView(string Label, IReadOnlyList<SheetFrameView> Frames);

public sealed record SheetView(CharacterSheet Sheet, string Title, string Description, IReadOnlyList<SheetRowView> Rows, Bitmap? Image)
{
    public string SizeText => string.Create(CultureInfo.InvariantCulture, $"{Sheet.Width} × {Sheet.Height}");
}

// one row of a sheet, as the character picker lists them
public sealed record SheetGroupView(SheetView Sheet, SheetRowView Row)
{
    public string Label => Row.Label;
}

// The Characters tab: the animations of the characters running in the menu, their frames and timing.
public sealed partial class CharactersViewModel : ViewModelBase, IDisposable
{
    private readonly ThemeSession _session;
    private readonly ICharacterAnimationEditor _characterEditor;
    private readonly IThemeAssetManager _assetManager;
    private readonly IAppDialogs _appDialogs;
    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileDialogs;
    private readonly ICharacterSheetEditor _sheetEditor;

    // what the rows of the menu's sheet show, from the top
    private static readonly string[] SheetRowNames = ["Mario", "Luigi", "Block"];

    // what the pictures of the background's sheet show, from the left
    private static readonly string[] BackgroundCharacterNames = ["Mario", "Luigi", "Mario and Luigi"];

    private List<Bitmap> _sheetBitmaps = [];
    private readonly IErrorHandler _errorHandler;

    // the words of the console's frame names, as people say them
    private static readonly Dictionary<string, string> FrameWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dush"] = "Dash",
        ["look"] = "Look up",
    };

    public CharactersViewModel(
        ThemeSession session,
        ICharacterAnimationEditor characterEditor,
        IThemeAssetManager assetManager,
        IAppDialogs appDialogs,
        IDialogService dialogService,
        IFileDialogService fileDialogs,
        ICharacterSheetEditor sheetEditor,
        IErrorHandler errorHandler,
        EditingMode mode)
    {
        _session = session;
        _characterEditor = characterEditor;
        _assetManager = assetManager;
        _appDialogs = appDialogs;
        _dialogService = dialogService;
        _fileDialogs = fileDialogs;
        _sheetEditor = sheetEditor;
        _errorHandler = errorHandler;
        Mode = mode;
        Mode.PropertyChanged += (_, _) => NotifyLayout();
        _session.Reloaded += (_, _) => Reload();
    }

    private IReadOnlyDictionary<string, CharacterTexture> _characterTextures = new Dictionary<string, CharacterTexture>();
    private readonly Dictionary<string, Bitmap?> _thumbnails = [];

    public EditingMode Mode { get; }

    // the NES and Famicom UIs draw their characters from sheets instead of scene animations
    [ObservableProperty]
    private IReadOnlyList<SheetView> sheets = [];

    // Mario, Luigi, the block and the small characters, each shown alone like a SNES character
    [ObservableProperty]
    private IReadOnlyList<SheetGroupView> sheetGroups = [];

    [ObservableProperty]
    private SheetGroupView? selectedSheetGroup;

    public bool ShowSheet => Sheets.Count > 0 && Characters.Count == 0;

    public bool ShowAnimations => !ShowSheet;

    public bool ShowSimpleFrames => ShowAnimations && Mode.IsSimple;

    public bool ShowAdvancedFrames => ShowAnimations && Mode.IsAdvanced;

    // the images of the selected character: the same frames as the official theme, replaced in place
    [ObservableProperty]
    private IReadOnlyList<CharacterFrameRow> characterFrames = [];

    [ObservableProperty]
    private IReadOnlyList<string> characters = [];

    [ObservableProperty]
    private string? selectedCharacter;

    [ObservableProperty]
    private IReadOnlyList<CharacterAnimation> characterAnimations = [];

    [ObservableProperty]
    private CharacterAnimation? selectedAnimation;

    [ObservableProperty]
    private IReadOnlyList<AnimationFrameView> animationFrames = [];

    [ObservableProperty]
    private string characterSizeText = "";

    [ObservableProperty]
    private decimal frameTimeMilliseconds = 50;

    // the window preview is drawn flipped; turned off once the frames themselves were flipped
    [ObservableProperty]
    private bool isPreviewMirrored;

    public AtlasSize? CurrentCharacterFrameSize { get; private set; }

    [RelayCommand]
    private async Task ReplaceFramesAsync()
    {
        if (_session.Draft is null || SelectedCharacter is not { } character || SelectedAnimation is not { } animation || PrimaryTrack(animation) is not { } track)
        {
            return;
        }

        var sheet = await _appDialogs.ImportSpriteSheetAsync(
            $"Frames for {character} {animation.Name}",
            CurrentCharacterFrameSize,
            AnimationFrames.FirstOrDefault(frame => frame.IsLoopFrame)?.Thumbnail);
        if (sheet is null)
        {
            return;
        }

        var resizeCharacter = false;
        if (CurrentCharacterFrameSize is { } current
            && (current.Width != sheet.FrameSize.Width || current.Height != sheet.FrameSize.Height))
        {
            resizeCharacter = await _dialogService.ShowConfirmationAsync(
                "Resize the character?",
                $"The new frames are {sheet.FrameSize.Width} x {sheet.FrameSize.Height} but {character} is drawn from {current.Width} x {current.Height} frames. "
                + "Resize the character in the scenes so the new frames are not stretched? Its other animations will then need frames of the same size.",
                DialogVariant.Warning,
                "Resize character",
                "Keep current size",
                false);
        }

        var draftPath = _session.Draft.DraftPath;
        var frameTime = (double)FrameTimeMilliseconds / 1000;
        var frameCount = 0;
        if (!await RunAsync("Cannot replace frames", () =>
        {
            // the loop frames are replaced in place, extra frames become new textures
            var loopIds = track.Keys.Where(key => key.Time < animation.Duration).Select(key => key.TextureId).Distinct().ToList();
            var frameIds = new List<string>();
            for (var index = 0; index < sheet.FramePaths.Count; index++)
            {
                if (index < loopIds.Count && _characterTextures.TryGetValue(loopIds[index], out var existing))
                {
                    _assetManager.ReplaceExtraAsset(
                        Path.Combine(draftPath, RelativeToRoot(existing.ImagePath)),
                        sheet.FramePaths[index],
                        Path.ChangeExtension(existing.ImagePath, ".json"));
                    frameIds.Add(loopIds[index]);
                }
                else
                {
                    frameIds.Add(_characterEditor.AddTexture(draftPath, _session.BaseUiPath, NextFrameRelativePath(loopIds), sheet.FramePaths[index]));
                }
            }

            var duration = frameTime * frameIds.Count;
            var keys = frameIds.Select((id, index) => new AnimationKey(index * frameTime, id))
                .Concat(track.Keys.Where(key => key.Time >= animation.Duration).Select(key => key with { Time = key.Time - animation.Duration + duration }))
                .ToList();
            _characterEditor.SetTrackKeys(draftPath, _session.BaseUiPath, animation.ResourcePath, track.NodeName, keys, duration);
            if (resizeCharacter)
            {
                _characterEditor.SetFrameSize(draftPath, _session.BaseUiPath, character, sheet.FrameSize);
            }

            frameCount = frameIds.Count;
        }))
        {
            return;
        }

        var frameTimeMilliseconds = FrameTimeMilliseconds;
        AfterChange(animation.Name);
        _session.Status = string.Create(CultureInfo.InvariantCulture, $"{character} {animation.Name}: {frameCount} frames at {frameTimeMilliseconds} ms. Other animations of {character} still use their own frames.");
    }

    [RelayCommand]
    private async Task ReplaceSheetFrameAsync(SheetFrameView? frame)
    {
        if (_session.Draft is not { } draft || frame is null
            || await _fileDialogs.OpenFileAsync($"Select the new {frame.Label}", new FileType("PNG image", "*.png")) is not { } imagePath)
        {
            return;
        }

        if (SheetGroups.FirstOrDefault(group => group.Row.Frames.Contains(frame))?.Row is { } row)
        {
            await ReplaceRowAsync(draft, row, [(frame.Frame.Index, imagePath)], $"Cannot replace {frame.Label}", _ => $"Replaced {frame.Label}.");
        }
    }

    // the pictures picked on a sprite sheet replace the frames of the row, in the order they were picked
    [RelayCommand]
    private async Task ImportRowAsync(SheetRowView? row)
    {
        if (_session.Draft is not { } draft || row is not { Frames.Count: > 0 } || row.Frames[0].Sheet is not { } sheet)
        {
            return;
        }

        var room = new AtlasSize(row.Frames.Max(frame => frame.Frame.SlotWidth), row.Frames.Max(frame => frame.Frame.SlotHeight));
        var picked = await _appDialogs.ImportSpriteSheetAsync(
            string.Create(CultureInfo.InvariantCulture, $"Pick the {row.Frames.Count} pictures of {row.Label}, in order"),
            room,
            row.Frames[0].Thumbnail);
        if (picked is not { FramePaths.Count: > 0 })
        {
            return;
        }

        var pictures = picked.FramePaths.Take(row.Frames.Count).Select((path, index) => (index, path)).ToList();
        await ReplaceRowAsync(
            draft,
            row,
            pictures,
            $"Cannot import the pictures of {row.Label}",
            count => string.Create(CultureInfo.InvariantCulture, $"{row.Label}: replaced {count} of {row.Frames.Count} pictures from the sprite sheet."));
    }

    // New pictures for frames of a row. Pictures larger than the frames make the row bigger, when the script that
    // cuts it can be rewritten and the user wants it, or are made smaller to fit.
    private async Task ReplaceRowAsync(ThemeDraft draft, SheetRowView row, List<(int Index, string Path)> pictures, string failure, Func<int, string> status)
    {
        var sheet = row.Frames[0].Sheet;
        var room = row.Frames[0].Frame;
        var needed = (Width: 0, Height: 0);
        if (!await RunAsync(failure, () =>
        {
            foreach (var (_, path) in pictures)
            {
                var (width, height) = _sheetEditor.MeasurePicture(path);
                needed = (Math.Max(needed.Width, width), Math.Max(needed.Height, height));
            }
        }))
        {
            return;
        }

        var grow = false;
        if ((needed.Width > room.SlotWidth || needed.Height > room.SlotHeight) && sheet.CanResize)
        {
            var answer = await _dialogService.ShowChoiceAsync(
                "Larger pictures",
                string.Create(CultureInfo.InvariantCulture,
                    $"The pictures are up to {needed.Width} × {needed.Height}, larger than the {room.SlotWidth} × {room.SlotHeight} frames of {row.Label}. ")
                    + $"Make {row.Label} bigger to fit them (the character sheet and sys_wallpaper_fg0.lua are rewritten, and {row.Label} is drawn bigger in the menu), or make the pictures smaller?",
                $"Make {row.Label} bigger",
                "Make them smaller",
                "Cancel",
                DialogVariant.Info);
            if (answer == DialogResult.Cancelled)
            {
                return;
            }

            grow = answer == DialogResult.Accepted;
        }

        var smallest = 1.0;
        if (await RunAsync(failure, () =>
        {
            var target = sheet;
            var frames = row.Frames.Select(frame => frame.Frame).ToList();
            if (grow)
            {
                _sheetEditor.ResizeRow(draft.DraftPath, _session.BaseUiPath, sheet, room.Row, needed.Width, needed.Height);
                target = _sheetEditor.GetSheets(draft.DraftPath, _session.BaseUiPath).First(candidate => candidate.Name == sheet.Name);
                frames = target.Frames.Where(frame => frame.Row == room.Row).OrderBy(frame => frame.Index).ToList();
            }

            foreach (var (index, path) in pictures)
            {
                smallest = Math.Min(smallest, _sheetEditor.ReplaceFrame(draft.DraftPath, _session.BaseUiPath, target, frames[index], path));
            }
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = status(pictures.Count)
                + (grow ? string.Create(CultureInfo.InvariantCulture, $" {row.Label} is now {Math.Max(needed.Width, room.SlotWidth)} × {Math.Max(needed.Height, room.SlotHeight)}.") : Shrunk(smallest, room));
        }
    }

    // pictures larger than their frame are made smaller; the status says so, as they lose detail
    private static string Shrunk(double scale, CharacterSheetFrame frame) => scale >= 1
        ? ""
        : string.Create(CultureInfo.InvariantCulture, $" Larger than the {frame.SlotWidth} × {frame.SlotHeight} of a frame, so made smaller (×{scale:0.##}).");

    [RelayCommand]
    private async Task ReplaceSheetAsync(SheetView? sheet)
    {
        if (_session.Draft is not { } draft || sheet is null
            || await _fileDialogs.OpenFileAsync($"Select the new {sheet.Title} ({sheet.SizeText} PNG)", new FileType("PNG image", "*.png")) is not { } imagePath)
        {
            return;
        }

        if (await RunAsync($"Cannot replace {sheet.Title}", () => _sheetEditor.ReplaceSheet(draft.DraftPath, _session.BaseUiPath, sheet.Sheet, imagePath)))
        {
            _session.NotifyContentChanged();
            _session.Status = $"Replaced {sheet.Title}.";
        }
    }

    [RelayCommand]
    private async Task ExportSheetAsync(SheetView? sheet)
    {
        if (sheet is null
            || await _fileDialogs.SaveFileAsync($"Export {sheet.Title}", Path.GetFileName(sheet.Sheet.RelativePath), "png", new FileType("PNG image", "*.png")) is not { } savePath)
        {
            return;
        }

        if (await RunAsync($"Cannot export {sheet.Title}", () => File.Copy(sheet.Sheet.ImagePath, savePath, true)))
        {
            _session.Status = $"Exported {sheet.Title} to {Path.GetFileName(savePath)}.";
        }
    }

    // a new picture for one frame; it keeps the size of the frame, which the scenes draw it at
    [RelayCommand]
    private async Task ReplaceFrameAsync(CharacterFrameRow? frame)
    {
        if (_session.Draft is not { } draft || frame is null
            || await _fileDialogs.OpenFileAsync($"Select the new {frame.Label} of {SelectedCharacter}", new FileType("PNG image", "*.png")) is not { } imagePath)
        {
            return;
        }

        var texture = frame.Texture;
        if (await RunAsync($"Cannot replace {frame.Label}", () =>
        {
            using var picture = new Bitmap(imagePath);
            var (width, height) = (picture.PixelSize.Width, picture.PixelSize.Height);
            if (width != texture.Width || height != texture.Height)
            {
                throw new OperationFailedException(string.Create(CultureInfo.InvariantCulture,
                    $"{frame.Label} must be {texture.Width} × {texture.Height} pixels, like the frame it replaces; {Path.GetFileName(imagePath)} is {width} × {height}."));
            }

            _assetManager.ReplaceExtraAsset(Path.Combine(draft.DraftPath, RelativeToRoot(texture.ImagePath)), imagePath, Path.ChangeExtension(texture.ImagePath, ".json"));
        }))
        {
            AfterChange(SelectedAnimation?.Name ?? "");
            _session.Status = $"{SelectedCharacter}: replaced {frame.Label}.";
        }
    }

    [RelayCommand]
    private Task MirrorSelectedFramesAsync(IList? selectedFrames)
    {
        return MirrorFramesAsync((selectedFrames ?? Array.Empty<object>()).OfType<AnimationFrameView>().Select(frame => frame.TextureId).ToList());
    }

    [RelayCommand]
    private Task MirrorAllFramesAsync()
    {
        return MirrorFramesAsync(SelectedAnimation?.Tracks.SelectMany(track => track.Keys).Select(key => key.TextureId).ToList() ?? []);
    }

    [RelayCommand]
    private async Task ApplyFrameTimeAsync()
    {
        if (_session.Draft is null || SelectedAnimation is not { } animation || PrimaryTrack(animation) is not { } track)
        {
            return;
        }

        var draftPath = _session.Draft.DraftPath;
        var loopKeys = track.Keys.Where(key => key.Time < animation.Duration).OrderBy(key => key.Time).ToList();
        var frameTimeMilliseconds = FrameTimeMilliseconds;
        var frameTime = (double)frameTimeMilliseconds / 1000;
        var duration = frameTime * loopKeys.Count;
        if (await RunAsync("Cannot change the frame time", () =>
        {
            var keys = loopKeys.Select((key, index) => key with { Time = index * frameTime })
                .Concat(track.Keys.Where(key => key.Time >= animation.Duration).Select(key => key with { Time = key.Time - animation.Duration + duration }))
                .ToList();
            _characterEditor.SetTrackKeys(draftPath, _session.BaseUiPath, animation.ResourcePath, track.NodeName, keys, duration);
        }))
        {
            AfterChange(animation.Name);
            _session.Status = string.Create(CultureInfo.InvariantCulture, $"{SelectedCharacter} {animation.Name}: {loopKeys.Count} frames at {frameTimeMilliseconds} ms ({duration:0.###} s per loop).");
        }
    }

    private async Task MirrorFramesAsync(List<string> textureIds)
    {
        if (_session.Draft is null || SelectedCharacter is not { } character || SelectedAnimation is not { } animation)
        {
            return;
        }

        if (textureIds.Count == 0)
        {
            _session.Status = "Select the frames to flip.";
            return;
        }

        var draftPath = _session.Draft.DraftPath;
        var distinct = textureIds.ToHashSet(StringComparer.Ordinal);
        if (!await RunAsync("Cannot flip frames", () => _characterEditor.MirrorTextures(draftPath, _session.BaseUiPath, distinct)))
        {
            return;
        }

        // frames are textures, so other animations showing them flip too
        var shared = CharacterAnimations
            .Where(candidate => candidate.Name != animation.Name
                && candidate.Tracks.Any(track => track.Keys.Any(key => distinct.Contains(key.TextureId))))
            .Select(candidate => candidate.Name)
            .ToList();
        IsPreviewMirrored = false;
        AfterChange(animation.Name);
        _session.Status = $"{character} {animation.Name}: flipped {distinct.Count} frame{(distinct.Count == 1 ? "" : "s")} horizontally."
            + (shared.Count > 0 ? $" Also used by {string.Join(", ", shared)}." : "");
    }

    // the theme reloads after a change; the animation that was edited stays selected
    private void AfterChange(string animationName)
    {
        _session.NotifyContentChanged();
        SelectedAnimation = CharacterAnimations.FirstOrDefault(candidate => candidate.Name == animationName) ?? SelectedAnimation;
    }

    private Task<bool> RunAsync(string failure, Action action)
    {
        return _errorHandler.TryRunAsync(failure, () =>
        {
            action();
            return Task.CompletedTask;
        });
    }

    private void Reload()
    {
        if (_session.Draft is null)
        {
            Characters = [];
            SelectedCharacter = null;
            return;
        }

        var previous = SelectedCharacter;
        _characterTextures = _characterEditor.GetTextures(_session.Draft.DraftPath, _session.BaseUiPath);
        DisposeThumbnails();
        Characters = _characterEditor.GetCharacters(_session.Draft.DraftPath, _session.BaseUiPath);
        LoadSheets();
        SelectedCharacter = null;
        SelectedCharacter = Characters.Contains(previous) ? previous : (Characters.Count > 0 ? Characters[0] : null);
    }

    partial void OnSelectedCharacterChanged(string? value)
    {
        if (_session.Draft is null || value is null)
        {
            CharacterAnimations = [];
            CharacterFrames = [];
            SelectedAnimation = null;
            CharacterSizeText = "";
            CurrentCharacterFrameSize = null;
            return;
        }

        var previousName = SelectedAnimation?.Name;
        CharacterAnimations = _characterEditor.GetAnimations(_session.Draft.DraftPath, _session.BaseUiPath, value);
        SelectedAnimation = CharacterAnimations.FirstOrDefault(animation => animation.Name == previousName)
            ?? CharacterAnimations.FirstOrDefault(animation => animation.Name == "run")
            ?? CharacterAnimations.FirstOrDefault();

        // the frames are the images in the character's folder (layout/asset/sprite/mario/...)
        CharacterFrames = _characterTextures.Values
            .Where(texture => string.Equals(Path.GetFileName(Path.GetDirectoryName(texture.ImagePath)), value, StringComparison.OrdinalIgnoreCase))
            .OrderBy(texture => texture.Name, StringComparer.OrdinalIgnoreCase)
            .Select(texture => new CharacterFrameRow(texture, FrameLabel(value, texture.Name), Thumbnail(texture)))
            .ToList();

        var node = _characterEditor.GetNodes(_session.Draft.DraftPath, _session.BaseUiPath, value).FirstOrDefault();
        CurrentCharacterFrameSize = SelectedAnimation is { } animation && PrimaryTrack(animation) is { Keys.Count: > 0 } track
            && _characterTextures.TryGetValue(track.Keys[0].TextureId, out var texture)
            ? new AtlasSize(texture.Width, texture.Height)
            : null;
        CharacterSizeText = node is null
            ? ""
            : string.Create(CultureInfo.InvariantCulture, $"Frames {CurrentCharacterFrameSize?.Width} x {CurrentCharacterFrameSize?.Height}, drawn at {node.Width:0} x {node.Height:0} in {node.Scene}");
    }

    partial void OnSelectedAnimationChanged(CharacterAnimation? value)
    {
        if (value is null || PrimaryTrack(value) is not { } track)
        {
            AnimationFrames = [];
            return;
        }

        AnimationFrames = track.Keys
            .Select(key => _characterTextures.TryGetValue(key.TextureId, out var texture)
                ? new AnimationFrameView(key.Time, key.TextureId, texture.Name, Thumbnail(texture), key.Time < value.Duration)
                : new AnimationFrameView(key.Time, key.TextureId, key.TextureId, null, key.Time < value.Duration))
            .ToList();
        var loopFrames = track.Keys.Count(key => key.Time < value.Duration);
        if (loopFrames > 0 && value.Duration > 0)
        {
            FrameTimeMilliseconds = Math.Round((decimal)(value.Duration / loopFrames * 1000), 1);
        }
    }

    private void LoadSheets()
    {
        var previous = _sheetBitmaps;
        var previousGroup = SelectedSheetGroup?.Label;
        _sheetBitmaps = [];
        Sheets = _session.Draft is { } draft
            ? _sheetEditor.GetSheets(draft.DraftPath, _session.BaseUiPath).Select(LoadSheet).ToList()
            : [];
        SheetGroups = Sheets.SelectMany(sheet => sheet.Rows.Select(row => new SheetGroupView(sheet, row))).ToList();
        SelectedSheetGroup = SheetGroups.FirstOrDefault(group => group.Label == previousGroup) ?? (SheetGroups.Count > 0 ? SheetGroups[0] : null);
        foreach (var bitmap in previous)
        {
            bitmap.Dispose();
        }

        NotifyLayout();
    }

    private SheetView LoadSheet(CharacterSheet sheet)
    {
        using var pixels = SKBitmap.Decode(sheet.ImagePath);
        var isBackground = sheet.Name == "wallpaper-hvc";
        var rows = sheet.Frames
            .GroupBy(frame => frame.Row)
            .Select(row =>
            {
                var rowName = row.Key < sheet.RowNames.Count ? sheet.RowNames[row.Key]
                    : row.Key < SheetRowNames.Length ? SheetRowNames[row.Key] : $"Row {row.Key + 1}";
                return new SheetRowView(isBackground ? "Small Mario and Luigi" : rowName, row
                    .Select(frame => new SheetFrameView(
                        sheet,
                        frame,
                        isBackground ? (frame.Index < BackgroundCharacterNames.Length ? BackgroundCharacterNames[frame.Index] : $"Small {frame.Index + 1}") : $"{rowName} {frame.Index + 1}",
                        Cut(pixels, frame)))
                    .ToList());
            })
            .ToList();
        return isBackground
            ? new SheetView(sheet, "Background characters", "The small Mario and Luigi that walk over the background.", rows, Keep(pixels))
            : new SheetView(sheet, "Menu characters", "Mario, Luigi and the ? block of the menu.", rows, Keep(pixels));
    }

    // the frame's own pixels, for its tile
    private Bitmap? Cut(SKBitmap? sheet, CharacterSheetFrame frame)
    {
        using var subset = new SKBitmap();
        return sheet is not null && sheet.ExtractSubset(subset, new SKRectI(frame.X, frame.Y, frame.X + frame.Width, frame.Y + frame.Height))
            ? Keep(subset)
            : null;
    }

    // a bitmap of the pixels, disposed with the next sheets
    private Bitmap? Keep(SKBitmap? pixels)
    {
        if (pixels is null)
        {
            return null;
        }

        using var png = pixels.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(png.ToArray());
        var bitmap = new Bitmap(stream);
        _sheetBitmaps.Add(bitmap);
        return bitmap;
    }

    public void Dispose()
    {
        DisposeThumbnails();
        foreach (var bitmap in _sheetBitmaps)
        {
            bitmap.Dispose();
        }

        _sheetBitmaps = [];
    }

    private void NotifyLayout()
    {
        OnPropertyChanged(nameof(ShowSheet));
        OnPropertyChanged(nameof(ShowAnimations));
        OnPropertyChanged(nameof(ShowSimpleFrames));
        OnPropertyChanged(nameof(ShowAdvancedFrames));
    }

    // mario_run01 is "Run 1", mario_look "Look up"
    private static string FrameLabel(string character, string name)
    {
        var rest = name.StartsWith(character + "_", StringComparison.OrdinalIgnoreCase) ? name[(character.Length + 1)..] : name;
        var word = rest.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        var number = rest[word.Length..].TrimStart('0');
        var label = FrameWords.TryGetValue(word, out var known) ? known : word.Length > 0 ? char.ToUpperInvariant(word[0]) + word[1..] : rest;
        return number.Length > 0 ? $"{label} {number}" : label;
    }

    private static AnimationTrack? PrimaryTrack(CharacterAnimation animation)
    {
        return animation.Tracks.FirstOrDefault(track => track.NodeName.EndsWith("_0", StringComparison.Ordinal)) ?? (animation.Tracks.Count > 0 ? animation.Tracks[0] : null);
    }

    private string NextFrameRelativePath(IReadOnlyList<string> loopIds)
    {
        var template = loopIds.Select(id => _characterTextures.GetValueOrDefault(id)).FirstOrDefault(texture => texture is not null)
            ?? throw new InvalidDataException("The animation has no frame to use as a naming template.");
        var directory = Path.GetDirectoryName(RelativeToRoot(template.ImagePath))!;
        var prefix = template.Name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        for (var number = 1; ; number++)
        {
            var candidate = Path.Combine(directory, $"{prefix}{number:00}.png");
            if (!File.Exists(Path.Combine(_session.Draft!.DraftPath, candidate))
                && (_session.BaseUiPath is null || !File.Exists(Path.Combine(_session.BaseUiPath, candidate))))
            {
                return candidate;
            }
        }
    }

    private string RelativeToRoot(string imagePath)
    {
        var root = _session.BaseUiPath is not null && !Path.GetRelativePath(_session.BaseUiPath, imagePath).StartsWith("..", StringComparison.Ordinal)
            ? _session.BaseUiPath
            : _session.Draft!.DraftPath;
        return Path.GetRelativePath(root, imagePath);
    }

    private Bitmap? Thumbnail(CharacterTexture texture)
    {
        if (_thumbnails.TryGetValue(texture.ImagePath, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        try
        {
            using var stream = File.OpenRead(texture.ImagePath);
            bitmap = new Bitmap(stream);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
        }

        _thumbnails[texture.ImagePath] = bitmap;
        return bitmap;
    }

    private void DisposeThumbnails()
    {
        var previous = _thumbnails.Values.ToList();
        _thumbnails.Clear();
        AnimationFrames = [];
        foreach (var bitmap in previous)
        {
            bitmap?.Dispose();
        }
    }
}
