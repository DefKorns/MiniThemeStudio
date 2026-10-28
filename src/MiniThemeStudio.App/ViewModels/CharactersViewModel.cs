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

namespace MiniThemeStudio.App.ViewModels;

public sealed record AnimationFrameView(double Time, string TextureId, string Name, Bitmap? Thumbnail, bool IsLoopFrame)
{
    public string TimeText => Time.ToString("0.000 's'", CultureInfo.InvariantCulture);

    public double Opacity => IsLoopFrame ? 1 : 0.45;
}

// The Characters tab: the animations of the characters running in the menu, their frames and timing.
public sealed partial class CharactersViewModel : ViewModelBase
{
    private readonly ThemeSession _session;
    private readonly ICharacterAnimationEditor _characterEditor;
    private readonly IThemeAssetManager _assetManager;
    private readonly IAppDialogs _appDialogs;
    private readonly IDialogService _dialogService;
    private readonly IErrorHandler _errorHandler;

    public CharactersViewModel(
        ThemeSession session,
        ICharacterAnimationEditor characterEditor,
        IThemeAssetManager assetManager,
        IAppDialogs appDialogs,
        IDialogService dialogService,
        IErrorHandler errorHandler)
    {
        _session = session;
        _characterEditor = characterEditor;
        _assetManager = assetManager;
        _appDialogs = appDialogs;
        _dialogService = dialogService;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
    }

    private IReadOnlyDictionary<string, CharacterTexture> _characterTextures = new Dictionary<string, CharacterTexture>();
    private readonly Dictionary<string, Bitmap?> _thumbnails = [];

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
        SelectedCharacter = null;
        SelectedCharacter = Characters.Contains(previous) ? previous : (Characters.Count > 0 ? Characters[0] : null);
    }

    partial void OnSelectedCharacterChanged(string? value)
    {
        if (_session.Draft is null || value is null)
        {
            CharacterAnimations = [];
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
