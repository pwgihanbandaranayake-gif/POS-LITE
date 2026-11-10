using System.Windows.Controls;
using System.Windows.Input;

namespace PosSystem.Views.Dialogs;

public partial class ItemSearchView : UserControl
{
    public ItemSearchView() => InitializeComponent();
    private ItemSearchViewModel? Vm => DataContext as ItemSearchViewModel;

    private void SearchKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        switch (e.Key)
        {
            case Key.Down: Vm.Move(1); Scroll(); e.Handled = true; break;
            case Key.Up: Vm.Move(-1); Scroll(); e.Handled = true; break;
            case Key.PageDown: Vm.Move(10); Scroll(); e.Handled = true; break;
            case Key.PageUp: Vm.Move(-10); Scroll(); e.Handled = true; break;
            case Key.Enter: Vm.ChooseCommand.Execute(null); e.Handled = true; break;
        }
    }

    private void Scroll() { if (Grid.SelectedItem != null) Grid.ScrollIntoView(Grid.SelectedItem); }
    private void GridDoubleClick(object sender, MouseButtonEventArgs e) => Vm?.ChooseCommand.Execute(null);
}
