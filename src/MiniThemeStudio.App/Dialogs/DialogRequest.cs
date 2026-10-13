namespace MiniThemeStudio.App.Dialogs;

public sealed record DialogRequest(
    string Title,
    string Message,
    DialogVariant Variant = DialogVariant.Default,
    DialogCategory Category = DialogCategory.Information,
    string AcceptButtonText = "OK",
    string CancelButtonText = "Go back",
    bool IsDestructive = false);
