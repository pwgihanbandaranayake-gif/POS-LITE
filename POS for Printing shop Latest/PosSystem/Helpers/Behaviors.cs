using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PosSystem.Helpers;

/// <summary>Focus an element (and select its text) when it is loaded.</summary>
public static class FocusOnLoad
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(FocusOnLoad), new PropertyMetadata(false, (d, e) =>
        {
            if (d is FrameworkElement fe && e.NewValue is true)
                fe.Loaded += (_, _) => fe.Dispatcher.BeginInvoke(() =>
                {
                    fe.Focus(); Keyboard.Focus(fe);
                    if (fe is TextBox tb) tb.SelectAll();
                }, System.Windows.Threading.DispatcherPriority.Input);
        }));
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
}

/// <summary>Writes PasswordBox.Password into the named string property of the DataContext (PasswordBox cannot be bound).</summary>
public static class PasswordTarget
{
    public static readonly DependencyProperty PropertyProperty = DependencyProperty.RegisterAttached(
        "Property", typeof(string), typeof(PasswordTarget), new PropertyMetadata(null, (d, e) =>
        {
            if (d is PasswordBox pb && e.NewValue is string name)
                pb.PasswordChanged += (_, _) => pb.DataContext?.GetType().GetProperty(name)?.SetValue(pb.DataContext, pb.Password);
        }));
    public static void SetProperty(DependencyObject d, string v) => d.SetValue(PropertyProperty, v);
    public static string GetProperty(DependencyObject d) => (string)d.GetValue(PropertyProperty);
}

/// <summary>Auto-generated DataGrid columns for DataTable results: hide Id, format money, right-align numbers.</summary>
public static class GridAuto
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(GridAuto), new PropertyMetadata(false, (d, e) =>
        {
            if (d is not DataGrid g || e.NewValue is not true) return;
            g.AutoGenerateColumns = true;
            g.AutoGeneratingColumn += (_, a) =>
            {
                if (a.PropertyName == "Id") { a.Cancel = true; return; }
                a.Column.Header = Lang.T(a.PropertyName);
                if (a.Column is DataGridTextColumn col)
                {
                    // indexer path: column names like "Qty (+/-)" or "Rate %" break normal property paths
                    var b = new Binding("[" + a.PropertyName + "]") { Mode = BindingMode.OneWay };
                    var t = Nullable.GetUnderlyingType(a.PropertyType) ?? a.PropertyType;
                    if (t == typeof(decimal) || t == typeof(double))
                    {
                        b.StringFormat = "N2";
                        col.ElementStyle = (Style)System.Windows.Application.Current.Resources["RightCell"];
                    }
                    else if (t == typeof(int) || t == typeof(long))
                        col.ElementStyle = (Style)System.Windows.Application.Current.Resources["RightCell"];
                    else if (t == typeof(DateTime))
                        b.StringFormat = "yyyy-MM-dd HH:mm";
                    col.Binding = b;
                    col.SortMemberPath = a.PropertyName;
                }
            };
            g.LoadingRow += (_, a) =>
            {
                if (a.Row.Item is DataRowView rv && rv.Row.ItemArray.Any(v => v is string s && s == "TOTAL"))
                {
                    a.Row.FontWeight = FontWeights.Bold;
                    a.Row.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TagSoftBrush"];
                }
                else { a.Row.ClearValue(DataGridRow.FontWeightProperty); a.Row.ClearValue(DataGridRow.BackgroundProperty); }
            };
        }));
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
}

/// <summary>Lets DataGrid columns (not in the visual tree) bind to the view model.</summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register("Data", typeof(object), typeof(BindingProxy));
    public object? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
