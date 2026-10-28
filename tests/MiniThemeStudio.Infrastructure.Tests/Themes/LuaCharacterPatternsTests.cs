using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class LuaCharacterPatternsTests
{
    [Fact]
    public void ReadsTheCharactersTheScriptDraws()
    {
        var patterns = LuaCharacterPatterns.Read(CharacterPatternFixtures.SampleBytes);

        Assert.Equal(["smb_mario", "smb_block"], patterns.Characters.Select(character => character.Name));
        var mario = patterns.Characters[0];
        Assert.Equal(("smb_tex", 16, 16), (mario.Texture, mario.Width, mario.Height));
        Assert.Equal([(0, 1), (18, 1)], mario.Frames);
        Assert.Equal([(0, 35)], patterns.Characters[1].Frames);
    }

    [Fact]
    public void ChangedNumbersGiveTheBytesLuaJitCompilesFromTheEditedSource()
    {
        Assert.Equal(CharacterPatternFixtures.SampleBytes, LuaCharacterPatterns.Read(CharacterPatternFixtures.SampleBytes).ToBytes());

        var patterns = LuaCharacterPatterns.Read(CharacterPatternFixtures.SampleBytes);
        patterns.Set("smb_mario", 28, 36, [(0, 1), (300, 1)]);

        Assert.Equal(CharacterPatternFixtures.SampleEditedBytes, patterns.ToBytes());
    }

    [Fact]
    public void ACharacterKeepsItsNumberOfPictures()
    {
        var patterns = LuaCharacterPatterns.Read(CharacterPatternFixtures.SampleBytes);

        Assert.Throws<ArgumentException>(() => patterns.Set("smb_block", 16, 16, [(0, 0), (18, 0)]));
        Assert.Throws<KeyNotFoundException>(() => patterns.Set("smb_luigi", 16, 16, []));
    }
}
