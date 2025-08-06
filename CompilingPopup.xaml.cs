using System.ComponentModel;
using System.Windows;

namespace WpfApp2;

public partial class CompilingPopup : Window
{
    public CompilingPopup()
    {
        InitializeComponent();
    }

    public bool cancel = true;

    private void CompilingPopup_OnClosing(object? sender, CancelEventArgs e) => e.Cancel = cancel;
}
