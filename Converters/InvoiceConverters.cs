using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TarazInvoice.Models;

namespace TarazInvoice.Converters
{
    public class StatusToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InvoiceStatus status)
            {
                return status.ToPersianString();
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusToColorBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InvoiceStatus status)
            {
                string hex = status.GetColorHex();
                return (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusToBgColorBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is InvoiceStatus status)
            {
                string hex = status.GetBgColorHex();
                return (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
            }
            return (SolidColorBrush)new BrushConverter().ConvertFrom("#F3F4F6")!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class CurrencyFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal d)
            {
                return $"{d:N0} تومان";
            }
            if (value is double db)
            {
                return $"{db:N0} تومان";
            }
            return "0 تومان";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
