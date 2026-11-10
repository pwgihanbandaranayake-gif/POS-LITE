using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PosSystem.Views;
using PosSystem.Views.Pos;

namespace PosSystem;

/// <summary>
/// Start-up: read the connection string, show Login -> Shell (back office) -> POS window.
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledError;
        Lang.Load();                       // English / Sinhala / Tamil (saved per computer)

        Db.ConnectionString = ReadConnectionString();
        if (string.IsNullOrWhiteSpace(Db.ConnectionString))
        {
            MessageBox.Show("Set ConnectionStrings:PosDb in appsettings.json.", "POS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        ShowLogin();
    }

    /// <summary>appsettings.json -> { "ConnectionStrings": { "PosDb": "..." } }</summary>
    private static string ReadConnectionString()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty("ConnectionStrings").GetProperty("PosDb").GetString() ?? "";
        }
        catch (Exception ex)
        {
            MessageBox.Show("appsettings.json is missing or invalid.\n" + ex.Message, "POS", MessageBoxButton.OK, MessageBoxImage.Error);
            return "";
        }
    }

    public static void ShowLogin()
    {
        var vm = new LoginViewModel();
        var window = new LoginWindow { DataContext = vm };
        vm.LoggedIn += () =>
        {
            ShowShell();
            window.Close();
        };
        window.Closed += (_, _) =>
        {
            if (Current.Windows.Count == 0) Current.Shutdown();
        };
        Current.MainWindow = window;
        window.Show();
    }

    public static void ShowShell()
    {
        var vm = new ShellViewModel();
        var window = new ShellWindow { DataContext = vm };
        bool loggingOut = false;

        vm.LogoutRequested += () =>
        {
            loggingOut = true;
            foreach (var other in Current.Windows.OfType<Window>().Where(x => x != window).ToList()) other.Close();
            ShowLogin();
            window.Close();
        };
        vm.OpenPosRequested += () => OpenPos(window);
        window.Closed += (_, _) => { if (!loggingOut) Current.Shutdown(); };

        Current.MainWindow = window;
        window.Show();
        _ = vm.LoadAsync();
    }

    private static void OpenPos(Window owner)
    {
        var existing = Current.Windows.OfType<PosWindow>().FirstOrDefault();
        if (existing != null) { existing.Activate(); return; }
        new PosWindow(new PosViewModel()) { Owner = owner }.Show();
    }

    private void OnUnhandledError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "error.log"), $"{DateTime.Now:u} {e.Exception}\n\n"); }
        catch { }
        MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
