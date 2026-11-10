using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PosSystem.Views.Pos;

public partial class PosTouchView : UserControl
{
    public PosTouchView() => InitializeComponent();
    public void FocusScan() => Dispatcher.BeginInvoke(() => { ScanBox.Focus(); Keyboard.Focus(ScanBox); }, DispatcherPriority.Input);
}
