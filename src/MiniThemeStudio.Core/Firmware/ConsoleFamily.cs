namespace MiniThemeStudio.Core.Firmware;

public enum ConsoleFamily
{
    NesFamicom,
    Snes,
    Shonen,
}

public static class ConsoleFamilies
{
    public static IReadOnlyList<ConsoleFamily> All { get; } = [ConsoleFamily.NesFamicom, ConsoleFamily.Snes, ConsoleFamily.Shonen];

    public static ConsoleFamily Of(FirmwareDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.Console.ToLowerInvariant() switch
        {
            "snes" => ConsoleFamily.Snes,
            "shonen" => ConsoleFamily.Shonen,
            _ => ConsoleFamily.NesFamicom,
        };
    }

    public static string DisplayName(ConsoleFamily family) => family switch
    {
        ConsoleFamily.NesFamicom => "NES / Famicom",
        ConsoleFamily.Snes => "SNES / Super Famicom",
        ConsoleFamily.Shonen => "Shonen Jump",
        _ => family.ToString(),
    };

    public static string PackagingSystem(ConsoleFamily family) => family switch
    {
        ConsoleFamily.NesFamicom => "nes",
        ConsoleFamily.Snes => "snes",
        ConsoleFamily.Shonen => "shonen",
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    public static string ShortLabel(FirmwareDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return $"{DisplayName(Of(definition))} · {definition.Region.ToUpperInvariant()} · v{definition.Version}";
    }
}
