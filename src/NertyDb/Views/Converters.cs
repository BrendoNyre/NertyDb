using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NertyDb.Models;

namespace NertyDb.Views
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = false;

            if (value is bool boolVal)
            {
                b = boolVal;
            }
            else if (value is int intVal)
            {
                b = intVal > 0;
            }
            else if (value is long longVal)
            {
                b = longVal > 0;
            }
            else if (value is double dblVal)
            {
                b = dblVal > 0;
            }
            else if (value != null)
            {
                b = true;
            }

            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isVisible = value is Visibility v && v == Visibility.Visible;
            return Invert ? !isVisible : isVisible;
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = value == null || (value is string s && string.IsNullOrWhiteSpace(s));
            if (Invert) isNull = !isNull;
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ChangeTypeToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ChangeType changeType)
            {
                return changeType switch
                {
                    ChangeType.Update => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")), // Amber
                    ChangeType.Insert => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")), // Green
                    ChangeType.Delete => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")), // Red
                    _ => Brushes.Gray
                };
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ChangeTypeToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ChangeType changeType)
            {
                return changeType switch
                {
                    ChangeType.Update => "ALTERAÇÃO (UPDATE)",
                    ChangeType.Insert => "INSERÇÃO (INSERT)",
                    ChangeType.Delete => "EXCLUSÃO (DELETE)",
                    _ => value.ToString() ?? ""
                };
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class NullableValueDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == DBNull.Value)
            {
                return "(NULL)";
            }
            if (value is DateTime dt)
            {
                if (dt.TimeOfDay == TimeSpan.Zero) return dt.ToString("dd/MM/yyyy");
                return dt.ToString("dd/MM/yyyy HH:mm:ss");
            }
            return value.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                if (string.Equals(s, "(NULL)", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(s))
                    return DBNull.Value;
                return s;
            }
            return value;
        }
    }
}
