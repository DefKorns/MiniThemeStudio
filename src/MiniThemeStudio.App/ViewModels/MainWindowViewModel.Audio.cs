using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Infrastructure.Audio;

namespace MiniThemeStudio.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private IReadOnlyList<BgmTrack> bgmTracks = [];

    [ObservableProperty]
    private BgmTrack? selectedBgmTrack;

    public bool IncludeBgm
    {
        get => _settingsStore.Current.IncludeBgm;
        set
        {
            if (_settingsStore.Current.IncludeBgm != value)
            {
                _settingsStore.Save(_settingsStore.Current with { IncludeBgm = value });
                OnPropertyChanged();
                RunValidation();
            }
        }
    }

    public bool ConvertBgmAudio
    {
        get => _settingsStore.Current.ConvertBgmAudio;
        set
        {
            if (_settingsStore.Current.ConvertBgmAudio != value)
            {
                _settingsStore.Save(_settingsStore.Current with { ConvertBgmAudio = value });
                OnPropertyChanged();
                RunValidation();
            }
        }
    }

    public BgmAudioChannels BgmChannels
    {
        get => _settingsStore.Current.BgmChannels;
        set
        {
            if (_settingsStore.Current.BgmChannels != value)
            {
                _settingsStore.Save(_settingsStore.Current with { BgmChannels = value });
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBgmStereo));
                OnPropertyChanged(nameof(IsBgmMono));
                RunValidation();
            }
        }
    }

    public bool IsBgmStereo
    {
        get => BgmChannels == BgmAudioChannels.Stereo;
        set
        {
            if (value && BgmChannels != BgmAudioChannels.Stereo)
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
            if (value && BgmChannels != BgmAudioChannels.Mono)
            {
                BgmChannels = BgmAudioChannels.Mono;
            }
        }
    }

    public void ReloadBgmTracks()
    {
        if (_draft is null)
        {
            BgmTracks = [];
            SelectedBgmTrack = null;
            return;
        }

        var selectedName = SelectedBgmTrack?.Name;
        BgmTracks = _assetManager.GetBgmTracks(_draft.DraftPath, _currentBaseUiPath);
        SelectedBgmTrack = BgmTracks.FirstOrDefault(t => t.Name == selectedName)
            ?? (BgmTracks.Count > 0 ? BgmTracks[0] : null);
    }

    public bool ReplaceBgmTrack(string trackName, string sourceAudioFilePath)
    {
        if (_draft is null)
        {
            return false;
        }

        try
        {
            _assetManager.ReplaceBgmTrack(_draft.DraftPath, trackName, sourceAudioFilePath, _currentBaseUiPath);
            ReloadBgmTracks();
            HasUnsavedChanges = true;
            IsDraftSaved = false;
            RunValidation();
            Status = $"Replaced BGM '{trackName}' with '{Path.GetFileName(sourceAudioFilePath)}'.";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Failed to replace BGM: {ex.Message}";
            return false;
        }
    }

    public bool RevertBgmTrack(string trackName)
    {
        if (_draft is null)
        {
            return false;
        }

        try
        {
            _assetManager.RevertBgmTrack(_draft.DraftPath, trackName);
            ReloadBgmTracks();
            HasUnsavedChanges = true;
            IsDraftSaved = false;
            RunValidation();
            Status = $"Reverted BGM '{trackName}' to base.";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Failed to revert BGM: {ex.Message}";
            return false;
        }
    }

    public bool MuteBgmTrack(string trackName)
    {
        if (_draft is null)
        {
            return false;
        }

        try
        {
            _assetManager.MuteBgmTrack(_draft.DraftPath, trackName, _currentBaseUiPath);
            ReloadBgmTracks();
            HasUnsavedChanges = true;
            IsDraftSaved = false;
            RunValidation();
            Status = $"Silenced boot music '{trackName}'.";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Failed to silence BGM: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> ConvertBgmTrackNowAsync(string trackName)
    {
        if (_draft is null)
        {
            return false;
        }

        var ffmpegPath = _settingsStore.Current.FFmpegPath;
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            Status = "FFmpeg executable is not configured in Settings.";
            return false;
        }

        var track = BgmTracks.FirstOrDefault(t => t.Name == trackName);
        if (track?.DraftFilePath is not { } sourcePath || !File.Exists(sourcePath))
        {
            Status = $"No draft audio file found for '{trackName}' to convert.";
            return false;
        }

        var converter = new FFmpegAudioConverter();
        var targetWavPath = Path.ChangeExtension(sourcePath, ".wav");

        Status = $"Converting {trackName}...";
        var result = await converter.ConvertToConsoleWavAsync(ffmpegPath, sourcePath, targetWavPath, BgmChannels);

        if (!result.Success)
        {
            Status = $"Conversion failed: {result.ErrorMessage}";
            return false;
        }

        if (!sourcePath.Equals(targetWavPath, StringComparison.OrdinalIgnoreCase) && File.Exists(sourcePath))
        {
            try
            {
                File.Delete(sourcePath);
            }
            catch
            {
                // Ignore delete errors
            }
        }

        ReloadBgmTracks();
        HasUnsavedChanges = true;
        IsDraftSaved = false;
        RunValidation();
        Status = $"Converted '{trackName}' to WAV ({result.ConvertedInfo?.FormatDescription}).";
        return true;
    }
}
