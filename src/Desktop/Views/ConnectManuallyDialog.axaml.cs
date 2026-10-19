using Avalonia.Controls;

namespace OmniConnect.Desktop.Views;

public partial class ConnectManuallyDialog : Window
{
    public string IpAddress => IpTextBox.Text?.Trim() ?? "";
    public int Port => int.TryParse(PortTextBox.Text?.Trim(), out var p) ? p : 5001;
    public string DeviceName => NameTextBox.Text?.Trim() ?? "Nearby Device";

    public ConnectManuallyDialog()
    {
        InitializeComponent();
        ConnectButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(IpAddress))
            {
                return;
            }
            Close(true);
        };
        CancelButton.Click += (_, _) => Close(false);
        IpTextBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) ConnectButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); };
        PortTextBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) ConnectButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); };
    }
}