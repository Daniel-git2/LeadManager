using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LeadManager.Converters;

/// <summary>true → Visible. Pass ConverterParameter="Invert" to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) != IsInverted(parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static bool IsInverted(object? parameter) => "Invert".Equals(parameter as string, StringComparison.OrdinalIgnoreCase);
}

/// <summary>null or empty text → Collapsed. Pass ConverterParameter="Invert" to flip.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is string text ? text.Length > 0 : value is not null;
        return hasValue != BoolToVisibilityConverter.IsInverted(parameter) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Lets a group of RadioButtons pick one string value: checked when the bound value equals the parameter.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter! : Binding.DoNothing;
}

/// <summary>
/// Formats dates the way people say them: "Today", "Yesterday", "Sep 26". With ConverterParameter="Due"
/// it describes a follow-up date instead ("Overdue · Sep 20", "Today", "Tomorrow", "Oct 1");
/// "DueShort" is the same without the overdue prefix, for narrow columns.
/// </summary>
public sealed class FriendlyDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime date)
        {
            return "";
        }
        var days = (date.Date - DateTime.Today).Days;
        var mode = parameter as string ?? "";
        var isDue = mode.StartsWith("Due", StringComparison.OrdinalIgnoreCase);
        return (isDue, days) switch
        {
            (_, 0) => "Today",
            (true, 1) => "Tomorrow",
            (true, < 0) when mode.Equals("Due", StringComparison.OrdinalIgnoreCase) => $"Overdue · {Short(date)}",
            (false, -1) => "Yesterday",
            _ => Short(date),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Short(DateTime date) =>
        date.ToString(date.Year == DateTime.Today.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.CurrentCulture);
}
