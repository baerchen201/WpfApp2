using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace WpfApp2;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        running = true;
        player = new Player(this);
        globals = new _Globals(new _Player(player));
        currentCode = "Player.Move(Input)";
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / 30) };
        timer.Tick += OnTimerTick;
        timer.Start();
    }

    public bool running;
    private Editor? editorWindow;
    public string currentCode;
    private Script<object>? script;
    private readonly DispatcherTimer timer;
    private readonly Player player;
    private readonly _Globals globals;
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(1);

    [SuppressMessage("ReSharper", "MemberHidesStaticFromOuterClass")]
    public class _Globals(_Player player)
    {
        public readonly _Player Player = player;
        public Vector2 Input { get; internal set; }
    }

    public class _Player(Player player)
    {
        public float X
        {
            get => player.x;
            set => player.x = value;
        }

        public float Y
        {
            get => player.y;
            set => player.y = value;
        }

        public void Move(Vector2 movement) => player.Move(movement);

        public override string ToString() => $"Player(X:{X}, Y:{Y})";
    }

    public class Player
    {
        internal Player(MainWindow window)
        {
            this.window = window;
            Reset();
        }

        private readonly MainWindow window;

        internal void Reset()
        {
            x = 0f;
            y = 0f;
        }

        public void Move(Vector2 movement)
        {
            x = Math.Clamp(
                x + movement.X,
                0f,
                (float)(window.MainCanvas.ActualWidth - window.PlayerImage.ActualWidth)
            );
            y = Math.Clamp(
                y + movement.Y,
                0f,
                (float)(window.MainCanvas.ActualHeight - window.PlayerImage.ActualHeight)
            );
        }

        public float x;
        public float y;
    }

    private void OnTimerTick(object? sender, EventArgs e) => Update();

    private void Update()
    {
        if (!running)
            return;
        Console.WriteLine($"Frame: {DateTime.Now}");
        globals.Input = GetInput();
        if (script != null)
        {
            var task = script.RunAsync(globals);
            task.Wait(timeout);
        }
        else
            DefaultUpdate();

        Canvas.SetLeft(PlayerImage, player.x);
        Canvas.SetTop(PlayerImage, player.y);
    }

    private Vector2 GetInput()
    {
        if (!IsActive)
            return Vector2.Zero;

        bool up = Keyboard.IsKeyDown(Key.W) || Keyboard.IsKeyDown(Key.Up),
            left = Keyboard.IsKeyDown(Key.A) || Keyboard.IsKeyDown(Key.Left),
            down = Keyboard.IsKeyDown(Key.S) || Keyboard.IsKeyDown(Key.Down),
            right = Keyboard.IsKeyDown(Key.D) || Keyboard.IsKeyDown(Key.Right);

        float v = 0f,
            h = 0f;
        if (up ^ down)
            v = down ? 1f : -1f;
        if (left ^ right)
            h = right ? 1f : -1f;

        if (v != 0 && h != 0)
        {
            v = Math.Sign(v) * 0.71f;
            h = Math.Sign(h) * 0.71f;
        }

        return new Vector2(h, v);
    }

    private void DefaultUpdate()
    {
        globals.Player.Move(globals.Input);
    }

    public void Pause()
    {
        running = false;
        PauseButton.IsEnabled = false;
        ResumeButton.IsEnabled = true;
    }

    public void Resume()
    {
        running = true;
        PauseButton.IsEnabled = true;
        ResumeButton.IsEnabled = false;
    }

    public void OpenEditor()
    {
        if (editorWindow != null)
            AfterCloseEditor();
        EditButton.IsEnabled = false;
        editorWindow = new Editor(this) { Owner = this };
        editorWindow.Closed += OnCloseEditor;
        editorWindow.Input.Text = currentCode;
        editorWindow.reactToChanges = true;
        editorWindow.Show();
    }

    private bool CompilationError(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(i => i.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length == 0)
            return false;
        MessageBox.Show(
            this,
            string.Join("\n", errors.Select(i => i.ToString())),
            "Compilation error",
            MessageBoxButton.OK,
            MessageBoxImage.Error
        );
        return true;
    }

    private void OnCloseEditor(object? sender, EventArgs e)
    {
        if (editorWindow is { changed: true })
            Recompile(editorWindow.Input.Text);
        AfterCloseEditor();
    }

    private void Recompile(string code)
    {
        var wasRunning = running;
        running = false;
        PauseButton.IsEnabled = false;
        ResumeButton.IsEnabled = false;
        EditButton.IsEnabled = false;

        currentCode = code;
        Console.WriteLine($"Code updated: {code}");
        var popup = new CompilingPopup { Owner = this };
        popup.Show();
        _Compile(code);
        popup.cancel = false;
        popup.Close();

        EditButton.IsEnabled = editorWindow == null;
        if (wasRunning)
            Resume();
        else
            Pause();
    }

    public void _Compile(string code)
    {
        var _script = CSharpScript.Create(
            code,
            ScriptOptions
                .Default.WithReferences(GetType().Assembly)
                .WithImports("System", "System.Numerics"),
            typeof(_Globals)
        );
        if (!CompilationError(_script.Compile()))
            script = _script;
    }

    private void AfterCloseEditor()
    {
        EditButton.IsEnabled = true;
        editorWindow?.Close();
        editorWindow = null;
    }

    private void PauseButton_OnClick(object sender, RoutedEventArgs e) => Pause();

    private void ResumeButton_OnClick(object sender, RoutedEventArgs e) => Resume();

    private void EditButton_OnClick(object sender, RoutedEventArgs e) => OpenEditor();
}
