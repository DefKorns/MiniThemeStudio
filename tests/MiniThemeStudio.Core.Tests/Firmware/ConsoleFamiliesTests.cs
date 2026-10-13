using MiniThemeStudio.Core.Firmware;

namespace MiniThemeStudio.Core.Tests.Firmware;

public sealed class ConsoleFamiliesTests
{
    [Theory]
    [InlineData("nes", ConsoleFamily.NesFamicom)]
    [InlineData("famicom", ConsoleFamily.NesFamicom)]
    [InlineData("snes", ConsoleFamily.Snes)]
    [InlineData("shonen", ConsoleFamily.Shonen)]
    public void GroupsConsolesIntoThreeFamilies(string console, ConsoleFamily expected)
    {
        Assert.Equal(expected, ConsoleFamilies.Of(Definition(console, "jpn", "1.0.5")));
    }

    [Fact]
    public void ShortLabelShowsFamilyRegionAndVersion()
    {
        Assert.Equal("SNES · EUR · v2.0.13", ConsoleFamilies.ShortLabel(Definition("snes", "eur", "2.0.13")));
    }

    private static FirmwareDefinition Definition(string console, string region, string version)
    {
        return new FirmwareDefinition($"{console}-{region}-{version}", console, region, version, "x", "usr/share/ui/x", 9, new string('a', 64), 1, [version], []);
    }
}
