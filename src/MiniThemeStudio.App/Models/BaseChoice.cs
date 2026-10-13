using MiniThemeStudio.Core.Firmware;

namespace MiniThemeStudio.App.Models;

public sealed record BaseChoice(InstalledBaseInfo Base, string Label)
{
    public ConsoleFamily Family => ConsoleFamilies.Of(Base.Definition);
}

public sealed record FamilyChoice(ConsoleFamily Family, string Label);
