using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MiniThemeStudio.App.ViewModels;

// The time of an animation preview: moved by a slider, or played in a loop over Duration.
public sealed partial class AnimationClock : ObservableObject
{
    private readonly Stopwatch _clock = new();
    private DispatcherTimer? _timer;
    private double _playStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    private double time;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText), nameof(HasDuration))]
    [NotifyCanExecuteChangedFor(nameof(TogglePlayCommand))]
    private double duration;

    [ObservableProperty]
    private bool isPlaying;

    public bool HasDuration => Duration > 0;

    public string TimeText => HasDuration ? string.Create(CultureInfo.InvariantCulture, $"{Time:0.00} / {Duration:0.00} s") : "";

    // back to the start, stopped, for another animation
    public void Reset(double duration)
    {
        Pause();
        Duration = Math.Max(0, duration);
        Time = 0;
    }

    public void Pause()
    {
        _timer?.Stop();
        _clock.Stop();
        IsPlaying = false;
    }

    [RelayCommand(CanExecute = nameof(HasDuration))]
    private void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
            return;
        }

        _clock.Restart();
        _playStart = Time >= Duration ? 0 : Time;
        _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(1.0 / 60), DispatcherPriority.Render, (_, _) => Advance());
        _timer.Start();
        IsPlaying = true;
    }

    private void Advance()
    {
        if (Duration <= 0)
        {
            Pause();
            return;
        }

        Time = (_playStart + _clock.Elapsed.TotalSeconds) % Duration;
    }
}
