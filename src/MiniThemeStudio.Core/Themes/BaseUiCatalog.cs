namespace MiniThemeStudio.Core.Themes;

public static class BaseUiCatalog
{
    public static IReadOnlyList<BaseUiDefinition> All { get; } =
    [
        new("sneseur", "SNES Europe", "snes-eur", "/usr/share/ui/snes-eur", "/var/squashfs/usr/share/ui/snes-eur"),
        new("snesusa", "SNES USA", "snes-usa", "/usr/share/ui/snes-usa", "/var/squashfs/usr/share/ui/snes-usa"),
        new("snesjpn", "Super Famicom", "snes-jpn", "/usr/share/ui/shvc", "/var/squashfs/usr/share/ui/shvc"),
        new("nes", "NES", "nes", "/usr/share/clover-ui", "/var/squashfs/usr/share/clover-ui"),
        new("famicom", "Famicom", "famicom", "/usr/share/clover-ui", "/var/squashfs/usr/share/clover-ui"),
        new("shonen", "Shonen Jump", "shonen", "/usr/share/ui/hvc", "/var/squashfs/usr/share/ui/hvc"),
    ];
}
