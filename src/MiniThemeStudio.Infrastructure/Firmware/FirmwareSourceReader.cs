using System.Formats.Tar;
using System.IO.Compression;
using DiscUtils.SquashFs;
using SharpCompress.Compressors.Xz;

namespace MiniThemeStudio.Infrastructure.Firmware;

public enum FirmwareSourceFormat
{
    Unknown,
    SquashFs,
    Tar,
    TarGz,
}

public static class FirmwareSourceReader
{
    private static readonly SquashFileSystemReaderOptions SquashFsOptions = new()
    {
        GetDecompressor = (kind, _) => kind == SquashFileSystemCompressionKind.Xz
            ? compressed => new XZStream(compressed)
            : null!,
    };

    public static FirmwareSourceFormat Detect(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var header = new byte[262];
        var length = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        return header.AsSpan(0, length) switch
        {
            [0x68, 0x73, 0x71, 0x73, ..] => FirmwareSourceFormat.SquashFs,
            [0x1f, 0x8b, ..] => FirmwareSourceFormat.TarGz,
            var span when span.Length >= 262 && span[257..262].SequenceEqual("ustar"u8) => FirmwareSourceFormat.Tar,
            _ => FirmwareSourceFormat.Unknown,
        };
    }

    public static string FormatName(FirmwareSourceFormat format) => format switch
    {
        FirmwareSourceFormat.SquashFs => "squashfs",
        FirmwareSourceFormat.Tar => "tar",
        FirmwareSourceFormat.TarGz => "tar.gz",
        _ => "unknown",
    };

    public static int ExtractDirectory(
        string sourcePath,
        FirmwareSourceFormat format,
        string directoryInSource,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var prefix = directoryInSource.Trim('/') + "/";
        Directory.CreateDirectory(destinationPath);

        return format switch
        {
            FirmwareSourceFormat.SquashFs => ExtractFromSquashFs(sourcePath, prefix, destinationPath, cancellationToken),
            FirmwareSourceFormat.Tar or FirmwareSourceFormat.TarGz => ExtractFromTar(sourcePath, format, prefix, destinationPath, cancellationToken),
            _ => throw new InvalidDataException("Unsupported firmware source format."),
        };
    }

    private static int ExtractFromSquashFs(
        string sourcePath,
        string prefix,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(sourcePath);
        using var fileSystem = new SquashFileSystemReader(stream, SquashFsOptions);
        var nativeRoot = ToNative(prefix.TrimEnd('/'));
        if (!fileSystem.DirectoryExists(nativeRoot))
        {
            return 0;
        }

        var count = 0;
        foreach (var file in fileSystem.GetFiles(nativeRoot, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = file.Replace('\\', '/')[prefix.Length..];
            if (UiTreeHash.IsIgnored(relativePath))
            {
                continue;
            }

            using var input = fileSystem.OpenFile(file, FileMode.Open);
            WriteFile(destinationPath, relativePath, input);
            count++;
        }

        return count;
    }

    private static int ExtractFromTar(
        string sourcePath,
        FirmwareSourceFormat format,
        string prefix,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var fileStream = File.OpenRead(sourcePath);
        using var tarStream = format == FirmwareSourceFormat.TarGz
            ? new GZipStream(fileStream, CompressionMode.Decompress)
            : (Stream)fileStream;
        using var reader = new TarReader(tarStream);

        var count = 0;
        while (reader.GetNextEntry() is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                || entry.DataStream is null)
            {
                continue;
            }

            var entryName = entry.Name.Replace('\\', '/').TrimStart('.', '/');
            if (!entryName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = entryName[prefix.Length..];
            if (UiTreeHash.IsIgnored(relativePath))
            {
                continue;
            }

            WriteFile(destinationPath, relativePath, entry.DataStream);
            count++;
        }

        return count;
    }

    private static void WriteFile(string destinationRoot, string relativePath, Stream content)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot)) + Path.DirectorySeparatorChar;
        var targetPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!targetPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsafe path in firmware source: {relativePath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        using var output = File.Create(targetPath);
        content.CopyTo(output);
    }

    private static string ToNative(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}
