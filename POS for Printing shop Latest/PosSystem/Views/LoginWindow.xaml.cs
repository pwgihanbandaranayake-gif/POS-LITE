using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;

namespace PosSystem.Views;

public partial class LoginWindow : Window
{
    public LoginWindow() => InitializeComponent();
    private void DragWindow(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void CloseClick(object s, RoutedEventArgs e) => Close();
    private void UserKeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Pwd.Focus(); }

    /// <summary>English / සිංහල / தமிழ் : reopen this window in the chosen language.</summary>
    private void LanguageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string code || code == Lang.Current) return;
        Lang.Set(code);
        App.ShowLogin();
        Close();
    }
}
