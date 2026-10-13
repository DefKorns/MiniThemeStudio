using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class BaseUiValidatorTests
{
    [Fact]
    public void ValidateAcceptsRequiredOriginalUiStructure()
    {
        var path = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(path, "resources"));
        File.WriteAllText(Path.Combine(path, "dependencies.json"), "{}");
        File.WriteAllText(Path.Combine(path, "init.json"), "{}");

        try
        {
            new BaseUiValidator().Validate(path);
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    [Fact]
    public void ValidateReportsAllMissingEntries()
    {
        var path = Directory.CreateTempSubdirectory().FullName;

        try
        {
            var exception = Assert.Throws<InvalidDataException>(
                () => new BaseUiValidator().Validate(path));

            Assert.Contains("resources/", exception.Message, StringComparison.Ordinal);
            Assert.Contains("dependencies.json", exception.Message, StringComparison.Ordinal);
            Assert.Contains("init.json", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }
}
