using System.Globalization;
using System.Windows.Data;

namespace ScreenKeeper.Views;

/// <summary>
/// Kehrt einen bool-Wert um (z. B. Knopf nicht anklickbar, solange sein Menü offen ist).
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        return value is bool flag ? !flag : true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        return value is bool flag ? !flag : false;
    }
}
