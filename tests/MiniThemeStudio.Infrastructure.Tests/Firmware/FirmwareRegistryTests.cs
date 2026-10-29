using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Infrastructure.Firmware;

namespace MiniThemeStudio.Infrastructure.Tests.Firmware;

public sealed class FirmwareRegistryTests
{
    [Fact]
    public void BuiltInRegistryListsEveryDistinctBaseUi()
    {
        var registry = FirmwareRegistry.CreateDefault();

        Assert.Equal(
            [
                "famicom-jpn-1.0.5",
                "nes-usa-eur-1.0.2",
                "shonen-jpn-3.0.1",
                "snes-eur-2.0.13",
                "snes-eur-2.0.7",
                "snes-jpn-2.0.12",
                "snes-usa-2.0.13",
                "snes-usa-2.0.7",
            ],
            registry.All.Select(definition => definition.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["2.0.13", "2.0.14"], registry.FindById("snes-eur-2.0.13")!.FirmwareVersions);
        Assert.Equal(10, registry.FindById("shonen-jpn-3.0.1")!.EngineFormat);
    }

    [Fact]
    public void FindsSourceByHashRegardlessOfCase()
    {
        var registry = new FirmwareRegistry([CreateDefinition("snes-eur-2.0.13", new string('a', 64), new string('b', 64))]);

        (FirmwareDefinition Definition, FirmwareSource Source)? match = registry.FindBySourceSha256(new string('B', 64));

        Assert.NotNull(match);
        Assert.Equal("snes-eur-2.0.13", match.Value.Definition.Id);
        Assert.Null(registry.FindBySourceSha256(new string('c', 64)));
    }

    [Fact]
    public void UserDefinitionsAddAndOverrideBuiltInBases()
    {
        string rootPath = Directory.CreateTempSubdirectory().FullName;
        string userPath = Path.Combine(rootPath, "firmwares.json");
        File.WriteAllText(userPath, $$"""
            {
              "schemaVersion": 1,
              "bases": [
                {
                  "id": "snes-eur-2.0.99", "console": "snes", "region": "eur", "version": "2.0.99",
                  "displayName": "Future SNES", "uiPath": "usr/share/ui/snes-eur", "engineFormat": 9,
                  "uiTreeSha256": "{{new string('d', 64)}}", "uiFileCount": 1,
                  "firmwareVersions": ["2.0.99"],
                  "sources": [{ "fileName": "x.hsqs", "format": "squashfs", "size": 1, "sha256": "{{new string('e', 64)}}", "firmwareVersion": "2.0.99" }]
                }
              ]
            }
            """);

        try
        {
            var registry = FirmwareRegistry.CreateDefault(userPath);

            Assert.Equal(9, registry.All.Count);
            Assert.Equal("Future SNES", registry.FindBySourceSha256(new string('e', 64))!.Value.Definition.DisplayName);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void RejectsMalformedHashes()
    {
        Assert.Throws<InvalidDataException>(() =>
            new FirmwareRegistry([CreateDefinition("bad", "not-a-hash", new string('b', 64))]));
    }

    internal static FirmwareDefinition CreateDefinition(
        string id,
        string uiTreeSha256,
        string sourceSha256,
        string format = "tar.gz",
        string uiPath = "usr/share/ui/snes-eur",
        string console = "snes",
        string region = "eur")
    {
        return new FirmwareDefinition(
            id,
            console,
            region,
            "2.0.13",
            "SNES Classic Mini (Europe)",
            uiPath,
            9,
            uiTreeSha256,
            3,
            ["2.0.13"],
            [new FirmwareSource("dp-sneseur-release-v2.0.13.tar.gz", format, 1, sourceSha256, "2.0.13")]);
    }
}
