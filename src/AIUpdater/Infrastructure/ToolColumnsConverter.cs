using KeepShell.Views.Controls;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIUpdater.Infrastructure;

public sealed class ToolColumnsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [double width, double fontSize]) return 4;
        return Math.Max(1, CardColumnsPanel.ColumnCount(width - 185, 180 * fontSize / 14, 0, 4));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [DependencyProperty.UnsetValue, DependencyProperty.UnsetValue];
}
