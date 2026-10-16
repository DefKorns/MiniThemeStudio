using MiniThemeStudio.Core.Fonts;
using MiniThemeStudio.Infrastructure.Fonts;
using Xunit;

namespace MiniThemeStudio.Infrastructure.Tests.Fonts;

public class FontCharsetsTests
{
    [Fact]
    public void BaseHasAsciiAndAccentsButNoExtraScripts()
    {
        var chars = FontCharsets.Build(ExtraScripts.None);
        Assert.Contains('A', chars);
        Assert.Contains('ç', chars);
        Assert.Contains('©', chars);
        Assert.DoesNotContain('Ж', chars);
        Assert.DoesNotContain('Ω', chars);
        Assert.DoesNotContain('あ', chars);
    }

    [Theory]
    [InlineData(ExtraScripts.Cyrillic, 'Ж')]
    [InlineData(ExtraScripts.Greek, 'Ω')]
    [InlineData(ExtraScripts.Japanese, 'あ')]
    [InlineData(ExtraScripts.Japanese, '時')]
    public void EachScriptAddsItsCharacters(ExtraScripts script, char expected)
    {
        var chars = FontCharsets.Build(script);
        Assert.Contains(expected, chars);
        Assert.Contains('A', chars);
        Assert.Equal(chars.Count, chars.Distinct().Count());
    }
}
