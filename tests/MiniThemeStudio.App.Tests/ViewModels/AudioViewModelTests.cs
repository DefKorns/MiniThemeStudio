using Microsoft.Extensions.DependencyInjection;
using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Infrastructure.Audio;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class AudioViewModelTests : IDisposable
{
    private readonly TestApp _app = new();

    public AudioViewModelTests()
    {
        var basePath = _app.CreateBase();
        TestApp.WriteBytes(basePath, "resources/sounds/hvc/bgm_home.wav", WavAudioReader.CreateMinimalPcm16Wav(44100, 2, 441));
        _app.Window.OpenTheme(_app.CreateTheme(), basePath);
    }

    public void Dispose() => _app.Dispose();

    private string SoundsPath => Path.Combine(_app.Window.CurrentDraftPath!, "resources", "sounds", "hvc");

    [Fact]
    public void ListsTheHomeAndBootMusic()
    {
        var audio = _app.Window.AudioTab;

        Assert.Equal(["bgm_home", "bgm_boot"], audio.BgmTracks.Select(track => track.Name));
        Assert.Equal("bgm_home", audio.SelectedBgmTrack?.Name);
        Assert.True(audio.BgmTracks[0].ExistsInBase);
        Assert.False(audio.BgmTracks[0].ExistsInDraft);
    }

    [Fact]
    public async Task AReplacedTrackCanBeRevertedToTheBase()
    {
        var audio = _app.Window.AudioTab;
        var replacement = Path.Combine(_app.Root, "song.wav");
        File.WriteAllBytes(replacement, WavAudioReader.CreateMinimalPcm16Wav(44100, 2, 441));
        _app.Files.Paths.Enqueue(replacement);

        await audio.ReplaceTrackCommand.ExecuteAsync("bgm_home");

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.True(File.Exists(Path.Combine(SoundsPath, "bgm_home.wav")));
        Assert.True(audio.SelectedBgmTrack?.ExistsInDraft);
        Assert.Equal("Replaced BGM 'bgm_home' with 'song.wav'.", _app.Window.Status);

        await audio.RevertTrackCommand.ExecuteAsync("bgm_home");

        Assert.False(File.Exists(Path.Combine(SoundsPath, "bgm_home.wav")));
        Assert.False(audio.SelectedBgmTrack?.ExistsInDraft);
    }

    [Fact]
    public async Task SilencingTheBootMusicWritesAnEmptyWav()
    {
        var audio = _app.Window.AudioTab;

        await audio.MuteTrackCommand.ExecuteAsync("bgm_boot");

        Assert.True(File.Exists(Path.Combine(SoundsPath, "bgm_boot.wav")));
        Assert.Equal("Silenced boot music 'bgm_boot'.", _app.Window.Status);
        Assert.True(_app.Window.HasUnsavedChanges);
    }

    [Fact]
    public async Task ConvertingWithoutFFmpegSaysWhatIsMissing()
    {
        var audio = _app.Window.AudioTab;

        await audio.ConvertTrackCommand.ExecuteAsync("bgm_home");

        var (exception, failure) = Assert.Single(_app.Errors.Failures);
        Assert.Equal("Cannot convert bgm_home", failure);
        Assert.Equal("FFmpeg executable is not configured in Settings.", exception.Message);
    }

    [Fact]
    public void TheOptionsAreSavedAsAppSettingsAndValidateTheThemeAgain()
    {
        var audio = _app.Window.AudioTab;
        var settings = _app.Services.GetRequiredService<IAppSettingsStore>();
        var changes = 0;
        audio.OptionsChanged += (_, _) => changes++;

        audio.IsBgmMono = true;
        audio.IsBgmMono = true;
        audio.IncludeBgm = false;

        Assert.Equal(2, changes);
        Assert.Equal(BgmAudioChannels.Mono, settings.Current.BgmChannels);
        Assert.False(settings.Current.IncludeBgm);
        Assert.False(audio.IsBgmStereo);
        Assert.False(audio.ExportOptions.IncludeBgm);
        Assert.Equal(_app.Window.ValidationTab.ValidationSummary, _app.Window.Status);
    }
}
