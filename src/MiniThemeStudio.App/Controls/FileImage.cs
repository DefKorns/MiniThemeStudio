using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace MiniThemeStudio.App.Controls;

// An image loaded from a file. Bind Revision to a counter the view model bumps when the file is rewritten
// under the same path, so the image is read again.
public sealed class FileImage : Image, IDisposable
{
    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<FileImage, string?>(nameof(FilePath));

    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<FileImage, int>(nameof(Revision));

    private Bitmap? _bitmap;

    protected override Type StyleKeyOverride => typeof(Image);

    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public int Revision
    {
        get => GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilePathProperty || change.Property == RevisionProperty)
        {
            Load();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Release();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Load();
    }

    public void Dispose()
    {
        Release();
    }

    private void Load()
    {
        Release();
        if (FilePath is not { Length: > 0 } path || !File.Exists(path))
        {
            return;
        }

        try
        {
            // read through a stream so the file is not kept open
            using FileStream stream = File.OpenRead(path);
            _bitmap = new Bitmap(stream);
            Source = _bitmap;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _bitmap = null;
        }
    }

    private void Release()
    {
        Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
