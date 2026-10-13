using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

public sealed record AnimationFrameView(double Time, string Name, Bitmap? Thumbnail, bool IsLoopFrame)
{
    public string TimeText => Time.ToString("0.000 's'", CultureInfo.InvariantCulture);

    public double Opacity => IsLoopFrame ? 1 : 0.45;
}

public partial class MainWindowViewModel
{
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

    public AtlasSize? CurrentCharacterFrameSize { get; private set; }

    public bool ReplaceAnimationFrames(IReadOnlyList<string> framePaths, AtlasSize frameSize, bool resizeCharacter)
    {
        if (_draft is null || SelectedCharacter is not { } character || SelectedAnimation is not { } animation || PrimaryTrack(animation) is not { } track)
        {
            Status = "Select a character animation first.";
            return false;
        }

        try
        {
            var loopIds = track.Keys.Where(key => key.Time < animation.Duration).Select(key => key.TextureId).Distinct().ToList();
            var frameIds = new List<string>();
            for (var index = 0; index < framePaths.Count; index++)
            {
                if (index < loopIds.Count && _characterTextures.TryGetValue(loopIds[index], out var existing))
                {
                    var relativePath = RelativeToRoot(existing.ImagePath);
                    _assetManager.ReplaceExtraAsset(
                        Path.Combine(_draft.DraftPath, relativePath),
                        framePaths[index],
                        Path.ChangeExtension(existing.ImagePath, ".json"));
                    frameIds.Add(loopIds[index]);
                }
                else
                {
                    frameIds.Add(_characterEditor.AddTexture(_draft.DraftPath, _currentBaseUiPath, NextFrameRelativePath(loopIds), framePaths[index]));
                }
            }

            var frameTime = (double)FrameTimeMilliseconds / 1000;
            var duration = frameTime * frameIds.Count;
            var keys = frameIds.Select((id, index) => new AnimationKey(index * frameTime, id))
                .Concat(track.Keys.Where(key => key.Time >= animation.Duration).Select(key => key with { Time = key.Time - animation.Duration + duration }))
                .ToList();
            _characterEditor.SetTrackKeys(_draft.DraftPath, _currentBaseUiPath, animation.ResourcePath, track.NodeName, keys, duration);
            if (resizeCharacter)
            {
                _characterEditor.SetFrameSize(_draft.DraftPath, _currentBaseUiPath, character, frameSize);
            }

            var animationName = animation.Name;
            HasUnsavedChanges = true;
            ReloadAssets();
            SelectedAnimation = CharacterAnimations.FirstOrDefault(candidate => candidate.Name == animationName);
            RunValidation();
            Status = $"{character} {animationName}: {frameIds.Count} frames at {FrameTimeMilliseconds} ms. Other animations of {character} still use their own frames.";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Status = exception.Message;
            return false;
        }
    }

    public bool ApplyFrameTime()
    {
        if (_draft is null || SelectedAnimation is not { } animation || PrimaryTrack(animation) is not { } track)
        {
            Status = "Select a character animation first.";
            return false;
        }

        try
        {
            var loopKeys = track.Keys.Where(key => key.Time < animation.Duration).OrderBy(key => key.Time).ToList();
            var frameTime = (double)FrameTimeMilliseconds / 1000;
            var duration = frameTime * loopKeys.Count;
            var keys = loopKeys.Select((key, index) => key with { Time = index * frameTime })
                .Concat(track.Keys.Where(key => key.Time >= animation.Duration).Select(key => key with { Time = key.Time - animation.Duration + duration }))
                .ToList();
            _characterEditor.SetTrackKeys(_draft.DraftPath, _currentBaseUiPath, animation.ResourcePath, track.NodeName, keys, duration);

            var animationName = animation.Name;
            var frameTimeMilliseconds = FrameTimeMilliseconds;
            HasUnsavedChanges = true;
            ReloadAssets();
            SelectedAnimation = CharacterAnimations.FirstOrDefault(candidate => candidate.Name == animationName);
            Status = $"{SelectedCharacter} {animationName}: {loopKeys.Count} frames at {frameTimeMilliseconds} ms ({duration:0.###} s per loop).";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Status = exception.Message;
            return false;
        }
    }

    private void ReloadCharacters()
    {
        if (_draft is null)
        {
            Characters = [];
            SelectedCharacter = null;
            return;
        }

        var previous = SelectedCharacter;
        _characterTextures = _characterEditor.GetTextures(_draft.DraftPath, _currentBaseUiPath);
        DisposeThumbnails();
        Characters = _characterEditor.GetCharacters(_draft.DraftPath, _currentBaseUiPath);
        SelectedCharacter = null;
        SelectedCharacter = Characters.Contains(previous) ? previous : (Characters.Count > 0 ? Characters[0] : null);
    }

    partial void OnSelectedCharacterChanged(string? value)
    {
        if (_draft is null || value is null)
        {
            CharacterAnimations = [];
            SelectedAnimation = null;
            CharacterSizeText = "";
            CurrentCharacterFrameSize = null;
            return;
        }

        var previousName = SelectedAnimation?.Name;
        CharacterAnimations = _characterEditor.GetAnimations(_draft.DraftPath, _currentBaseUiPath, value);
        SelectedAnimation = CharacterAnimations.FirstOrDefault(animation => animation.Name == previousName)
            ?? CharacterAnimations.FirstOrDefault(animation => animation.Name == "run")
            ?? CharacterAnimations.FirstOrDefault();

        var node = _characterEditor.GetNodes(_draft.DraftPath, _currentBaseUiPath, value).FirstOrDefault();
        CurrentCharacterFrameSize = SelectedAnimation is { } animation && PrimaryTrack(animation) is { Keys.Count: > 0 } track
            && _characterTextures.TryGetValue(track.Keys[0].TextureId, out var texture)
            ? new AtlasSize(texture.Width, texture.Height)
            : null;
        CharacterSizeText = node is null
            ? ""
            : $"Frames {CurrentCharacterFrameSize?.Width} x {CurrentCharacterFrameSize?.Height}, drawn at {node.Width:0} x {node.Height:0} in {node.Scene}";
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
                ? new AnimationFrameView(key.Time, texture.Name, Thumbnail(texture), key.Time < value.Duration)
                : new AnimationFrameView(key.Time, key.TextureId, null, key.Time < value.Duration))
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
            if (!File.Exists(Path.Combine(_draft!.DraftPath, candidate))
                && (_currentBaseUiPath is null || !File.Exists(Path.Combine(_currentBaseUiPath, candidate))))
            {
                return candidate;
            }
        }
    }

    private string RelativeToRoot(string imagePath)
    {
        var root = _currentBaseUiPath is not null && !Path.GetRelativePath(_currentBaseUiPath, imagePath).StartsWith("..", StringComparison.Ordinal)
            ? _currentBaseUiPath
            : _draft!.DraftPath;
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
