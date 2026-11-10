using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PosSystem.Views.Pos;

public partial class PosKeyboardView : UserControl
{
    public PosKeyboardView() => InitializeComponent();

    public void FocusScan()
    {
        Dispatcher.BeginInvoke(() => { ScanBox.Focus(); Keyboard.Focus(ScanBox); ScanBox.CaretIndex = ScanBox.Text.Length; }, DispatcherPriority.Input);
    }

    public bool ScanHasFocus => ScanBox.IsKeyboardFocusWithin;

    private void CartSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Cart.SelectedItem != null) Cart.ScrollIntoView(Cart.SelectedItem);
        if (IsVisible) FocusScan();   // keep the scanner input in the scan box
    }
}
