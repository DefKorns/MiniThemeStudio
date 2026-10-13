using System.IO;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Infrastructure.Firmware;
using Xunit;

namespace MiniThemeStudio.Infrastructure.Tests.Firmware;

public sealed class FirmwareRegistryTests
{
    [Fact]
    public void BuiltInRegistryContainsAllReferenceReleases()
    {
        var registry = new FirmwareRegistry();
        Assert.True(registry.All.Count >= 11);

        Assert.NotNull(registry.FindById("dp-sneseur-v2.0.14"));
        Assert.NotNull(registry.FindById("dp-sneseur-v2.0.7"));
        Assert.NotNull(registry.FindById("dp-snesusa-v2.0.14"));
        Assert.NotNull(registry.FindById("dp-snesusa-v2.0.13"));
        Assert.NotNull(registry.FindById("dp-snesusa-v2.0.7"));
        Assert.NotNull(registry.FindById("dp-shvc-v2.0.12"));
        Assert.NotNull(registry.FindById("dp-nes-v1.0.2"));
        Assert.NotNull(registry.FindById("dp-nes-v1.0.3"));
        Assert.NotNull(registry.FindById("dp-nes-v1.0.7"));
        Assert.NotNull(registry.FindById("dp-hvc-v1.0.5"));
        Assert.NotNull(registry.FindById("dp-hvcj-v3.0.1"));
    }

    [Theory]
    [InlineData("dp-sneseur-release-v2.0.14-0-gd8b65c6.tar.gz", "dp-sneseur-v2.0.14")]
    [InlineData("dp-snesusa-release-v2.0.13-0-g9dca6c5.tar.gz", "dp-snesusa-v2.0.13")]
    [InlineData("dp-shvc-release-v2.0.12-0-gbff4fb3.tar.gz", "dp-shvc-v2.0.12")]
    [InlineData("dp-hvc-release-v1.0.5-0-g2f04d11.tar.gz", "dp-hvc-v1.0.5")]
    [InlineData("dp-nes-release-v1.0.2-0-g99e37e1.tar.gz", "dp-nes-v1.0.2")]
    [InlineData("dp-nes-release-v1.0.7-0-g4ea4041.tar", "dp-nes-v1.0.7")]
    public void MatchesArchivesByFilenameCorrectly(string fileName, string expectedId)
    {
        var registry = new FirmwareRegistry();
        var match = registry.MatchArchive(fileName);
        Assert.NotNull(match);
        Assert.Equal(expectedId, match.Id);
    }

    [Fact]
    public void ExtensibilityAllowsRegisteringCustomFirmwareWithoutCodeChanges()
    {
        var registry = new FirmwareRegistry();
        var custom = new FirmwareDefinition(
            "custom-genesis-v1.0",
            "genesis",
            "usa",
            "1.0.0",
            "Sega Genesis Mini v1.0",
            "genesis-*.tar.gz",
            "usr/share/ui",
            "/usr/share/ui",
            "/var/squashfs/usr/share/ui");

        registry.RegisterCustomDefinition(custom);
        Assert.NotNull(registry.FindById("custom-genesis-v1.0"));
        Assert.Equal(custom, registry.MatchArchive("genesis-release-v1.0.tar.gz"));
    }
}
