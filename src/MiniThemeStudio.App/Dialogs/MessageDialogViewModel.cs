namespace MiniThemeStudio.App.Dialogs;

public sealed class MessageDialogViewModel(DialogRequest request)
{
    public string Title => request.Title;

    public string Message => request.Message;

    public string AcceptButtonText => request.AcceptButtonText;

    public string CancelButtonText => request.CancelButtonText;

    public bool IsDestructive => request.IsDestructive;

    public string AlternateButtonText => request.AlternateButtonText ?? "";

    public bool HasAlternate => request.AlternateButtonText is not null;

    public bool IsConfirmation => request.Category == DialogCategory.Confirmation;

    public bool IsDefault => request.Variant == DialogVariant.Default;

    public bool IsInfo => request.Variant == DialogVariant.Info;

    public bool IsSuccess => request.Variant == DialogVariant.Success;

    public bool IsWarning => request.Variant == DialogVariant.Warning;

    public bool IsError => request.Variant == DialogVariant.Error;
}
