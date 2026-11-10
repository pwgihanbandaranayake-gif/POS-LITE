using System.Windows.Controls;
using System.Windows.Input;
namespace PosSystem.Views.Dialogs;
public partial class PricePickerView : UserControl
{
    public PricePickerView() => InitializeComponent();
    private void Pick(object s, MouseButtonEventArgs e) => (DataContext as PricePickerViewModel)?.ChooseCommand.Execute(null);
}
