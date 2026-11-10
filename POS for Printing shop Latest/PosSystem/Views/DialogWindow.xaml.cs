using System.Windows;
using System.Windows.Input;

namespace PosSystem.Views;

public partial class DialogWindow : Window
{
    public DialogWindow() => InitializeComponent();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is DialogViewModel vm)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
