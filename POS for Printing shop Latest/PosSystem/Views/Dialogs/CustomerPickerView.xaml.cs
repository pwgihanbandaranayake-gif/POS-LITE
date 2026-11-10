using System.Windows.Controls;
using System.Windows.Input;
namespace PosSystem.Views.Dialogs;
public partial class CustomerPickerView : UserControl
{
    public CustomerPickerView() => InitializeComponent();
    private void Pick(object s, MouseButtonEventArgs e) => (DataContext as CustomerPickerViewModel)?.ChooseCommand.Execute(null);
}
