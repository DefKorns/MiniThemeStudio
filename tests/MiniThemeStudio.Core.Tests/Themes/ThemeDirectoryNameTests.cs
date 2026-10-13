using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Core.Tests.Themes;

public sealed class ThemeDirectoryNameTests
{
    [Theory]
    [InlineData("Chrono Trigger", "chrono_trigger")]
    [InlineData("  Pokémon — Mini  ", "pokemon_mini")]
    [InlineData("Shonen__Jump 50th", "shonen_jump_50th")]
    public void FromDisplayNameCreatesSnakeCase(string displayName, string expected)
    {
        Assert.Equal(expected, ThemeDirectoryName.FromDisplayName(displayName));
    }

    [Fact]
    public void FromDisplayNameRejectsNamesWithoutLettersOrNumbers()
    {
        Assert.Throws<ArgumentException>(() => ThemeDirectoryName.FromDisplayName("---"));
    }
}
