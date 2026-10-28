namespace MiniThemeStudio.Core.Themes;

public sealed record ThemeValidationIssue(string Message, bool IsError);

public sealed record ThemeValidationResult(
    bool IsValid,
    IReadOnlyList<ThemeValidationIssue> Issues)
{
    public bool HasWarnings => Issues.Any(i => !i.IsError);
    public bool HasErrors => Issues.Any(i => i.IsError);
}
