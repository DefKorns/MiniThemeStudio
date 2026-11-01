using System.Reflection;
using Xunit.Sdk;

namespace MiniThemeStudio.Infrastructure.Tests;

/// <summary>
/// Files of the author's machine (firmware images, themes, fonts) some tests check against. A test that needs
/// one shows as skipped where it is missing, instead of passing without checking anything.
/// Paths may use environment variables, such as %APPDATA%.
/// </summary>
internal static class MachineFiles
{
    public static string Resolve(string path) => Environment.ExpandEnvironmentVariables(path);

    public static string? SkipReason(IEnumerable<string> paths)
    {
        string? missing = paths.Select(Resolve).FirstOrDefault(path => !File.Exists(path) && !Directory.Exists(path));
        return missing is null ? null : $"Needs {missing}, which is not on this machine.";
    }
}

/// <summary>A fact that is skipped when a file or folder it needs is missing.</summary>
public sealed class FactWhenPresentAttribute : FactAttribute
{
    public FactWhenPresentAttribute(params string[] paths)
    {
        Skip = MachineFiles.SkipReason(paths);
    }
}

/// <summary>A theory that is skipped when a file or folder all its rows need is missing.</summary>
public sealed class TheoryWhenPresentAttribute : TheoryAttribute
{
    public TheoryWhenPresentAttribute(params string[] paths)
    {
        Skip = MachineFiles.SkipReason(paths);
    }
}

/// <summary>
/// A theory row whose first argument is a file or folder, skipped when it is missing. The test receives the
/// path with its environment variables expanded, followed by the rest of the row.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class InlineDataWhenPresentAttribute : DataAttribute
{
    private readonly string _path;
    private readonly object[] _data;

    public InlineDataWhenPresentAttribute(string path, params object[] data)
    {
        _path = path;
        _data = data;
        Skip = MachineFiles.SkipReason([path]);
    }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod) => [[MachineFiles.Resolve(_path), .. _data]];
}
