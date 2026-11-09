using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ScreenKeeper.Views;

/// <summary>
/// Sichtbar, wenn der Text nicht leer ist (z. B. Fehlermeldungen unter Eingabefeldern).
/// </summary>
public sealed class TextToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        return value is string text && text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        return Binding.DoNothing;
    }
}
