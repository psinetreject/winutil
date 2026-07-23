using System.Globalization;
using System.Windows.Data;

namespace WinUtil.App.Views;

/// <summary>
/// Two-way maps an enum value to a bool for radio-button groups: true when the bound value equals the
/// ConverterParameter (an enum member name); ConvertBack returns that member when the radio is checked.
/// </summary>
public sealed class EnumBooleanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is not null
           && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string name
            ? Enum.Parse(targetType, name)
            : Binding.DoNothing;
}
