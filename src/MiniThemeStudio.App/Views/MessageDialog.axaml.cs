using Avalonia.Controls;
using Avalonia.Interactivity;
using MiniThemeStudio.App.Dialogs;

namespace MiniThemeStudio.App.Views;

public sealed partial class MessageDialog : Window
{
    public MessageDialog() : this(new DialogRequest("Message", ""))
    {
    }

    public MessageDialog(DialogRequest request)
    {
        InitializeComponent();
        DataContext = new MessageDialogViewModel(request);
    }

    private void Accept_Click(object? sender, RoutedEventArgs e)
    {
        Close(DialogResult.Accepted);
    }

    private void Alternate_Click(object? sender, RoutedEventArgs e)
    {
        Close(DialogResult.Alternate);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(DialogResult.Cancelled);
    }
}
