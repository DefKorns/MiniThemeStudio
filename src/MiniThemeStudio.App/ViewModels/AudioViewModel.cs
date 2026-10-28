using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

// The background music of the theme and how it is packaged; the options are app settings.
public sealed partial class AudioViewModel : ViewModelBase
{
    private static readonly FileType AudioFiles = new("Audio files (*.wav, *.mp3, *.ogg, *.flac, *.m4a)", "*.wav", "*.mp3", "*.ogg", "*.flac", "*.m4a", "*.aac", "*.wma");
    private static readonly FileType AllFiles = new("All files", "*");

    private readonly ThemeSession _session;
    private readonly IThemeAssetManager _assetManager;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IFFmpegAudioConverter _audioConverter;
    private readonly IFileDialogService _fileDialogs;
    private readonly IErrorHandler _errorHandler;

    public AudioViewModel(
        ThemeSession session,
        IThemeAssetManager assetManager,
        IAppSettingsStore settingsStore,
        IFFmpegAudioConverter audioConverter,
        IFileDialogService fileDialogs,
        IErrorHandler errorHandler)
    {
        _session = session;
        _assetManager = assetManager;
        _settingsStore = settingsStore;
        _audioConverter = audioConverter;
        _fileDialogs = fileDialogs;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
    }

    // the packaging options changed, so the theme validates differently
    public event EventHandler? OptionsChanged;

    [ObservableProperty]
    private IReadOnlyList<BgmTrack> bgmTracks = [];

    [ObservableProperty]
    private BgmTrack? selectedBgmTrack;

    public bool IncludeBgm
    {
        get => _settingsStore.Current.IncludeBgm;
        set => SaveOption(_settingsStore.Current.IncludeBgm != value, _settingsStore.Current with { IncludeBgm = value });
    }

    public bool ConvertBgmAudio
    {
        get => _settingsStore.Current.ConvertBgmAudio;
        set => SaveOption(_settingsStore.Current.ConvertBgmAudio != value, _settingsStore.Current with { ConvertBgmAudio = value });
    }

    public BgmAudioChannels BgmChannels
    {
        get => _settingsStore.Current.BgmChannels;
        set => SaveOption(_settingsStore.Current.BgmChannels != value, _settingsStore.Current with { BgmChannels = value });
    }

    public bool IsBgmStereo
    {
        get => BgmChannels == BgmAudioChannels.Stereo;
        set
        {
            if (value)
            {
                BgmChannels = BgmAudioChannels.Stereo;
            }
        }
    }

    public bool IsBgmMono
    {
        get => BgmChannels == BgmAudioChannels.Mono;
        set
        {
            if (value)
            {
                BgmChannels = BgmAudioChannels.Mono;
            }
        }
    }

    public ThemeValidationOptions ValidationOptions => new(
        ConvertBgmAudio: ConvertBgmAudio,
        FFmpegPath: _settingsStore.Current.FFmpegPath,
        BgmChannels: BgmChannels,
        IncludeBgm: IncludeBgm);

    public ThemeExportOptions ExportOptions => new(
        ConvertBgmAudio: ConvertBgmAudio,
        FFmpegPath: _settingsStore.Current.FFmpegPath,
        BgmChannels: BgmChannels,
        IncludeBgm: IncludeBgm);

    [RelayCommand]
    private async Task ReplaceTrackAsync(string trackName)
    {
        if (_session.Draft is not { } draft
            || await _fileDialogs.OpenFileAsync($"Select audio file for {trackName}", AudioFiles, AllFiles) is not { } audioPath)
        {
            return;
        }

        await ChangeTrackAsync(
            $"Cannot replace {trackName}",
            () => _assetManager.ReplaceBgmTrack(draft.DraftPath, trackName, audioPath, _session.BaseUiPath),
            $"Replaced BGM '{trackName}' with '{Path.GetFileName(audioPath)}'.");
    }

    [RelayCommand]
    private async Task RevertTrackAsync(string trackName)
    {
        if (_session.Draft is { } draft)
        {
            await ChangeTrackAsync($"Cannot revert {trackName}", () => _assetManager.RevertBgmTrack(draft.DraftPath, trackName), $"Reverted BGM '{trackName}' to base.");
        }
    }

    [RelayCommand]
    private async Task MuteTrackAsync(string trackName)
    {
        if (_session.Draft is { } draft)
        {
            await ChangeTrackAsync(
                $"Cannot silence {trackName}",
                () => _assetManager.MuteBgmTrack(draft.DraftPath, trackName, _session.BaseUiPath),
                $"Silenced boot music '{trackName}'.");
        }
    }

    // converts the track's draft file to the WAV format the console plays
    [RelayCommand]
    private async Task ConvertTrackAsync(string trackName)
    {
        if (_session.Draft is null)
        {
            return;
        }

        string? format = null;
        if (await _errorHandler.TryRunAsync($"Cannot convert {trackName}", async () =>
        {
            string ffmpegPath = _settingsStore.Current.FFmpegPath;
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                throw new OperationFailedException("FFmpeg executable is not configured in Settings.");
            }

            if (BgmTracks.FirstOrDefault(track => track.Name == trackName)?.DraftFilePath is not { } sourcePath || !File.Exists(sourcePath))
            {
                throw new OperationFailedException($"No draft audio file found for '{trackName}' to convert.");
            }

            string targetWavPath = Path.ChangeExtension(sourcePath, ".wav");
            _session.Status = $"Converting {trackName}...";
            AudioConversionResult result = await _audioConverter.ConvertToConsoleWavAsync(ffmpegPath, sourcePath, targetWavPath, BgmChannels);
            if (!result.Success)
            {
                throw new IOException($"Conversion failed: {result.ErrorMessage}");
            }

            // the converted WAV replaces the original file
            if (!sourcePath.Equals(targetWavPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(sourcePath);
            }

            format = result.ConvertedInfo?.FormatDescription;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = $"Converted '{trackName}' to WAV ({format}).";
        }
    }

    private async Task ChangeTrackAsync(string failure, Action change, string status)
    {
        if (await _errorHandler.TryRunAsync(failure, () =>
        {
            change();
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = status;
        }
    }

    private void SaveOption(bool changed, AppSettings settings)
    {
        if (!changed)
        {
            return;
        }

        _settingsStore.Save(settings);
        OnPropertyChanged(nameof(IncludeBgm));
        OnPropertyChanged(nameof(ConvertBgmAudio));
        OnPropertyChanged(nameof(BgmChannels));
        OnPropertyChanged(nameof(IsBgmStereo));
        OnPropertyChanged(nameof(IsBgmMono));
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Reload()
    {
        string? selectedName = SelectedBgmTrack?.Name;
        BgmTracks = _session.Draft is { } draft ? _assetManager.GetBgmTracks(draft.DraftPath, _session.BaseUiPath) : [];
        SelectedBgmTrack = BgmTracks.FirstOrDefault(track => track.Name == selectedName)
            ?? (BgmTracks.Count > 0 ? BgmTracks[0] : null);
    }
}
