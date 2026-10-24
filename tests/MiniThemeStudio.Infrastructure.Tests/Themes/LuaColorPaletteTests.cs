using System;
using System.IO;
using System.Linq;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;
using Xunit;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public class LuaColorPaletteTests
{
    // folders searched for colors_*.lua, separated by the platform's path separator
    private const string PalettesVariable = "MTS_TEST_PALETTE_DIRS";

    [Fact]
    public void ReadsColoursInSourceOrder()
    {
        var palette = LuaColorPalette.Read(PaletteFixtures.SampleBytes);

        Assert.Equal(
            [
                new PaletteColor("bg_base", 0.5, 0.25, 0.75, 1),
                new PaletteColor("font_home_hud", 1, 1, 1, 1),
                new PaletteColor("font_resume_title", 0, 0.2, 0.4, 0.6),
            ],
            palette.Colors);
    }

    [Fact]
    public void WritesUnchangedPaletteBackByteForByte()
    {
        var bytes = PaletteFixtures.SampleBytes;

        Assert.Equal(bytes, LuaColorPalette.Read(bytes).ToBytes());
    }

    [Fact]
    public void EditedPaletteMatchesWhatLuaJitCompilesFromTheEditedSource()
    {
        var palette = LuaColorPalette.Read(PaletteFixtures.SampleBytes);

        palette.SetColor(PaletteColor.FromBytes("font_home_hud", 190, 0, 26, 255));

        Assert.Equal(PaletteFixtures.SampleEditedBytes, palette.ToBytes());
    }

    [Fact]
    public void UnknownColourIsRejected()
    {
        var palette = LuaColorPalette.Read(PaletteFixtures.SampleBytes);

        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
            () => palette.SetColor(new PaletteColor("missing", 0, 0, 0, 1)));
    }

    [Fact]
    public void NotBytecodeIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => LuaColorPalette.Read("colors = {}"u8.ToArray()));
    }

    [Theory]
    [InlineData((byte)0, 0.0)]
    [InlineData((byte)21, 0.0823529)]
    [InlineData((byte)190, 0.745098)]
    [InlineData((byte)255, 1.0)]
    public void ChannelsUseSixSignificantDigitsLikeTheFirmware(byte value, double expected)
    {
        Assert.Equal(expected, PaletteColor.ChannelFromByte(value));
        Assert.Equal(value, PaletteColor.ChannelToByte(expected));
    }

    [Fact]
    public void PalettesInChosenFoldersRoundTrip()
    {
        var files = (Environment.GetEnvironmentVariable(PalettesVariable) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "colors_*.lua", SearchOption.AllDirectories))
            .Where(file => File.ReadAllBytes(file) is [0x1B, (byte)'L', (byte)'J', ..]) // skips decompiled sources
            .ToList();

        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            var palette = LuaColorPalette.Read(bytes);

            Assert.NotEmpty(palette.Colors);
            Assert.Equal(bytes, palette.ToBytes());
        }
    }
}
