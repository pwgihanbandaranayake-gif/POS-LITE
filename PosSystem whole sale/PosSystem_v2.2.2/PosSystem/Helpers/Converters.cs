using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PosSystem.Helpers;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is Visibility v && v == Visibility.Visible ^ Invert;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object? value, Type t, object p, CultureInfo c)
    {
        bool has = value != null && !(value is string s && s.Length == 0);
        if (Invert) has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class NonZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is decimal d && d != 0 ? Visibility.Visible : value is int i && i != 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => !(value is bool b && b);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => !(value is bool b && b);
}

/// <summary>"#RRGGBB" string to brush (touch tiles). Falls back to leaf green.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object p, CultureInfo c)
    {
        try
        {
            if (value is string s && s.StartsWith("#")) return new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));
        }
        catch { }
        return new SolidColorBrush(Color.FromRgb(0x1F, 0x6B, 0x4A));
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class EqualsToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => Equals(value?.ToString(), p?.ToString());
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
    {
        if (value is not bool b || !b) return Binding.DoNothing;
        if (t.IsEnum) return Enum.Parse(t, p.ToString()!);
        var target = Nullable.GetUnderlyingType(t) ?? t;
        return System.Convert.ChangeType(p, target, CultureInfo.InvariantCulture);
    }
}
