using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Core.Tests.Themes;

public sealed class ThemeMetadataResolverTests
{
    [Fact]
    public void ResolveNamePrefersTitle()
    {
        string name = ThemeMetadataResolver.ResolveName(
            "folder_name",
            " Chrono Trigger ",
            ["NAME=Legacy name"]);

        Assert.Equal("Chrono Trigger", name);
    }

    [Fact]
    public void ResolveNameUsesLegacyInfoName()
    {
        string name = ThemeMetadataResolver.ResolveName(
            "folder_name",
            null,
            ["AUTHOR=DefKorns", "name=Chrono Trigger [15.5Mb]"]);

        Assert.Equal("Chrono Trigger [15.5Mb]", name);
    }

    [Fact]
    public void ResolveNameFallsBackToDirectoryName()
    {
        string name = ThemeMetadataResolver.ResolveName("chrono_trigger", " ", []);

        Assert.Equal("chrono_trigger", name);
    }
}
