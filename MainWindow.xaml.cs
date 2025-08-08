using System.Diagnostics.CodeAnalysis;
using System.Media;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
        player = new Player(this, PlayerImage);
        globals = new _Globals(this, player, Finish);
        currentCode =
            "Player.Move(Input);\nif (Keyboard.IsKeyDown(Key.Space))\n    Finish.Activate();";
        mediaplayer = new SoundPlayer();
        PlaySound();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / 30) };
        timer.Tick += OnTimerTick;
        timer.Start();
    }

    private void PlaySound()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/music.wav"));
        if (stream == null)
            return;
        mediaplayer.Stream = stream.Stream;
        mediaplayer.Play();
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
    public class _Globals(MainWindow window, Player player, FrameworkElement finish)
    {
        public readonly _Player Player = new(player);
        public readonly _ToggleableElement Finish = new(window, finish, _updateFinish);
        public Vector2 Input { get; internal set; }
    }

    public static void _updateFinish(FrameworkElement finish, bool active)
    {
        ((Rectangle)finish).Fill = new SolidColorBrush(active ? Colors.Lime : Colors.DarkRed);
    }

    public abstract class _Updatable(MainWindow window)
    {
        internal virtual void Update() { }

        internal virtual void PostUpdate() { }
    }

    public class _Element : _Updatable
    {
        internal _Element(MainWindow window, FrameworkElement element)
            : base(window)
        {
            this.element = element;
        }

        private readonly FrameworkElement element;

        internal FrameworkElement Element => element;
    }

    internal List<_Updatable> updates = [];
    private readonly SoundPlayer mediaplayer;

    public class _ToggleableElement : _Element
    {
        internal _ToggleableElement(
            MainWindow window,
            FrameworkElement element,
            Action<FrameworkElement, bool> update
        )
            : base(window, element)
        {
            this.update = update;
            window.updates.Add(this);
        }

        private bool active;
        public bool Active => active;
        private readonly Action<FrameworkElement, bool> update;

        internal override void Update() => update(Element, active);

        internal override void PostUpdate() => active = false;

        public void Activate() => active = true;
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

        public bool Colliding(_Element other) => player.Colliding(other.Element);

        public bool Colliding(Vector2 otherPosition, Vector2 otherSize) =>
            player.Colliding(otherPosition, otherSize);

        public override string ToString() => $"Player(X:{X}, Y:{Y})";
    }

    public class Player
    {
        internal Player(MainWindow window, FrameworkElement playerElement)
        {
            this.window = window;
            this.playerElement = playerElement;
            Reset();
        }

        private readonly MainWindow window;
        private readonly FrameworkElement playerElement;

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

        public bool Colliding(FrameworkElement other)
        {
            double left = Canvas.GetLeft(other),
                right = Canvas.GetRight(other),
                top = Canvas.GetTop(other),
                bottom = Canvas.GetBottom(other);
            double oX = double.IsNaN(left)
                    ? double.IsNaN(right)
                        ? 0d
                        : ((Canvas)other.Parent).ActualWidth - right - other.ActualWidth
                    : left,
                oY = double.IsNaN(top)
                    ? double.IsNaN(bottom)
                        ? 0d
                        : ((Canvas)other.Parent).ActualHeight - bottom - other.ActualHeight
                    : top;
            return Colliding(
                new Vector2((float)oX, (float)oY),
                new Vector2((float)other.ActualWidth, (float)other.ActualHeight)
            );
        }

        public bool Colliding(Vector2 otherPosition, Vector2 otherSize)
        {
            float mX = x + (float)playerElement.ActualWidth,
                mY = y + (float)playerElement.ActualHeight,
                oX = otherPosition.X,
                oY = otherPosition.Y,
                omX = oX + otherSize.X,
                omY = oY + otherSize.Y;

            return !(mX <= oX || omX <= x || mY <= oY || omY <= y);
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

        updates.ForEach(i => i.Update());
        if (globals.Finish.Active && player.Colliding(Finish))
            GameEnd();
        updates.ForEach(i => i.PostUpdate());
        Canvas.SetLeft(PlayerImage, player.x);
        Canvas.SetTop(PlayerImage, player.y);
    }

    private void GameEnd()
    {
        timer.Stop();
        running = false;
        PauseButton.IsEnabled = false;
        ResumeButton.IsEnabled = false;
        EditButton.IsEnabled = false;
        if (editorWindow != null)
            editorWindow.changed = false;
        editorWindow?.Close();
        editorWindow = null;
        mediaplayer.Stop();
        MessageBox.Show(
            this,
            "You Win",
            "GameEnd",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
        Close();
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

        return new Vector2(h, v) * 2.5f;
    }

    private void DefaultUpdate()
    {
        globals.Player.Move(globals.Input);
        if (Keyboard.IsKeyDown(Key.Space))
            globals.Finish.Activate();
    }

    public void Pause()
    {
        running = false;
        PauseButton.IsEnabled = false;
        ResumeButton.IsEnabled = true;
        mediaplayer.Stop();
    }

    public void Resume()
    {
        running = true;
        PauseButton.IsEnabled = true;
        ResumeButton.IsEnabled = false;
        mediaplayer.Stop();
        if (musicEnabled)
            mediaplayer.PlayLooping();
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
                .WithImports("System", "System.Numerics", "System.Windows.Input"),
            typeof(_Globals)
        );
        if (!CompilationError(_script.Compile()))
            script = _script;
    }

    private void AfterCloseEditor()
    {
        EditButton.IsEnabled = true;
        if (editorWindow != null)
            editorWindow.changed = false;
        editorWindow?.Close();
        editorWindow = null;
        Focus();
    }

    private void PauseButton_OnClick(object sender, RoutedEventArgs e) => Pause();

    private void ResumeButton_OnClick(object sender, RoutedEventArgs e) => Resume();

    private void EditButton_OnClick(object sender, RoutedEventArgs e) => OpenEditor();

    private bool musicEnabled = true;

    public void ToggleMusic(bool enable)
    {
        musicEnabled = enable;
        if (running)
            Resume();
    }

    private void MuteButton_OnChecked(object sender, RoutedEventArgs e) => ToggleMusic(false);

    private void MuteButton_OnUnchecked(object sender, RoutedEventArgs e) => ToggleMusic(true);
}
