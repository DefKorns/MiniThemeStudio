using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Core.Tests.Themes;

public sealed class BaseUiCatalogTests
{
    [Fact]
    public void CatalogContainsConsolePathsAndLocalFolders()
    {
        Assert.Collection(
            BaseUiCatalog.All,
            item => Assert.Equal(("snes-eur", "/usr/share/ui/snes-eur", "/var/squashfs/usr/share/ui/snes-eur"), (item.LocalFolderName, item.ConsolePath, item.OriginalConsolePath)),
            item => Assert.Equal(("snes-usa", "/usr/share/ui/snes-usa", "/var/squashfs/usr/share/ui/snes-usa"), (item.LocalFolderName, item.ConsolePath, item.OriginalConsolePath)),
            item => Assert.Equal(("snes-jpn", "/usr/share/ui/shvc", "/var/squashfs/usr/share/ui/shvc"), (item.LocalFolderName, item.ConsolePath, item.OriginalConsolePath)),
            item => Assert.Equal(("nes", "/usr/share/clover-ui", "/var/squashfs/usr/share/clover-ui"), (item.LocalFolderName, item.ConsolePath, item.OriginalConsolePath)),
            item => Assert.Equal(("famicom", "/usr/share/clover-ui", "/var/squashfs/usr/share/clover-ui"), (item.LocalFolderName, item.ConsolePath, item.OriginalConsolePath)),
            item => Assert.Equal(("shonen", "/usr/share/ui/hvc", "/var/squashfs/usr/share/ui/hvc"), (item.LocalFolderName, item.ConsolePath, item.OriginalConsolePath)));
    }
}
