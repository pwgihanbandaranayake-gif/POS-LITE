using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PosSystem.Views.Pos;

public partial class PosWindow : Window
{
    private readonly PosViewModel _vm;

    public PosWindow(PosViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.FocusScanRequested += FocusScan;
        vm.CloseRequested += () => Dispatcher.BeginInvoke(Close);
        Activated += (_, _) => FocusScan();
        Loaded += async (_, _) => await vm.LoadAsync();
        Closing += (_, e) =>
        {
            if (vm.HasLines) { e.Cancel = true; vm.ExitCommand.Execute(null); }
        };
    }

    private void FocusScan()
    {
        if (_vm.IsTouchMode) TouchView.FocusScan(); else KeyboardView.FocusScan();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // F10 is a Windows "system" key, so it never reaches a KeyBinding
        if (e.Key == Key.System && e.SystemKey == Key.F10) { _vm.PayCommand.Execute(null); e.Handled = true; return; }
        if (e.Key == Key.Delete && Keyboard.FocusedElement is TextBox tb && tb.Text.Length == 0) { _vm.VoidLineCommand.Execute(null); e.Handled = true; }
    }

    /// <summary>If a scanner types while focus is elsewhere (e.g. after a mouse click), send the characters to the scan box.</summary>
    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        _vm.ScanText += e.Text;
        FocusScan();
        e.Handled = true;
    }
}
