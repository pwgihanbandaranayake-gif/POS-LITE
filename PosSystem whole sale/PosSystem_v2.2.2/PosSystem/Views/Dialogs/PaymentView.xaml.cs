using System.Windows.Controls;
using System.Windows.Input;

namespace PosSystem.Views.Dialogs;

public partial class PaymentView : UserControl
{
    public PaymentView() => InitializeComponent();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (DataContext is not PaymentViewModel vm) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F10) { vm.CompleteCommand.Execute(null); e.Handled = true; return; }
        if (key >= Key.F1 && key <= Key.F9)
        {
            vm.SelectByKey(key.ToString());
            vm.AmountText = vm.Balance.ToString("0.00");
            AmountBox.Focus(); AmountBox.SelectAll();
            e.Handled = true;
        }
    }
}
