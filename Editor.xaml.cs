using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WpfApp2;

public partial class Editor : Window
{
    public Editor(MainWindow window)
    {
        this.window = window;
        InitializeComponent();
    }

    public bool changed;
    public bool reactToChanges;
    private readonly MainWindow window;

    private void Input_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (reactToChanges)
            changed = true;
    }

    private void Input_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            Recompile(Input.Text);
    }

    private void Recompile(string code)
    {
        var wasRunning = window.running;
        window.running = false;
        window.PauseButton.IsEnabled = false;
        window.ResumeButton.IsEnabled = false;

        window.currentCode = code;
        Console.WriteLine($"Code updated: {code}");
        var popup = new CompilingPopup { Owner = window };
        popup.Show();
        window._Compile(code);
        popup.cancel = false;
        popup.Close();

        window.EditButton.IsEnabled = false;
        if (wasRunning)
            window.Resume();
        else
            window.Pause();
        changed = false;
    }

    private void Editor_OnActivated(object? sender, EventArgs e)
    {
        Input.Focus();
    }
}
